using TrueInjector.Models;

namespace TrueInjector.UI;

/// <summary>A labelled enum value surfaced in the technique pickers.</summary>
public sealed record Choice<T>(T Value, string Label, string Hint)
{
    public override string ToString() => Label;
}

public static class OptionSets
{
    public static IReadOnlyList<Choice<LoadLibraryFlavor>> LoadLibrary { get; } =
    [
        new(LoadLibraryFlavor.Ansi, "LoadLibraryA", "Emits an ASCII payload path."),
        new(LoadLibraryFlavor.Unicode, "LoadLibraryW", "Emits a wide (UTF-16) payload path."),
    ];

    public static IReadOnlyList<Choice<ExecutionTechnique>> Execution { get; } =
    [
        new(ExecutionTechnique.CreateRemoteThread, "CreateRemoteThread", "Plain Win32 call — easy to spot, hard to beat."),
        new(ExecutionTechnique.RtlCreateUserThread, "RtlCreateUserThread", "ntdll's classic user-thread helper."),
        new(ExecutionTechnique.NtCreateThreadEx, "NtCreateThreadEx", "Undocumented workhorse, works everywhere."),
        new(ExecutionTechnique.QueueUserApc, "NtQueueApcThread", "Queues an APC on every thread of the target."),
        new(ExecutionTechnique.QueueUserApcEx, "NtQueueApcThreadEx", "Special APC reservation — quieter queue."),
    ];

    public static IReadOnlyList<Choice<AllocationTechnique>> Allocation { get; } =
    [
        new(AllocationTechnique.VirtualAllocEx, "VirtualAllocEx", "Stock Win32 allocation."),
        new(AllocationTechnique.NtAllocateVirtualMemory, "NtAllocateVirtualMemory", "ntdll allocator, rounds regions itself."),
        new(AllocationTechnique.NtCreateSection, "NtCreateSection", "Backed by a section object; view is pre-filled."),
        new(AllocationTechnique.CreateFileMapping, "CreateFileMapping", "Classic file-mapping allocation."),
        new(AllocationTechnique.VirtualAlloc2, "VirtualAlloc2", "Modern kernelbase API for x64 targets."),
    ];

    public static IReadOnlyList<Choice<WriteTechnique>> Write { get; } =
    [
        new(WriteTechnique.WriteProcessMemory, "WriteProcessMemory", "Stock Win32 write."),
        new(WriteTechnique.NtWriteVirtualMemory, "NtWriteVirtualMemory", "ntdll write primitive."),
        new(WriteTechnique.ZwWriteVirtualMemory, "ZwWriteVirtualMemory", "Kernel-side twin of NtWriteVirtualMemory."),
        new(WriteTechnique.NtSectionTransfer, "NtCreateSection + NtMapViewOfSection", "Transfers the image through shared sections — no explicit write call."),
        new(WriteTechnique.FileMappingTransfer, "CreateFileMapping + MapViewOfFile", "Maps the payload in straight from a file mapping."),
    ];

    public static IReadOnlyList<Choice<HandleTechnique>> Handles { get; } =
    [
        new(HandleTechnique.Kernel32, "OpenProcess + CloseHandle", "Canonical kernel32 handle lifecycle."),
        new(HandleTechnique.Ntdll, "NtOpenProcess + NtClose", "Direct ntdll handle lifecycle."),
    ];
}
