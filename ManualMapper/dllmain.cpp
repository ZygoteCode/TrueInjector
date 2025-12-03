#include "pch.h"
#include "ManualMapInjection.h"
#include <iostream>
#include <fstream>
#include <vector>

extern "C"
{
    __declspec(dllexport)
    int ManualMapDLL(DWORD processID, const char* dllPath)
    {
        HANDLE hProcess = OpenProcess(PROCESS_ALL_ACCESS, FALSE, processID);

        std::ifstream file(dllPath, std::ios::binary | std::ios::ate);
        size_t fileSize = file.tellg();
        file.seekg(0);

        std::vector<BYTE> dllData(fileSize);
        file.read(reinterpret_cast<char*>(dllData.data()), fileSize);
        file.close();

        if (Inject::InjectDllData(hProcess, dllData.data(), dllData.size(), dllPath))
        {
            Inject::PerformCleanup();
        }

        CloseHandle(hProcess);
    }
}

BOOL APIENTRY DllMain( HMODULE hModule,
                       DWORD  ul_reason_for_call,
                       LPVOID lpReserved
                     )
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