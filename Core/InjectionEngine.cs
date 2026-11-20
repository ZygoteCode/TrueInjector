using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using TrueInjector.Models;

namespace TrueInjector.Core;

public sealed record InjectionJob(uint ProcessId, string DllPath, InjectionSettings Settings, bool RandomizePayload, bool HidePayload);

public sealed record InjectionOutcome(bool Success, string Message);

/// <summary>
/// Faithful port of the legacy injection routine: every handle, allocation,
/// write and execution technique is preserved exactly.
/// </summary>
public static class InjectionEngine
{
    private const uint MemCommit = 0x00001000;
    private const uint MemReserve = 0x00002000;
    private const uint PageReadWrite = 4;
    private const uint SectionAllAccess = 0x10000000;
    private const uint SecCommit = 0x8000000;
    private const uint ViewShare = 1;
    private const uint FileMapWrite = 0x0002;
    private const int StatusSuccess = 0x00000000;

    private const string DosStubSignature = "This program cannot be run in DOS mode.";
    private static readonly char[] RandomCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

    #region Kernel32

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, uint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateFileMapping(IntPtr hFile, IntPtr lpFileMappingAttributes, uint flProtect, uint dwMaximumSizeHigh, uint dwMaximumSizeLow, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

    [DllImport("kernelbase.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc2(IntPtr processHandle, IntPtr baseAddress, UIntPtr size, AllocationTypeFlags allocationType, MemoryProtectionFlags protection, IntPtr extendedParameters, IntPtr parameterCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    #endregion

    #region NTDLL

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern IntPtr NtWriteVirtualMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer, uint numberOfBytesToWrite, ref uint numberOfBytesWritten);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern IntPtr ZwWriteVirtualMemory(IntPtr processHandle, IntPtr baseAddress, byte[] buffer, uint numberOfBytesToWrite, ref uint numberOfBytesWritten);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern IntPtr RtlCreateUserThread(IntPtr processHandle, IntPtr threadSecurity, bool createSuspended, int stackZeroBits, IntPtr stackReserved, IntPtr stackCommit, IntPtr startAddress, IntPtr parameter, ref IntPtr threadHandle, IntPtr clientId);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern IntPtr NtCreateThreadEx(ref IntPtr threadHandle, uint desiredAccess, IntPtr objectAttributes, IntPtr processHandle, IntPtr startAddress, IntPtr parameter, bool inCreateSuspended, int stackZeroBits, int sizeOfStack, int maximumStackSize, IntPtr attributeList);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueueApcThread(IntPtr threadHandle, IntPtr apcRoutine, IntPtr apcArgument1, IntPtr apcArgument2, IntPtr apcArgument3);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueueApcThreadEx(IntPtr threadHandle, IntPtr userApcReserveHandle, IntPtr apcRoutine, IntPtr apcArgument1, IntPtr apcArgument2, IntPtr apcArgument3);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtAllocateVirtualMemory(IntPtr processHandle, ref IntPtr baseAddress, IntPtr zeroBits, ref ulong regionSize, uint allocationType, uint protect);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtCreateSection(out IntPtr sectionHandle, uint desiredAccess, IntPtr objectAttributes, ref long maximumSize, uint sectionPageProtection, uint allocationAttributes, IntPtr fileHandle);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtMapViewOfSection(IntPtr sectionHandle, IntPtr processHandle, out IntPtr baseAddress, IntPtr zeroBits, IntPtr commitSize, out long sectionOffset, out ulong viewSize, uint inheritDisposition, uint allocationType, uint win32Protect);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtOpenProcess(out IntPtr processHandle, uint desiredAccess, ref ObjectAttributes objectAttributes, ref ClientId clientId);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtOpenThread(out IntPtr threadHandle, uint desiredAccess, ref ObjectAttributes objectAttributes, ref ClientId clientId);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtClose(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern uint NtUnmapViewOfSection(IntPtr processHandle, IntPtr baseAddress);

    #endregion

    #region Native companion modules

    [DllImport("ManualMapper.dll", EntryPoint = "ManualMapDLL", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int ManualMapDLL(uint processId, string dllPath);

    [DllImport("ThreadHijacker.dll", EntryPoint = "ThreadHijack", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int ThreadHijack(uint processId, string dllPath);

    #endregion

    [Flags]
    private enum AllocationTypeFlags : uint
    {
        MemCommit = 0x1000,
        MemReserve = 0x2000,
    }

    [Flags]
    private enum MemoryProtectionFlags : uint
    {
        PageReadWrite = 0x04,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClientId
    {
        public IntPtr UniqueProcess;
        public IntPtr UniqueThread;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ObjectAttributes
    {
        public uint Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    public static void InitializeObjectAttributes(ref ObjectAttributes attributes, uint attributesValue)
    {
        attributes = new ObjectAttributes
        {
            Length = (uint)Marshal.SizeOf<ObjectAttributes>(),
            RootDirectory = IntPtr.Zero,
            ObjectName = IntPtr.Zero,
            Attributes = attributesValue,
            SecurityDescriptor = IntPtr.Zero,
            SecurityQualityOfService = IntPtr.Zero,
        };
    }

    public static IntPtr OpenProcessNt(int processId, uint desiredAccess)
    {
        ClientId cid = new()
        {
            UniqueProcess = new IntPtr(processId),
            UniqueThread = IntPtr.Zero,
        };

        ObjectAttributes objAttr = new();
        InitializeObjectAttributes(ref objAttr, 0);

        int status = NtOpenProcess(out IntPtr hProcess, desiredAccess, ref objAttr, ref cid);

        return status != StatusSuccess ? IntPtr.Zero : hProcess;
    }

    public static bool CloseHandleNt(IntPtr handle)
    {
        return handle == IntPtr.Zero || NtClose(handle) == StatusSuccess;
    }

    public static IntPtr NtOpenThreadReplacement(int targetProcessId, int targetThreadId, uint desiredAccess)
    {
        ClientId clientId = new()
        {
            UniqueProcess = new IntPtr(targetProcessId),
            UniqueThread = new IntPtr(targetThreadId),
        };

        ObjectAttributes objAttributes = new()
        {
            Length = (uint)Marshal.SizeOf<ObjectAttributes>(),
        };

        uint status = NtOpenThread(out IntPtr hThread, desiredAccess, ref objAttributes, ref clientId);

        return status != StatusSuccess || hThread == IntPtr.Zero ? IntPtr.Zero : hThread;
    }

    /// <summary>
    /// Executes the job synchronously; invoke on a worker thread.
    /// </summary>
    public static InjectionOutcome Execute(InjectionJob job, Action<string>? log = null)
    {
        try
        {
            return job.Settings.Method switch
            {
                InjectionMethod.Standard => ExecuteStandard(job, log),
                InjectionMethod.ManualMapping => DispatchCompanion("ManualMapDLL", ManualMapDLL, job, log),
                InjectionMethod.ThreadHijacking => DispatchCompanion("ThreadHijack", ThreadHijack, job, log),
                InjectionMethod.NativeLoading => ExecuteNativeLoader(job, log),
                _ => new InjectionOutcome(false, "Unknown injection method."),
            };
        }
        catch (Exception ex)
        {
            return new InjectionOutcome(false, ex.Message);
        }
    }

    private static InjectionOutcome DispatchCompanion(string entryPoint, Func<uint, string, int> proc, InjectionJob job, Action<string>? log)
    {
        ValidateDll(job.DllPath);
        log?.Invoke($"{entryPoint}({job.ProcessId})");
        proc(job.ProcessId, job.DllPath);
        return new InjectionOutcome(true, $"{entryPoint} dispatched on process {job.ProcessId}.");
    }

    private static InjectionOutcome ExecuteNativeLoader(InjectionJob job, Action<string>? log)
    {
        ValidateDll(job.DllPath);
        log?.Invoke($"InjectLdrLoadDll({job.ProcessId})");

        bool ok = NativeLoader.InjectLdrLoadDll((int)job.ProcessId, job.DllPath);

        return ok
            ? new InjectionOutcome(true, "LdrLoadDll bootstrap executed.")
            : new InjectionOutcome(false, "The LdrLoadDll bootstrap failed.");
    }

    private static void ValidateDll(string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException("The payload DLL no longer exists.", dllPath);
        }

        if (!Path.GetExtension(dllPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected payload is not a DLL file.");
        }
    }

    private static InjectionOutcome ExecuteStandard(InjectionJob job, Action<string>? log)
    {
        ValidateDll(job.DllPath);

        InjectionSettings settings = job.Settings;
        uint processId = job.ProcessId;
        PayloadRandomizer random = PayloadRandomizer.Shared;

        IntPtr processHandle = settings.Handles switch
        {
            HandleTechnique.Kernel32 => OpenProcess(0x001F0FFF, false, (int)processId),
            HandleTechnique.Ntdll => OpenProcessNt((int)processId, 0x001F0FFF),
            _ => IntPtr.Zero,
        };

        if (processHandle == IntPtr.Zero)
        {
            return new InjectionOutcome(false, "Unable to open the target process (access denied?).");
        }

        try
        {
            string dllPath = job.DllPath;

            // Cloaking: overwrite the DOS stub text and drop the payload under a
            // random folder/name on the system drive, exactly like before.
            if (job.RandomizePayload)
            {
                byte[] dllContent = File.ReadAllBytes(dllPath);
                byte[] bytesToReplace = Encoding.UTF8.GetBytes(DosStubSignature);
                dllContent = ReplaceBytes(dllContent, bytesToReplace, random.Bytes(bytesToReplace.Length));

                string rootDir = Environment.GetFolderPath(Environment.SpecialFolder.System)[..1] + ":";

                if (!Directory.Exists(rootDir + "\\Temp"))
                {
                    Directory.CreateDirectory(rootDir + "\\Temp");
                }

                string folderName = random.String(RandomCharacters, random.NextInt(12, 64));

                if (Directory.Exists(rootDir + "\\Temp\\" + folderName))
                {
                    Directory.Delete(rootDir + "\\Temp\\" + folderName);
                }

                Directory.CreateDirectory(rootDir + "\\Temp\\" + folderName);
                dllPath = rootDir + "\\Temp\\" + folderName + "\\" + random.String(RandomCharacters, random.NextInt(6, 32)) + ".dll";
                File.WriteAllBytes(dllPath, dllContent);
            }

            if (job.HidePayload)
            {
                HideFile(dllPath);
            }

            byte[] dllBytes = settings.LoadLibrary switch
            {
                LoadLibraryFlavor.Ansi => Encoding.ASCII.GetBytes(dllPath + "\0"),
                _ => Encoding.Unicode.GetBytes(dllPath + "\0"),
            };

            uint dllSize = (uint)dllBytes.Length;

            IntPtr loadLibraryAddress = settings.LoadLibrary switch
            {
                LoadLibraryFlavor.Ansi => GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA"),
                LoadLibraryFlavor.Unicode => GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW"),
                _ => IntPtr.Zero,
            };

            if (loadLibraryAddress == IntPtr.Zero)
            {
                return new InjectionOutcome(false, "Unable to resolve the LoadLibrary entry point.");
            }

            IntPtr allocatedMemoryAddress = IntPtr.Zero;
            bool memoryAlreadyFilled = false;

            switch (settings.Write)
            {
                case WriteTechnique.NtSectionTransfer:
                    allocatedMemoryAddress = TransferThroughNtSection(processHandle, dllBytes, settings.Handles);
                    memoryAlreadyFilled = true;
                    break;

                case WriteTechnique.FileMappingTransfer:
                    allocatedMemoryAddress = TransferThroughFileMapping(processHandle, dllBytes, settings.Handles);
                    memoryAlreadyFilled = true;
                    break;

                default:
                    // Legacy parity: allocating through a section or file mapping also
                    // fills the remote view immediately; the selected write call then
                    // runs on top of it, exactly like the original tool.
                    allocatedMemoryAddress = settings.Allocation switch
                    {
                        AllocationTechnique.NtCreateSection => TransferThroughNtSection(processHandle, dllBytes, settings.Handles),
                        AllocationTechnique.CreateFileMapping => TransferThroughFileMapping(processHandle, dllBytes, settings.Handles),
                        AllocationTechnique.VirtualAllocEx => VirtualAllocEx(processHandle, IntPtr.Zero, dllSize, MemCommit | MemReserve, PageReadWrite),
                        AllocationTechnique.NtAllocateVirtualMemory => AllocateThroughNt(processHandle, dllSize),
                        AllocationTechnique.VirtualAlloc2 => VirtualAlloc2(processHandle, IntPtr.Zero, (UIntPtr)dllSize, AllocationTypeFlags.MemReserve | AllocationTypeFlags.MemCommit, MemoryProtectionFlags.PageReadWrite, IntPtr.Zero, IntPtr.Zero),
                        _ => IntPtr.Zero,
                    };
                    break;
            }

            if (allocatedMemoryAddress == IntPtr.Zero)
            {
                return new InjectionOutcome(false, "The memory allocation call failed.");
            }

            if (!memoryAlreadyFilled)
            {
                WritePayload(processHandle, allocatedMemoryAddress, dllBytes, dllSize, settings.Write);
            }

            DispatchPayload(processId, processHandle, settings, loadLibraryAddress, allocatedMemoryAddress);

            log?.Invoke(TechniqueDescriptions.Pipeline(settings));
            return new InjectionOutcome(true, $"Payload handed off to process {processId}.");
        }
        finally
        {
            if (settings.Handles == HandleTechnique.Kernel32)
            {
                CloseHandle(processHandle);
            }
            else
            {
                CloseHandleNt(processHandle);
            }
        }
    }

    private static IntPtr TransferThroughNtSection(IntPtr processHandle, byte[] dllBytes, HandleTechnique handles)
    {
        uint dllSize = (uint)dllBytes.Length;
        long maxSize = dllSize;

        NtCreateSection(out IntPtr sectionHandle, SectionAllAccess, IntPtr.Zero, ref maxSize, PageReadWrite, SecCommit, IntPtr.Zero);

        NtMapViewOfSection(sectionHandle, (IntPtr)(-1), out IntPtr localBase, IntPtr.Zero, IntPtr.Zero, out long offset, out ulong viewSize, ViewShare, 0, PageReadWrite);
        Marshal.Copy(dllBytes, 0, localBase, dllBytes.Length);

        offset = 0;
        viewSize = 0;

        NtMapViewOfSection(sectionHandle, processHandle, out IntPtr remoteBase, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PageReadWrite);

        NtUnmapViewOfSection((IntPtr)(-1), localBase);

        if (handles == HandleTechnique.Kernel32)
        {
            CloseHandle(sectionHandle);
        }
        else
        {
            CloseHandleNt(sectionHandle);
        }

        return remoteBase;
    }

    private static IntPtr TransferThroughFileMapping(IntPtr processHandle, byte[] dllBytes, HandleTechnique handles)
    {
        uint dllSize = (uint)dllBytes.Length;

        IntPtr hSection = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, PageReadWrite, 0, dllSize, null);
        IntPtr localView = MapViewOfFile(hSection, FileMapWrite, 0, 0, (UIntPtr)dllSize);
        Marshal.Copy(dllBytes, 0, localView, dllBytes.Length);

        NtMapViewOfSection(hSection, processHandle, out IntPtr remoteBase, IntPtr.Zero, IntPtr.Zero, out _, out _, ViewShare, 0, PageReadWrite);

        UnmapViewOfFile(localView);

        if (handles == HandleTechnique.Kernel32)
        {
            CloseHandle(hSection);
        }
        else
        {
            CloseHandleNt(hSection);
        }

        return remoteBase;
    }

    private static IntPtr AllocateThroughNt(IntPtr processHandle, uint dllSize)
    {
        IntPtr baseAddress = IntPtr.Zero;
        ulong regionSize = dllSize;
        NtAllocateVirtualMemory(processHandle, ref baseAddress, IntPtr.Zero, ref regionSize, MemCommit | MemReserve, PageReadWrite);
        return baseAddress;
    }

    private static void WritePayload(IntPtr processHandle, IntPtr address, byte[] dllBytes, uint dllSize, WriteTechnique write)
    {
        switch (write)
        {
            case WriteTechnique.WriteProcessMemory:
                WriteProcessMemory(processHandle, address, dllBytes, dllSize, 0);
                break;

            case WriteTechnique.NtWriteVirtualMemory:
            {
                uint bytesWritten = 0;
                NtWriteVirtualMemory(processHandle, address, dllBytes, dllSize, ref bytesWritten);
                break;
            }

            case WriteTechnique.ZwWriteVirtualMemory:
            {
                uint bytesWritten = 0;
                ZwWriteVirtualMemory(processHandle, address, dllBytes, dllSize, ref bytesWritten);
                break;
            }
        }
    }

    private static void DispatchPayload(uint processId, IntPtr processHandle, InjectionSettings settings, IntPtr loadLibraryAddress, IntPtr allocatedMemoryAddress)
    {
        switch (settings.Execution)
        {
            case ExecutionTechnique.CreateRemoteThread:
                CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddress, allocatedMemoryAddress, 0, IntPtr.Zero);
                break;

            case ExecutionTechnique.RtlCreateUserThread:
            {
                IntPtr remoteThread = new(0);
                RtlCreateUserThread(processHandle, IntPtr.Zero, false, 0, IntPtr.Zero, IntPtr.Zero, loadLibraryAddress, allocatedMemoryAddress, ref remoteThread, IntPtr.Zero);
                break;
            }

            case ExecutionTechnique.NtCreateThreadEx:
            {
                IntPtr threadHandle = IntPtr.Zero;
                NtCreateThreadEx(ref threadHandle, 0x1FFFFF, IntPtr.Zero, processHandle, loadLibraryAddress, allocatedMemoryAddress, false, 0, 0, 0, IntPtr.Zero);
                break;
            }

            case ExecutionTechnique.QueueUserApc:
            {
                foreach (System.Diagnostics.ProcessThread thread in System.Diagnostics.Process.GetProcessById((int)processId).Threads)
                {
                    IntPtr hThread = OpenThreadFor(settings.Handles, processId, thread.Id, 0x0010);

                    if (hThread == IntPtr.Zero)
                    {
                        continue;
                    }

                    NtQueueApcThread(hThread, loadLibraryAddress, allocatedMemoryAddress, IntPtr.Zero, IntPtr.Zero);
                    ReleaseThreadHandle(settings.Handles, hThread);
                }

                break;
            }

            case ExecutionTechnique.QueueUserApcEx:
            {
                foreach (System.Diagnostics.ProcessThread thread in System.Diagnostics.Process.GetProcessById((int)processId).Threads)
                {
                    IntPtr hThread = OpenThreadFor(settings.Handles, processId, thread.Id, 0x1FFFFF);

                    if (hThread == IntPtr.Zero)
                    {
                        continue;
                    }

                    NtQueueApcThreadEx(hThread, IntPtr.Zero, loadLibraryAddress, allocatedMemoryAddress, IntPtr.Zero, IntPtr.Zero);
                    ReleaseThreadHandle(settings.Handles, hThread);
                }

                break;
            }
        }
    }

    private static IntPtr OpenThreadFor(HandleTechnique handles, uint processId, int threadId, uint access)
    {
        return handles switch
        {
            HandleTechnique.Kernel32 => OpenThread(access, false, (uint)threadId),
            HandleTechnique.Ntdll => NtOpenThreadReplacement((int)processId, threadId, access),
            _ => IntPtr.Zero,
        };
    }

    private static void ReleaseThreadHandle(HandleTechnique handles, IntPtr hThread)
    {
        if (handles == HandleTechnique.Kernel32)
        {
            CloseHandle(hThread);
        }
        else
        {
            CloseHandleNt(hThread);
        }
    }

    public static void HideFile(string file)
    {
        File.SetAttributes(file, FileAttributes.Hidden);
        FileInfo info = new(file);
        info.IsReadOnly = true;
    }

    private static int FindBytes(byte[] source, byte[] find)
    {
        int index = -1;
        int matchIndex = 0;

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == find[matchIndex])
            {
                if (matchIndex == find.Length - 1)
                {
                    index = i - matchIndex;
                    break;
                }

                matchIndex++;
            }
            else if (source[i] == find[0])
            {
                matchIndex = 1;
            }
            else
            {
                matchIndex = 0;
            }
        }

        return index;
    }

    private static byte[] ReplaceBytes(byte[] source, byte[] search, byte[] replacement)
    {
        int index = FindBytes(source, search);

        if (index < 0)
        {
            return source;
        }

        byte[] dst = new byte[source.Length - search.Length + replacement.Length];

        Buffer.BlockCopy(source, 0, dst, 0, index);
        Buffer.BlockCopy(replacement, 0, dst, index, replacement.Length);
        Buffer.BlockCopy(source, index + search.Length, dst, index + replacement.Length, source.Length - (index + search.Length));

        return dst;
    }
}
