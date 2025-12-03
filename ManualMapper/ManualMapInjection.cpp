#include "pch.h"
#include "ManualMapInjection.h"
#include <cstdio>
#include <random>
#include <iostream>
#include <vector>
#include <mutex>
#include <Windows.h>
#include <winternl.h>

#define INVALID_DATA_POINTER ((HINSTANCE)0x404040)
#define SEH_SUPPORT_FAILED ((HINSTANCE)0x505050)
constexpr DWORD SHELLCODE_SIZE = 0x1000;

struct InjectionInfo {
    HANDLE hProcess;
    BYTE* baseAddress;
    DWORD imageSize;
    DWORD originalImageSize;
    BYTE* allocationBase;
    std::string dllName;
};

static std::vector<InjectionInfo> g_injections;
static std::mutex g_injectionMutex;

#define RELOC_FLAG(RelInfo) (((RelInfo) >> 12) == IMAGE_REL_BASED_DIR64)

static BYTE* GenerateRandomBaseAddress(DWORD imageSize) {
    static std::random_device rd;
    static std::mt19937 gen(rd() ^ static_cast<unsigned int>(GetTickCount64()));

    std::uniform_int_distribution<ULONG_PTR> dist(0x10000000ULL, 0x7FF00000000ULL);

    ULONG_PTR baseAddr = dist(gen);

    baseAddr &= ~0xFFFF;
    baseAddr += 0x10000;

    if (baseAddr > (SIZE_T(-1) - imageSize)) {
        baseAddr = 0x10000000;
    }

    return reinterpret_cast<BYTE*>(baseAddr);
}

namespace Inject {
    typedef NTSTATUS(NTAPI* pNtWriteVirtualMemory)
        (
            IN HANDLE ProcessHandle,
            IN PVOID BaseAddress,
            IN PVOID Buffer,
            IN SIZE_T NumberOfBytesToWrite,
            OUT PSIZE_T NumberOfBytesWritten OPTIONAL
            );

    typedef NTSTATUS(NTAPI* pNtFreeVirtualMemory)
        (
            HANDLE ProcessHandle,
            PVOID* BaseAddress,
            PSIZE_T RegionSize,
            ULONG FreeType
            );

    typedef NTSTATUS(NTAPI* pNtReadVirtualMemory)
        (
            HANDLE ProcessHandle,
            PVOID BaseAddress,
            PVOID Buffer,
            SIZE_T BufferSize,
            PSIZE_T NumberOfBytesRead
            );

    typedef NTSTATUS(NTAPI* pNtCreateSection)
    (
        OUT PHANDLE            SectionHandle,
        IN  ACCESS_MASK        DesiredAccess,
        IN  POBJECT_ATTRIBUTES ObjectAttributes OPTIONAL,
        IN  PLARGE_INTEGER     MaximumSize OPTIONAL,
        IN  ULONG              SectionPageProtection,
        IN  ULONG              AllocationAttributes,
        IN  HANDLE             FileHandle OPTIONAL
    );

    typedef enum _SECTION_INHERIT
    {
        ViewShare = 1, // The mapped view of the section will be mapped into any child processes created by the process.
        ViewUnmap = 2  // The mapped view of the section will not be mapped into any child processes created by the process.
    } SECTION_INHERIT;

    typedef NTSTATUS(NTAPI* pNtMapViewOfSection)(
        IN        HANDLE          SectionHandle,
        IN        HANDLE          ProcessHandle,
        IN OUT    PVOID* BaseAddress,
        IN        ULONG_PTR       ZeroBits,
        IN        SIZE_T          CommitSize,
        IN OUT    PLARGE_INTEGER  SectionOffset OPTIONAL,
        IN OUT    PSIZE_T         ViewSize,
        IN        SECTION_INHERIT InheritDisposition,
        IN        ULONG           AllocationType,
        IN        ULONG           Win32Protect
    );

    typedef NTSTATUS(NTAPI* pNtUnmapViewOfSection)(
        HANDLE ProcessHandle,
        PVOID BaseAddress
        );

    typedef NTSTATUS(NTAPI* pNtAllocateVirtualMemory)(
        IN     HANDLE    ProcessHandle,
        IN OUT PVOID* BaseAddress,
        IN     ULONG_PTR ZeroBits,
        IN OUT PSIZE_T   RegionSize,
        IN     ULONG     AllocationType,
        IN     ULONG     Protect
     );

    typedef NTSTATUS(NTAPI* pNtProtectVirtualMemory)(
        HANDLE ProcessHandle,
        PVOID* BaseAddress,
        PSIZE_T RegionSize,
        ULONG NewProtect,
        PULONG OldProtect
        );

    typedef enum _MEMORY_INFORMATION_CLASS {
        MemoryBasicInformation,
        MemoryWorkingSetList,
        MemorySectionName,
        MemoryBasicVlmInformation
    } MEMORY_INFORMATION_CLASS;

    typedef NTSTATUS(NTAPI* pNtQueryVirtualMemory)
    (
        HANDLE ProcessHandle,
        PVOID BaseAddress,
        MEMORY_INFORMATION_CLASS MemoryInformationClass,
        PVOID MemoryInformation,
        SIZE_T MemoryInformationLength,
        PSIZE_T ReturnLength
    );

    typedef NTSTATUS(NTAPI* pNtWaitForSingleObject)
    (
        IN HANDLE Handle,
        IN BOOLEAN Alertable,
        IN PLARGE_INTEGER Timeout OPTIONAL
     );

    typedef NTSTATUS(NTAPI* pNtCreateThreadEx)(
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
        OUT PVOID /* PCLIENT_ID */ clientId
        );

    typedef NTSTATUS(NTAPI* pNtClose)
    (
        IN HANDLE Handle
    );

    pNtWriteVirtualMemory NtWriteVirtualMemory;
    pNtFreeVirtualMemory NtFreeVirtualMemory;
    pNtReadVirtualMemory NtReadVirtualMemory;
    pNtCreateSection NtCreateSection;
    pNtMapViewOfSection NtMapViewOfSection;
    pNtUnmapViewOfSection NtUnmapViewOfSection;
    pNtAllocateVirtualMemory NtAllocateVirtualMemory;
    pNtProtectVirtualMemory NtProtectVirtualMemory;
    pNtQueryVirtualMemory NtQueryVirtualMemory;
    pNtWaitForSingleObject NtWaitForSingleObject;
    pNtCreateThreadEx NtCreateThreadEx;
    pNtClose NtClose;

    bool ntdllLoaded = false;

    static void TrackInjection(HANDLE hProcess, BYTE* baseAddress, DWORD originalImageSize, const std::string& dllName) {
        std::lock_guard<std::mutex> lock(g_injectionMutex);

        MEMORY_BASIC_INFORMATION mbi = {};
        SIZE_T actualSize = originalImageSize;
        BYTE* allocationBase = baseAddress;

        if (VirtualQueryEx(hProcess, baseAddress, &mbi, sizeof(mbi)) != 0) {
            actualSize = mbi.RegionSize;
            allocationBase = (BYTE*)mbi.AllocationBase;
        }

        InjectionInfo info;
        info.hProcess = hProcess;
        info.baseAddress = baseAddress;
        info.imageSize = static_cast<DWORD>(actualSize);
        info.originalImageSize = originalImageSize;
        info.allocationBase = allocationBase;
        info.dllName = dllName;

        g_injections.push_back(info);
    }

    void InitNtDll()
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
        NtWriteVirtualMemory = (pNtWriteVirtualMemory)GetProcAddress(hNtdll, "NtWriteVirtualMemory");
        NtFreeVirtualMemory = (pNtFreeVirtualMemory)GetProcAddress(hNtdll, "NtFreeVirtualMemory");
        NtReadVirtualMemory = (pNtReadVirtualMemory)GetProcAddress(hNtdll, "NtReadVirtualMemory");
        NtCreateSection = (pNtCreateSection)GetProcAddress(hNtdll, "NtCreateSection");
        NtMapViewOfSection = (pNtMapViewOfSection)GetProcAddress(hNtdll, "NtMapViewOfSection");
        NtUnmapViewOfSection = (pNtUnmapViewOfSection)GetProcAddress(hNtdll, "NtUnmapViewOfSection");
        NtAllocateVirtualMemory = (pNtAllocateVirtualMemory)GetProcAddress(hNtdll, "NtAllocateVirtualMemory");
        NtQueryVirtualMemory = (pNtQueryVirtualMemory)GetProcAddress(hNtdll, "NtQueryVirtualMemory");
        NtProtectVirtualMemory = (pNtProtectVirtualMemory)GetProcAddress(hNtdll, "NtProtectVirtualMemory");
        NtWaitForSingleObject = (pNtWaitForSingleObject)GetProcAddress(hNtdll, "NtWaitForSingleObject");
        NtCreateThreadEx = (pNtCreateThreadEx)GetProcAddress(hNtdll, "NtCreateThreadEx");
        NtClose = (pNtClose)GetProcAddress(hNtdll, "NtClose");
    }

    void FreeRemoteMemory(HANDLE processHandle, LPVOID ptr)
    {
        NtFreeVirtualMemory(processHandle, &ptr, 0, MEM_RELEASE);
    }

    void PerformCleanup() {
        std::lock_guard<std::mutex> lock(g_injectionMutex);

        for (auto it = g_injections.begin(); it != g_injections.end(); ) {
            MEMORY_BASIC_INFORMATION mbi = {};
            NTSTATUS status;

            SIZE_T returnLength;
            status = NtQueryVirtualMemory(
                it->hProcess,
                it->baseAddress,
                MemoryBasicInformation,
                &mbi,
                sizeof(mbi),
                &returnLength
            );

            if (!NT_SUCCESS(status) || returnLength == 0) {
                it = g_injections.erase(it);
                continue;
            }

            SIZE_T wipeSize = it->originalImageSize;

            DWORD oldProtect = 0;
            PVOID protectBase = it->baseAddress;
            SIZE_T protectSize = wipeSize;

            status = NtProtectVirtualMemory(
                it->hProcess,
                &protectBase,
                &protectSize,
                PAGE_READWRITE,
                &oldProtect
            );

            if (NT_SUCCESS(status)) {
                const SIZE_T CHUNK_SIZE = 64 * 1024;

                for (SIZE_T offset = 0; offset < wipeSize; offset += CHUNK_SIZE) {
                    SIZE_T chunkSize = min(CHUNK_SIZE, wipeSize - offset);
                    BYTE* chunkAddr = it->baseAddress + offset;

                    BYTE* zeroBuffer = new(std::nothrow) BYTE[chunkSize];
                    if (zeroBuffer) {
                        memset(zeroBuffer, 0, chunkSize);

                        SIZE_T bytesWritten = 0;
                        NtWriteVirtualMemory(it->hProcess, chunkAddr, zeroBuffer, chunkSize, &bytesWritten);

                        delete[] zeroBuffer;
                    }
                }
            }

            PVOID freeBase = it->allocationBase;
            SIZE_T freeSize = 0;
            ULONG freeType = MEM_RELEASE;

            status = NtFreeVirtualMemory(
                it->hProcess,
                &freeBase,
                &freeSize,
                freeType
            );

            if (!NT_SUCCESS(status)) {
                freeBase = it->baseAddress;
                freeSize = 0;

                status = NtFreeVirtualMemory(
                    it->hProcess,
                    &freeBase,
                    &freeSize,
                    freeType
                );
            }

            it = g_injections.erase(it);
        }
    }

    size_t GetTrackedInjectionCount() {
        std::lock_guard<std::mutex> lock(g_injectionMutex);
        return g_injections.size();
    }

    void CleanupInjectionTracking() {
        std::lock_guard<std::mutex> lock(g_injectionMutex);
        g_injections.clear();
    }

    void* MapSectionInMemory(HANDLE hProc, HANDLE hLocalProc, SIZE_T size, const void* dataBuffer) {
        HANDLE sectionHandle = NULL;
        LARGE_INTEGER maxSize;
        maxSize.QuadPart = size;
        NTSTATUS status;
        PVOID remoteBase = NULL;
        PVOID localBase = NULL;

        status = NtCreateSection(
            &sectionHandle,
            SECTION_ALL_ACCESS,
            NULL,
            &maxSize,
            PAGE_EXECUTE_READWRITE,
            SEC_COMMIT,
            NULL
        );

        if (!NT_SUCCESS(status)) {
            return nullptr;
        }

        LARGE_INTEGER offset = { 0 };
        SIZE_T viewSize = 0;

        status = NtMapViewOfSection(
            sectionHandle,
            hLocalProc,
            &localBase,
            0, 0, &offset, &viewSize,
            ViewShare,
            0,
            PAGE_READWRITE
        );

        if (!NT_SUCCESS(status) || localBase == NULL) {
            NtClose(sectionHandle);
            return nullptr;
        }

        if (dataBuffer != nullptr && size > 0) {
            memcpy(localBase, dataBuffer, size);
        }

        offset.QuadPart = 0;
        viewSize = 0;

        status = NtMapViewOfSection(
            sectionHandle,
            hProc,
            &remoteBase,
            0, 0, &offset, &viewSize,
            ViewShare,
            0,
            PAGE_EXECUTE_READWRITE
        );

        NtUnmapViewOfSection(hLocalProc, localBase);
        NtClose(sectionHandle);

        if (!NT_SUCCESS(status) || remoteBase == NULL) {
            return nullptr;
        }

        return remoteBase;
    }

    bool ManualMapDll(HANDLE hProc, BYTE* pSrcData, bool SEHExceptionSupport, DWORD fdwReason, LPVOID lpReserved, const std::string& dllName) {
        if (reinterpret_cast<IMAGE_DOS_HEADER*>(pSrcData)->e_magic != 0x5A4D) {
            return false;
        }

        const auto pOldNtHeader = reinterpret_cast<IMAGE_NT_HEADERS*>(pSrcData + reinterpret_cast<IMAGE_DOS_HEADER*>(pSrcData)->e_lfanew);
        const auto pOldOptHeader = &pOldNtHeader->OptionalHeader;
        const auto pOldFileHeader = &pOldNtHeader->FileHeader;

        BYTE* pTargetBase = nullptr;
        SIZE_T ImageSize = pOldOptHeader->SizeOfImage;

        for (int attempts = 0; attempts < 3 && !pTargetBase; ++attempts) {
            PVOID randomAddr = GenerateRandomBaseAddress(ImageSize);
            PVOID allocationBase = randomAddr;
            SIZE_T currentSize = ImageSize;

            NTSTATUS status = NtAllocateVirtualMemory(
                hProc,
                &allocationBase,
                0,
                &currentSize,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_EXECUTE_READWRITE
            );

            if (NT_SUCCESS(status)) {
                pTargetBase = reinterpret_cast<BYTE*>(allocationBase);
            }
        }

        if (!pTargetBase) {
            PVOID allocationBase = nullptr;
            SIZE_T currentSize = ImageSize;

            NTSTATUS status = NtAllocateVirtualMemory(
                hProc,
                &allocationBase,
                0,
                &currentSize,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_EXECUTE_READWRITE
            );

            if (NT_SUCCESS(status)) {
                pTargetBase = reinterpret_cast<BYTE*>(allocationBase);
            }
        }

        if (!pTargetBase) {
            return false;
        }

        NTSTATUS writeStatus0 = NtWriteVirtualMemory(hProc, pTargetBase, pSrcData, pOldOptHeader->SizeOfHeaders, nullptr);

        if (!NT_SUCCESS(writeStatus0)) {
            FreeRemoteMemory(hProc, pTargetBase);
            return false;
        }

        auto pSectionHeader = IMAGE_FIRST_SECTION(pOldNtHeader);
        for (UINT i = 0; i < pOldFileHeader->NumberOfSections; ++i, ++pSectionHeader) {
            NTSTATUS writeStatus = NtWriteVirtualMemory(hProc, pTargetBase + pSectionHeader->VirtualAddress,
                pSrcData + pSectionHeader->PointerToRawData,
                pSectionHeader->SizeOfRawData, nullptr);

            if (pSectionHeader->SizeOfRawData > 0 &&
                !NT_SUCCESS(writeStatus)) {
                FreeRemoteMemory(hProc, pTargetBase);
                return false;
            }
        }

        MAPPING_CTX data{};
        data.fnLoadLib = LoadLibraryA;
        data.fnGetProc = GetProcAddress;
        data.baseAddr = pTargetBase;
        data.reason = fdwReason;
        data.reserved = lpReserved;
        data.sehEnabled = SEHExceptionSupport;
        data.fnAddTable = (pRtlAddFunctionTable)RtlAddFunctionTable;

        BYTE* MappingDataAlloc = reinterpret_cast<BYTE*>(MapSectionInMemory(hProc, GetCurrentProcess(), sizeof(data), &data));
        void* pShellcode = MapSectionInMemory(hProc, GetCurrentProcess(), SHELLCODE_SIZE, (void*)Shellcode);

        if (!pShellcode)
        {
            FreeRemoteMemory(hProc, pTargetBase);
            FreeRemoteMemory(hProc, MappingDataAlloc);
            FreeRemoteMemory(hProc, pShellcode);
            return false;
        }

        HANDLE hThread = NULL;
        
        NTSTATUS threadCreationStatus = NtCreateThreadEx(
            &hThread,
            THREAD_ALL_ACCESS,
            NULL,
            hProc,
            reinterpret_cast<LPTHREAD_START_ROUTINE>(pShellcode),
            MappingDataAlloc,
            0,
            0,
            0,
            0,
            NULL
        );

        if (!NT_SUCCESS(threadCreationStatus)) {
            FreeRemoteMemory(hProc, pTargetBase);
            FreeRemoteMemory(hProc, MappingDataAlloc);
            FreeRemoteMemory(hProc, pShellcode);
            return false;
        }

        NtWaitForSingleObject(hThread, FALSE, NULL);
        NtClose(hThread);

        MAPPING_CTX result{};
        NTSTATUS readStatus = NtReadVirtualMemory(hProc, MappingDataAlloc, &result, sizeof(result), nullptr);

        if (!NT_SUCCESS(readStatus)) {
            FreeRemoteMemory(hProc, pTargetBase);
            return false;
        }

        if (result.modHandle == INVALID_DATA_POINTER) {
            return false;
        }

        BYTE zero[SHELLCODE_SIZE] = {};
        NtWriteVirtualMemory(hProc, pShellcode, zero, sizeof(zero), nullptr);
        FreeRemoteMemory(hProc, pShellcode);
        FreeRemoteMemory(hProc, MappingDataAlloc);

        BYTE headerZeros[0x1000] = {};
        SIZE_T headerSize = min(0x1000, pOldOptHeader->SizeOfHeaders);
        NtWriteVirtualMemory(hProc, pTargetBase, headerZeros, headerSize, nullptr);

        TrackInjection(hProc, pTargetBase, pOldOptHeader->SizeOfImage, dllName);
        return true;
    }

#pragma runtime_checks("", off)
#pragma optimize("", off)

    void __stdcall Shellcode(MAPPING_CTX* pData) {
        if (!pData) {
            pData->modHandle = INVALID_DATA_POINTER;
            return;
        }

        BYTE* pBase = reinterpret_cast<BYTE*>(pData->baseAddr);
        auto* pOpt = &reinterpret_cast<IMAGE_NT_HEADERS*>(pBase + reinterpret_cast<IMAGE_DOS_HEADER*>(pBase)->e_lfanew)->OptionalHeader;
        auto _LoadLibraryA = pData->fnLoadLib;
        auto _GetProcAddress = pData->fnGetProc;
        auto _DllMain = reinterpret_cast<BOOL(WINAPI*)(void*, DWORD, void*)>(pBase + pOpt->AddressOfEntryPoint);

        BYTE* LocationDelta = pBase - pOpt->ImageBase;
        if (LocationDelta && pOpt->DataDirectory[IMAGE_DIRECTORY_ENTRY_BASERELOC].Size) {
            auto* pRelocData = reinterpret_cast<IMAGE_BASE_RELOCATION*>(pBase + pOpt->DataDirectory[IMAGE_DIRECTORY_ENTRY_BASERELOC].VirtualAddress);
            const auto* pRelocEnd = reinterpret_cast<BYTE*>(pRelocData) + pOpt->DataDirectory[IMAGE_DIRECTORY_ENTRY_BASERELOC].Size;

            while (reinterpret_cast<BYTE*>(pRelocData) < pRelocEnd && pRelocData->SizeOfBlock) {
                const UINT Count = (pRelocData->SizeOfBlock - sizeof(IMAGE_BASE_RELOCATION)) / sizeof(WORD);
                WORD* pRelativeInfo = reinterpret_cast<WORD*>(pRelocData + 1);

                for (UINT i = 0; i < Count; ++i) {
                    if (RELOC_FLAG(pRelativeInfo[i])) {
                        UINT_PTR* pPatch = reinterpret_cast<UINT_PTR*>(pBase + pRelocData->VirtualAddress + (pRelativeInfo[i] & 0xFFF));
                        *pPatch += reinterpret_cast<UINT_PTR>(LocationDelta);
                    }
                }
                pRelocData = reinterpret_cast<IMAGE_BASE_RELOCATION*>(reinterpret_cast<BYTE*>(pRelocData) + pRelocData->SizeOfBlock);
            }
        }

        if (pOpt->DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].Size) {
            auto* pImportDesc = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(pBase + pOpt->DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress);

            while (pImportDesc->Name) {
                HMODULE hMod = _LoadLibraryA(reinterpret_cast<char*>(pBase + pImportDesc->Name));
                if (!hMod) {
                    pData->modHandle = INVALID_DATA_POINTER;
                    return;
                }

                ULONG_PTR* pThunk = reinterpret_cast<ULONG_PTR*>(pBase + pImportDesc->OriginalFirstThunk);
                ULONG_PTR* pFunc = reinterpret_cast<ULONG_PTR*>(pBase + pImportDesc->FirstThunk);
                if (!pThunk) pThunk = pFunc;

                for (; *pThunk; ++pThunk, ++pFunc) {
                    if (IMAGE_SNAP_BY_ORDINAL(*pThunk)) {
                        *pFunc = reinterpret_cast<ULONG_PTR>(_GetProcAddress(hMod, reinterpret_cast<char*>(*pThunk & 0xFFFF)));
                    }
                    else {
                        *pFunc = reinterpret_cast<ULONG_PTR>(_GetProcAddress(hMod, reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(pBase + *pThunk)->Name));
                    }
                }
                ++pImportDesc;
            }
        }

        if (pOpt->DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS].Size) {
            auto* pTLS = reinterpret_cast<IMAGE_TLS_DIRECTORY*>(pBase + pOpt->DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS].VirtualAddress);
            PIMAGE_TLS_CALLBACK* pCallback = reinterpret_cast<PIMAGE_TLS_CALLBACK*>(pTLS->AddressOfCallBacks);
            for (; pCallback && *pCallback; ++pCallback) {
                (*pCallback)(pBase, DLL_PROCESS_ATTACH, nullptr);
            }
        }

        bool ExceptionSupportFailed = false;
        if (pData->sehEnabled) {
            const auto& excep = pOpt->DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
            if (excep.Size && !pData->fnAddTable(
                reinterpret_cast<IMAGE_RUNTIME_FUNCTION_ENTRY*>(pBase + excep.VirtualAddress),
                excep.Size / sizeof(IMAGE_RUNTIME_FUNCTION_ENTRY),
                reinterpret_cast<DWORD64>(pBase))) {
                ExceptionSupportFailed = true;
            }
        }

        BOOL dllResult = _DllMain(pBase, pData->reason, pData->reserved);
        pData->modHandle = ExceptionSupportFailed ? SEH_SUPPORT_FAILED : reinterpret_cast<HINSTANCE>(pBase);
    }

#pragma runtime_checks("", restore)
#pragma optimize("", on)

    bool InjectDllData(HANDLE hProcess, BYTE* dllData, size_t dllSize, const char* dllName) {
        InitNtDll();

        if (!hProcess || hProcess == INVALID_HANDLE_VALUE || !dllData || dllSize == 0) {
            return false;
        }

        bool result = ManualMapDll(hProcess, dllData, true, DLL_PROCESS_ATTACH, nullptr, dllName ? dllName : "");
        return result;
    }
}