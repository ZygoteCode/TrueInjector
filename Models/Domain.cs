namespace TrueInjector.Models;

public enum InjectionMethod
{
    Standard = 0,
    ManualMapping = 1,
    ThreadHijacking = 2,
    NativeLoading = 3,
}

public enum LoadLibraryFlavor
{
    Ansi = 0,
    Unicode = 1,
}

public enum ExecutionTechnique
{
    CreateRemoteThread = 0,
    RtlCreateUserThread = 1,
    NtCreateThreadEx = 2,
    QueueUserApc = 3,
    QueueUserApcEx = 4,
}

public enum AllocationTechnique
{
    VirtualAllocEx = 0,
    NtAllocateVirtualMemory = 1,
    NtCreateSection = 2,
    CreateFileMapping = 3,
    VirtualAlloc2 = 4,
}

public enum WriteTechnique
{
    WriteProcessMemory = 0,
    NtWriteVirtualMemory = 1,
    ZwWriteVirtualMemory = 2,
    NtSectionTransfer = 3,
    FileMappingTransfer = 4,
}

public enum HandleTechnique
{
    Kernel32 = 0,
    Ntdll = 1,
}

public enum ActivityKind
{
    Info,
    Success,
    Error,
    Action,
}

public static class TechniqueDescriptions
{
    public static string Describe(InjectionMethod method) => method switch
    {
        InjectionMethod.Standard => "Classic loader chain: allocate, write, start a thread.",
        InjectionMethod.ManualMapping => "Maps the image manually, skipping LoadLibrary entirely. Requires ManualMapper.dll.",
        InjectionMethod.ThreadHijacking => "Hijacks an existing thread to run the bootstrap. Requires ThreadHijacker.dll.",
        InjectionMethod.NativeLoading => "Uses ntdll's LdrLoadDll through a custom stub. Requires NativeLoader.",
        _ => string.Empty,
    };

    public static string Pipeline(InjectionSettings s)
    {
        if (s.Method == InjectionMethod.ManualMapping)
        {
            return "ManualMapDLL(pid, path)";
        }

        if (s.Method == InjectionMethod.ThreadHijacking)
        {
            return "ThreadHijack(pid, path)";
        }

        if (s.Method == InjectionMethod.NativeLoading)
        {
            return "NtCreateSection → LdrLoadDll stub → NtCreateThreadEx";
        }

        string open = s.Handles == HandleTechnique.Kernel32 ? "OpenProcess" : "NtOpenProcess";

        string alloc;
        string write;
        switch (s.Write)
        {
            case WriteTechnique.NtSectionTransfer:
                alloc = "NtCreateSection";
                write = "NtMapViewOfSection ×2";
                break;
            case WriteTechnique.FileMappingTransfer:
                alloc = "CreateFileMapping";
                write = "MapViewOfFile → NtMapViewOfSection";
                break;
            default:
                alloc = s.Allocation switch
                {
                    AllocationTechnique.VirtualAllocEx => "VirtualAllocEx",
                    AllocationTechnique.NtAllocateVirtualMemory => "NtAllocateVirtualMemory",
                    AllocationTechnique.NtCreateSection => "NtCreateSection",
                    AllocationTechnique.CreateFileMapping => "CreateFileMapping",
                    AllocationTechnique.VirtualAlloc2 => "VirtualAlloc2",
                    _ => "?",
                };
                write = s.Write switch
                {
                    WriteTechnique.WriteProcessMemory => "WriteProcessMemory",
                    WriteTechnique.NtWriteVirtualMemory => "NtWriteVirtualMemory",
                    WriteTechnique.ZwWriteVirtualMemory => "ZwWriteVirtualMemory",
                    _ => "?",
                };
                break;
        }

        string exec = s.Execution switch
        {
            ExecutionTechnique.CreateRemoteThread => "CreateRemoteThread",
            ExecutionTechnique.RtlCreateUserThread => "RtlCreateUserThread",
            ExecutionTechnique.NtCreateThreadEx => "NtCreateThreadEx",
            ExecutionTechnique.QueueUserApc => "NtQueueApcThread",
            ExecutionTechnique.QueueUserApcEx => "NtQueueApcThreadEx",
            _ => "?",
        };

        string close = s.Handles == HandleTechnique.Kernel32 ? "CloseHandle" : "NtClose";
        return $"{open} → {alloc} → {write} → {exec} → {close}";
    }
}

public sealed class InjectionSettings : Infrastructure.ObservableObject
{
    private InjectionMethod _method = InjectionMethod.Standard;
    private LoadLibraryFlavor _loadLibrary = LoadLibraryFlavor.Unicode;
    private ExecutionTechnique _execution = ExecutionTechnique.QueueUserApcEx;
    private AllocationTechnique _allocation = AllocationTechnique.NtCreateSection;
    private WriteTechnique _write = WriteTechnique.NtSectionTransfer;
    private HandleTechnique _handles = HandleTechnique.Ntdll;

    public InjectionMethod Method
    {
        get => _method;
        set
        {
            if (SetProperty(ref _method, value))
            {
                OnPropertyChanged(nameof(IsStandardPipeline));
                OnPropertyChanged(nameof(MethodHeader));
            }
        }
    }

    public LoadLibraryFlavor LoadLibrary
    {
        get => _loadLibrary;
        set
        {
            if (SetProperty(ref _loadLibrary, value))
            {
                OnPropertyChanged(nameof(MethodHeader));
            }
        }
    }

    public ExecutionTechnique Execution
    {
        get => _execution;
        set
        {
            if (SetProperty(ref _execution, value))
            {
                OnPropertyChanged(nameof(MethodHeader));
            }
        }
    }

    public AllocationTechnique Allocation
    {
        get => _allocation;
        set
        {
            if (SetProperty(ref _allocation, value))
            {
                OnPropertyChanged(nameof(MethodHeader));
            }
        }
    }

    public WriteTechnique Write
    {
        get => _write;
        set
        {
            if (SetProperty(ref _write, value))
            {
                OnPropertyChanged(nameof(AllocationManagedByWrite));
                OnPropertyChanged(nameof(MethodHeader));
            }
        }
    }

    public HandleTechnique Handles
    {
        get => _handles;
        set
        {
            if (SetProperty(ref _handles, value))
            {
                OnPropertyChanged(nameof(MethodHeader));
            }
        }
    }

    public bool IsStandardPipeline => Method == InjectionMethod.Standard;

    public bool AllocationManagedByWrite => IsStandardPipeline && Write is WriteTechnique.NtSectionTransfer or WriteTechnique.FileMappingTransfer;

    public string MethodHeader => Method switch
    {
        InjectionMethod.Standard => $"Standard · {TechniqueDescriptions.Pipeline(this)}",
        InjectionMethod.ManualMapping => "Manual mapping · ManualMapper.dll",
        InjectionMethod.ThreadHijacking => "Thread hijacking · ThreadHijacker.dll",
        InjectionMethod.NativeLoading => "Native loading · LdrLoadDll",
        _ => string.Empty,
    };
}
