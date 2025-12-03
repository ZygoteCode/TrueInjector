#include "pch.h"
#pragma once
#include <Windows.h>
#include <string>
#include <cstddef>

typedef struct _MAPPING_CTX
{
    LPVOID baseAddr;
    HINSTANCE(WINAPI* fnLoadLib)(LPCSTR);
    FARPROC(WINAPI* fnGetProc)(HMODULE, LPCSTR);
    HINSTANCE modHandle;
    DWORD reason;
    LPVOID reserved;
    BOOL sehEnabled;
    BOOL(WINAPI* fnAddTable)(PVOID, DWORD, DWORD64);
}
MAPPING_CTX, * PMAPPING_CTX;

typedef BOOL(WINAPI* pRtlAddFunctionTable)
(
    PVOID FunctionTable,
    DWORD EntryCount,
    DWORD64 BaseAddress
);

namespace Inject
{
    bool ManualMapDll
    (
        HANDLE hProc,
        BYTE* pSrcData,
        bool SEHExceptionSupport,
        DWORD fdwReason,
        LPVOID lpReserved,
        const std::string& dllName
    );

    bool InjectDllData
    (
        HANDLE hProcess,
        BYTE* dllData,
        size_t dllSize,
        const char* dllName
    );

    void __stdcall Shellcode(MAPPING_CTX* pData);
    void PerformCleanup();
    size_t GetTrackedInjectionCount();
    void CleanupInjectionTracking();
}