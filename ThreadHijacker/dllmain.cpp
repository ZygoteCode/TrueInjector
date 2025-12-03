#include "pch.h"
#include <windows.h>
#include <iostream>
#include <vector>
#include <cstring>
#include <algorithm>
#include <tlhelp32.h>
#include <winternl.h>

typedef NTSTATUS(NTAPI* pNtClose)
(
    IN HANDLE Handle
);

typedef NTSTATUS(NTAPI* pNtOpenProcess)
(
    OUT PHANDLE            ProcessHandle,
    IN  ACCESS_MASK        DesiredAccess,
    IN  POBJECT_ATTRIBUTES ObjectAttributes,
    IN  CLIENT_ID         ClientId
);

typedef NTSTATUS(NTAPI* pNtWriteVirtualMemory)
(
    IN HANDLE ProcessHandle,
    IN PVOID BaseAddress,
    IN PVOID Buffer,
    IN SIZE_T NumberOfBytesToWrite,
    OUT PSIZE_T NumberOfBytesWritten OPTIONAL
    );

typedef NTSTATUS(NTAPI* pNtAllocateVirtualMemory)(
    IN     HANDLE    ProcessHandle,
    IN OUT PVOID* BaseAddress,
    IN     ULONG_PTR ZeroBits,
    IN OUT PSIZE_T   RegionSize,
    IN     ULONG     AllocationType,
    IN     ULONG     Protect
    );


typedef NTSTATUS(NTAPI* pNtFreeVirtualMemory)
(
    HANDLE ProcessHandle,
    PVOID* BaseAddress,
    PSIZE_T RegionSize,
    ULONG FreeType
    );

typedef NTSTATUS(NTAPI* pNtGetContextThread)
(
    IN HANDLE ThreadHandle,
    IN OUT PCONTEXT ThreadContext
);

typedef NTSTATUS(NTAPI* pNtSetContextThread)
(
    IN HANDLE ThreadHandle,
    IN PCONTEXT ThreadContext
);

typedef NTSTATUS(NTAPI* pNtResumeThread)
(
    IN HANDLE ThreadHandle,
    OUT PULONG SuspendCount OPTIONAL
);

typedef NTSTATUS(NTAPI* pNtWaitForSingleObject)
(
    IN HANDLE Handle,
    IN BOOLEAN Alertable,
    IN PLARGE_INTEGER Timeout OPTIONAL
);

typedef NTSTATUS(NTAPI* pNtCreateThreadEx)
(
    OUT PHANDLE hThread,
    IN ACCESS_MASK DesiredAccess,
    IN PVOID ObjectAttributes,
    IN HANDLE ProcessHandle,
    IN PVOID StartRoutine,
    IN PVOID Argument,
    IN ULONG CreateFlags,
    IN SIZE_T ZeroBits,
    IN SIZE_T StackSize,
    IN SIZE_T MaximumStackSize,
    OUT PVOID clientId
);

pNtClose RealNtClose;
pNtOpenProcess NtOpenProcess;
pNtWriteVirtualMemory NtWriteVirtualMemory;
pNtAllocateVirtualMemory NtAllocateVirtualMemory;
pNtFreeVirtualMemory NtFreeVirtualMemory;
pNtGetContextThread NtGetContextThread;
pNtSetContextThread NtSetContextThread;
pNtResumeThread NtResumeThread;
pNtWaitForSingleObject RealNtWaitForSingleObject;
pNtCreateThreadEx NtCreateThreadEx;

struct HijackAllocation
{
    HANDLE hThread;
    LPVOID pRemoteAlloc;
    SIZE_T size;
};

const SIZE_T JMP_ABS_SIZE = 14;
const SIZE_T REGISTER_PUSH_SIZE = 8 * 6;
bool ntdllLoaded = false;

HANDLE OpenProcessNt(DWORD processId, ACCESS_MASK desiredAccess)
{
    CLIENT_ID cid{};
    cid.UniqueProcess = reinterpret_cast<HANDLE>(processId);
    cid.UniqueThread = nullptr;

    OBJECT_ATTRIBUTES objAttr{};
    InitializeObjectAttributes(&objAttr, nullptr, 0, nullptr, nullptr);

    HANDLE hProcess = nullptr;

    NTSTATUS status = NtOpenProcess(
        &hProcess,
        desiredAccess,
        &objAttr,
        (_CLIENT_ID) cid
    );

    if (status < 0)
        return nullptr;

    return hProcess;
}

void InitNTDLL()
{
    if (!ntdllLoaded)
    {
        ntdllLoaded = true;
    }
    else
    {
        return;
    }
    
    HMODULE hNtdll = LoadLibraryA("ntdll.dll");
    RealNtClose = (pNtClose)GetProcAddress(hNtdll, "NtClose");
    NtOpenProcess = (pNtOpenProcess)GetProcAddress(hNtdll, "NtOpenProcess");
    NtWriteVirtualMemory = (pNtWriteVirtualMemory)GetProcAddress(hNtdll, "NtWriteVirtualMemory");
    NtAllocateVirtualMemory = (pNtAllocateVirtualMemory)GetProcAddress(hNtdll, "NtAllocateVirtualMemory");
    NtFreeVirtualMemory = (pNtFreeVirtualMemory)GetProcAddress(hNtdll, "NtFreeVirtualMemory");
    NtGetContextThread = (pNtGetContextThread)GetProcAddress(hNtdll, "NtGetContextThread");
    NtSetContextThread = (pNtSetContextThread)GetProcAddress(hNtdll, "NtSetContextThread");
    NtResumeThread = (pNtResumeThread)GetProcAddress(hNtdll, "NtResumeThread");
    RealNtWaitForSingleObject = (pNtWaitForSingleObject)GetProcAddress(hNtdll, "NtWaitForSingleObject");
    NtCreateThreadEx = (pNtCreateThreadEx)GetProcAddress(hNtdll, "NtCreateThreadEx");
}

void FreeRemoteMemory(HANDLE processHandle, LPVOID ptr)
{
    NtFreeVirtualMemory(processHandle, &ptr, 0, MEM_RELEASE);
}

std::vector<BYTE> GenerateHijackShellcode(LPVOID pRemotePath)
{
    ULONG_PTR pLoadLibraryA = (ULONG_PTR)GetProcAddress(GetModuleHandleA("kernel32.dll"), "LoadLibraryA");
    ULONG_PTR pExitThread = (ULONG_PTR)GetProcAddress(GetModuleHandleA("kernel32.dll"), "ExitThread");
    std::vector<BYTE> shellcode;

    shellcode.insert(shellcode.end(), { 0x53, 0x55, 0x57, 0x56, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41, 0x57 });
    shellcode.insert(shellcode.end(), { 0x48, 0x83, 0xEC, 0x28 });
    shellcode.insert(shellcode.end(), { 0x48, 0xB9 });
    shellcode.insert(shellcode.end(), (BYTE*)&pRemotePath, (BYTE*)&pRemotePath + 8);
    shellcode.insert(shellcode.end(), { 0x48, 0xB8 });
    shellcode.insert(shellcode.end(), (BYTE*)&pLoadLibraryA, (BYTE*)&pLoadLibraryA + 8);
    shellcode.insert(shellcode.end(), { 0xFF, 0xD0 });
    shellcode.insert(shellcode.end(), { 0x48, 0x83, 0xC4, 0x28 });
    shellcode.insert(shellcode.end(), { 0x48, 0xC7, 0xC1, 0x00, 0x00, 0x00, 0x00 });
    shellcode.insert(shellcode.end(), { 0x48, 0xB8 });
    shellcode.insert(shellcode.end(), (BYTE*)&pExitThread, (BYTE*)&pExitThread + 8);
    shellcode.insert(shellcode.end(), { 0xFF, 0xE0 });

    return shellcode;
}

bool PerformThreadHijacking(DWORD dwProcessId, const char* dllPath)
{
    InitNTDLL();
    HANDLE hProc = OpenProcessNt(dwProcessId, PROCESS_ALL_ACCESS);

    if (!hProc)
    {
        return false;
    }

    SIZE_T pathSize = strlen(dllPath) + 1;
    SIZE_T codeSize = 1024;

    SIZE_T regionSize = codeSize + pathSize;
    LPVOID pRemoteAlloc = NULL;
    NTSTATUS allocationStatus = NtAllocateVirtualMemory(hProc, &pRemoteAlloc, 0, &regionSize, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);

    if (!NT_SUCCESS(allocationStatus) || pRemoteAlloc == NULL)
    {
        RealNtClose(hProc);
        return false;
    }

    LPVOID pRemoteCode = pRemoteAlloc;
    LPVOID pRemotePath = (LPVOID)((ULONG_PTR)pRemoteAlloc + codeSize);
    NTSTATUS writeStatus1 = NtWriteVirtualMemory(hProc, pRemotePath, (PVOID) dllPath, pathSize, NULL);

    if (!NT_SUCCESS(writeStatus1))
    {
        FreeRemoteMemory(hProc, pRemoteAlloc);
        RealNtClose(hProc);
        return false;
    }

    HANDLE hThread = NULL;
    NTSTATUS threadCreationStatus = NtCreateThreadEx(&hThread, THREAD_ALL_ACCESS, NULL, hProc, (PVOID)0, (PVOID)0, 0x00000001, 0, 0, 0, NULL );

    if (!NT_SUCCESS(threadCreationStatus))
    {
        FreeRemoteMemory(hProc, pRemoteAlloc);
        RealNtClose(hProc);
        return false;
    }

    std::vector<BYTE> shellcodeBuffer = GenerateHijackShellcode(pRemotePath);
    NTSTATUS writeStatus2 = NtWriteVirtualMemory(hProc, pRemoteCode, shellcodeBuffer.data(), shellcodeBuffer.size(), NULL);

    if (!NT_SUCCESS(writeStatus2))
    {
        RealNtClose(hThread);
        FreeRemoteMemory(hProc, pRemoteAlloc);
        RealNtClose(hProc);
        return false;
    }

    CONTEXT ctx = {};
    ctx.ContextFlags = CONTEXT_CONTROL;

    NTSTATUS threadContextStatus = NtGetContextThread(hThread, &ctx);

    if (!NT_SUCCESS(threadContextStatus))
    {
        RealNtClose(hThread);
        FreeRemoteMemory(hProc, pRemoteAlloc);
        RealNtClose(hProc);
        return false;
    }

    ctx.Rip = (ULONG_PTR)pRemoteCode;
    NtSetContextThread(hThread, &ctx);

    ULONG previousSuspendCount = 0;
    NtResumeThread(hThread, &previousSuspendCount);
    RealNtWaitForSingleObject(hThread, FALSE, NULL);
    FreeRemoteMemory(hProc, pRemoteAlloc);

    RealNtClose(hThread);
    RealNtClose(hProc);

    return true;
}

extern "C"
{
    __declspec(dllexport)
    void ThreadHijack(DWORD dwProcessId, const char* dllPath)
    {
        PerformThreadHijacking(dwProcessId, dllPath);
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
        case DLL_PROCESS_ATTACH:
        case DLL_THREAD_ATTACH:
        case DLL_THREAD_DETACH:
        case DLL_PROCESS_DETACH:
            break;
    }

    return TRUE;
}