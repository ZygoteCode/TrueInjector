#pragma once
#include <Windows.h>

#ifdef __cplusplus
extern "C" {
#endif

    // Tipi base per NtAPI
    typedef LONG NTSTATUS;

    // Macro di stato comune
#define STATUS_SUCCESS ((NTSTATUS)0x00000000L)

// NtWriteVirtualMemory - versione sicura (solo self-process)
    typedef NTSTATUS(NTAPI* NtWriteVirtualMemory_t)(
        HANDLE ProcessHandle,
        PVOID BaseAddress,
        PVOID Buffer,
        ULONG NumberOfBytesToWrite,
        PULONG NumberOfBytesWritten
        );

#ifdef __cplusplus
}
#endif