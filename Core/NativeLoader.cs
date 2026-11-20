using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TrueInjector.Core;

/// <summary>
/// Loads a DLL through ntdll's LdrLoadDll driven by a tiny remote stub.
/// Ported from the original tool without behavioural changes.
/// </summary>
public static class NativeLoader
{
    public const uint ProcessAllAccess = 0x1F0FFF;
    public const uint ThreadAllAccess = 0x1F03FF;

    private const uint PageReadWrite = 4;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint SectionAllAccess = 0x10000000;
    private const uint SecCommit = 0x8000000;
    private const uint ViewShare = 1;
    private const int StatusSuccess = 0x00000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtCreateSection(out IntPtr sectionHandle, uint desiredAccess, IntPtr objectAttributes, ref long maximumSize, uint sectionPageProtection, uint allocationAttributes, IntPtr fileHandle);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtMapViewOfSection(IntPtr sectionHandle, IntPtr processHandle, out IntPtr baseAddress, IntPtr zeroBits, IntPtr commitSize, out long sectionOffset, out ulong viewSize, uint inheritDisposition, uint allocationType, uint win32Protect);

    [DllImport("ntdll.dll")]
    private static extern uint NtUnmapViewOfSection(IntPtr processHandle, IntPtr baseAddress);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtCreateThreadEx(out IntPtr threadHandle, uint desiredAccess, IntPtr objectAttributes, IntPtr processHandle, IntPtr startAddress, IntPtr parameter, bool inCreateSuspended, int stackZeroBits, int sizeOfStackCommit, int maximumStackSize, IntPtr attributeList);

    private static byte[] GenerateLdrLoadDllShellcode(IntPtr ldrLoadDllAddress, IntPtr remoteUnicodeString, IntPtr sleepExAddress)
    {
        using MemoryStream ms = new();

        ms.WriteByte(0x55);                         // push rbp
        ms.Write([0x48, 0x89, 0xE5]);               // mov rbp, rsp
        ms.Write([0x48, 0x83, 0xEC, 0x20]);         // sub rsp, 0x20
        ms.Write([0x48, 0x31, 0xC9]);               // xor rcx, rcx
        ms.Write([0x48, 0x31, 0xD2]);               // xor rdx, rdx
        ms.Write([0x49, 0xB8]);                     // mov r8, imm64
        ms.Write(BitConverter.GetBytes(remoteUnicodeString.ToInt64()));
        ms.Write([0x49, 0x31, 0xC9]);               // xor r9, r9
        ms.Write([0x48, 0xB8]);                     // mov rax, imm64
        ms.Write(BitConverter.GetBytes(ldrLoadDllAddress.ToInt64()));
        ms.Write([0xFF, 0xD0]);                     // call rax
        ms.Write([0x48, 0x83, 0xC4, 0x20]);         // add rsp, 0x20
        ms.WriteByte(0x5D);                         // pop rbp
        ms.Write([0x48, 0xC7, 0xC1, 0xFF, 0xFF, 0xFF, 0xFF]); // mov rcx, -1
        ms.Write([0x48, 0x31, 0xD2]);               // xor rdx, rdx
        ms.Write([0x48, 0xB8]);                     // mov rax, imm64
        ms.Write(BitConverter.GetBytes(sleepExAddress.ToInt64()));
        ms.Write([0xFF, 0xD0]);                     // call rax

        return ms.ToArray();
    }

    public static bool InjectLdrLoadDll(int processId, string dllPath)
    {
        IntPtr hProcess = IntPtr.Zero;
        IntPtr remoteThread = IntPtr.Zero;

        try
        {
            IntPtr ntdll = GetModuleHandle("ntdll.dll");
            IntPtr kernel32 = GetModuleHandle("kernel32.dll");

            IntPtr ldrLoadDllAddress = GetProcAddress(ntdll, "LdrLoadDll");
            IntPtr sleepExAddress = GetProcAddress(kernel32, "SleepEx");

            if (ldrLoadDllAddress == IntPtr.Zero || sleepExAddress == IntPtr.Zero)
            {
                return false;
            }

            hProcess = InjectionEngine.OpenProcessNt(processId, ProcessAllAccess);

            if (hProcess == IntPtr.Zero)
            {
                return false;
            }

            //
            // 1. Remote path buffer for the module name.
            //
            byte[] dllPathBytes = Encoding.Unicode.GetBytes(dllPath);
            long pathSize = dllPathBytes.Length + 2;

            NtCreateSection(out IntPtr pathSection, SectionAllAccess, IntPtr.Zero, ref pathSize, PageReadWrite, SecCommit, IntPtr.Zero);

            NtMapViewOfSection(pathSection, (IntPtr)(-1), out IntPtr localPath, IntPtr.Zero, IntPtr.Zero, out long offset, out ulong viewSize, ViewShare, 0, PageReadWrite);
            Marshal.Copy(dllPathBytes, 0, localPath, dllPathBytes.Length);

            offset = 0;
            viewSize = 0;

            NtMapViewOfSection(pathSection, hProcess, out IntPtr remoteDllPath, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PageReadWrite);
            NtUnmapViewOfSection((IntPtr)(-1), localPath);
            InjectionEngine.CloseHandleNt(pathSection);

            //
            // 2. UNICODE_STRING describing the remote path buffer.
            //
            int unicodeStructSize = Marshal.SizeOf<UnicodeString>();
            UnicodeString unicodeString = new()
            {
                Length = (ushort)(dllPath.Length * 2),
                MaximumLength = (ushort)((dllPath.Length + 1) * 2),
                Buffer = remoteDllPath,
            };

            byte[] unicodeStructBytes = new byte[unicodeStructSize];
            IntPtr localStructPtr = Marshal.AllocHGlobal(unicodeStructSize);

            try
            {
                Marshal.StructureToPtr(unicodeString, localStructPtr, false);
                Marshal.Copy(localStructPtr, unicodeStructBytes, 0, unicodeStructBytes.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(localStructPtr);
            }

            long structSectionSize = unicodeStructSize;
            NtCreateSection(out IntPtr structSection, SectionAllAccess, IntPtr.Zero, ref structSectionSize, PageReadWrite, SecCommit, IntPtr.Zero);

            NtMapViewOfSection(structSection, (IntPtr)(-1), out IntPtr localStruct, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PageReadWrite);
            Marshal.Copy(unicodeStructBytes, 0, localStruct, unicodeStructBytes.Length);

            offset = 0;
            viewSize = 0;

            int mapStatus = NtMapViewOfSection(structSection, hProcess, out IntPtr remoteUnicodeString, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PageReadWrite);

            NtUnmapViewOfSection((IntPtr)(-1), localStruct);
            InjectionEngine.CloseHandleNt(structSection);

            if (mapStatus != StatusSuccess)
            {
                return false;
            }

            //
            // 3. Bootstrap shellcode: LdrLoadDll(NULL, NULL,usz,NULL) then SleepEx forever.
            //
            byte[] shellcode = GenerateLdrLoadDllShellcode(ldrLoadDllAddress, remoteUnicodeString, sleepExAddress);
            long shellcodeSize = shellcode.Length;

            NtCreateSection(out IntPtr codeSection, SectionAllAccess, IntPtr.Zero, ref shellcodeSize, PageExecuteReadWrite, SecCommit, IntPtr.Zero);

            NtMapViewOfSection(codeSection, (IntPtr)(-1), out IntPtr localCode, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PageExecuteReadWrite);
            Marshal.Copy(shellcode, 0, localCode, shellcode.Length);

            offset = 0;
            viewSize = 0;

            NtMapViewOfSection(codeSection, hProcess, out IntPtr remoteShellcode, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PageExecuteReadWrite);
            NtUnmapViewOfSection((IntPtr)(-1), localCode);
            InjectionEngine.CloseHandleNt(codeSection);

            NtCreateThreadEx(out remoteThread, ThreadAllAccess, IntPtr.Zero, hProcess, remoteShellcode, IntPtr.Zero, false, 0, 0, 0, IntPtr.Zero);

            if (remoteThread == IntPtr.Zero)
            {
                return false;
            }

            _ = CloseHandle(remoteThread);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hProcess != IntPtr.Zero)
            {
                InjectionEngine.CloseHandleNt(hProcess);
            }
        }
    }
}
