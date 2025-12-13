using Guna.UI2.WinForms;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using static MainForm;
using static NativeLoader;

public class NativeLoader
{
    public const uint MEM_COMMIT = 0x1000;
    public const uint PAGE_READWRITE = 0x04;
    public const uint PAGE_EXECUTE_READ = 0x20;
    public const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
    public const uint RTL_CONSTANT_STRING_MAX_LENGTH = 512;

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CLIENT_ID
    {
        public IntPtr UniqueProcess;
        public IntPtr UniqueThread;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern uint RtlCreateUnicodeString(ref UNICODE_STRING DestinationString, [MarshalAs(UnmanagedType.LPWStr)] string SourceString);

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern IntPtr LdrLoadDll(IntPtr PathToFile, IntPtr Flags, ref UNICODE_STRING ModuleFileName, out IntPtr ModuleHandle);

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern IntPtr RtlInitUnicodeString(ref UNICODE_STRING DestinationString, [MarshalAs(UnmanagedType.LPWStr)] string SourceString);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint SleepEx(uint dwMilliseconds, bool bAlertable);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtOpenProcess(out IntPtr ProcessHandle, UInt32 DesiredAccess, ref OBJECT_ATTRIBUTES ObjectAttributes, ref CLIENT_ID ClientId);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtOpenThread(out IntPtr ThreadHandle, uint DesiredAccess, ref OBJECT_ATTRIBUTES ObjectAttributes, ref CLIENT_ID ClientId);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtClose(IntPtr Handle);

    [DllImport("ntdll.dll")]
    private static extern int NtCreateSection(out IntPtr SectionHandle, uint DesiredAccess, IntPtr ObjectAttributes, ref long MaximumSize, uint SectionPageProtection, uint AllocationAttributes, IntPtr FileHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtMapViewOfSection(IntPtr SectionHandle, IntPtr ProcessHandle, out IntPtr BaseAddress, IntPtr ZeroBits, IntPtr CommitSize, out long SectionOffset, out ulong ViewSize, uint InheritDisposition, uint AllocationType, uint Win32Protect);

    [DllImport("ntdll.dll")]
    private static extern uint NtUnmapViewOfSection(IntPtr hProc, IntPtr baseAddr);

    [StructLayout(LayoutKind.Sequential)]
    public struct OBJECT_ATTRIBUTES
    {
        public uint Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtCreateThreadEx(
        out IntPtr threadHandle,
    uint desiredAccess,
    IntPtr objectAttributes,
    IntPtr processHandle,
    IntPtr startAddress,
    IntPtr parameter,
    bool inCreateSuspended,
    Int32 stackZeroBits,
    Int32 sizeOfStackCommit,
    Int32 maximumStackSize,
    IntPtr attributeList
    );

    public const uint MEM_RELEASE = 0x8000;
    public const uint PAGE_EXECUTE_READWRITE = 0x40;
    public const uint INFINITE = 0xFFFFFFFF;
    private const uint SECTION_ALL_ACCESS = 0x10000000;
    private const uint SEC_COMMIT = 0x8000000;
    private const uint ViewShare = 1;
    private const uint STATUS_SUCCESS = 0x00000000;
    public const uint THREAD_ALL_ACCESS = 0x1F03FF;
    public const uint OBJ_KERNEL_HANDLE = 0x200;

    public static void InitializeObjectAttributes(ref OBJECT_ATTRIBUTES attributes, UInt32 attributesValue)
    {
        attributes.Length = (uint)Marshal.SizeOf(attributes);
        attributes.RootDirectory = IntPtr.Zero;
        attributes.ObjectName = IntPtr.Zero;
        attributes.Attributes = attributesValue;
        attributes.SecurityDescriptor = IntPtr.Zero;
        attributes.SecurityQualityOfService = IntPtr.Zero;
    }

    public static IntPtr OpenProcessNt(int processId, UInt32 desiredAccess)
    {
        CLIENT_ID cid = new CLIENT_ID
        {
            UniqueProcess = new IntPtr(processId),
            UniqueThread = IntPtr.Zero
        };

        OBJECT_ATTRIBUTES objAttr = new OBJECT_ATTRIBUTES();
        InitializeObjectAttributes(ref objAttr, 0);

        IntPtr hProcess = IntPtr.Zero;
        int status = NtOpenProcess(out hProcess, desiredAccess, ref objAttr, ref cid);

        if (status != STATUS_SUCCESS)
        {
            return IntPtr.Zero;
        }

        return hProcess;
    }

    public static bool CloseHandleNt(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return true;
        }

        int status = NtClose(handle);
        return status == STATUS_SUCCESS;
    }

    public static IntPtr NtOpenThreadReplacement(int targetProcessId, int targetThreadId, uint desiredAccess)
    {
        IntPtr hThread = IntPtr.Zero;

        CLIENT_ID clientId = new CLIENT_ID
        {
            UniqueProcess = new IntPtr(targetProcessId),
            UniqueThread = new IntPtr(targetThreadId)
        };

        OBJECT_ATTRIBUTES objAttributes = new OBJECT_ATTRIBUTES
        {
            Length = (uint)Marshal.SizeOf(typeof(OBJECT_ATTRIBUTES)),
        };

        uint status = NtOpenThread(out hThread, desiredAccess, ref objAttributes, ref clientId);

        if (status != STATUS_SUCCESS || hThread == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        return hThread;
    }

    private static byte[] GenerateLdrLoadDllShellcode(IntPtr LdrLoadDllAddress, IntPtr remoteUnicodeStr, IntPtr SleepExAddress)
    {
        using (var ms = new System.IO.MemoryStream())
        {
            ms.Write(new byte[] { 0x55 }, 0, 1);
            ms.Write(new byte[] { 0x48, 0x89, 0xE5 }, 0, 3);
            ms.Write(new byte[] { 0x48, 0x83, 0xEC, 0x20 }, 0, 4);
            ms.Write(new byte[] { 0x48, 0x31, 0xC9 }, 0, 3);
            ms.Write(new byte[] { 0x48, 0x31, 0xD2 }, 0, 3);
            ms.Write(new byte[] { 0x49, 0xB8 }, 0, 2);
            ms.Write(BitConverter.GetBytes(remoteUnicodeStr.ToInt64()), 0, 8);
            ms.Write(new byte[] { 0x49, 0x31, 0xC9 }, 0, 3);
            ms.Write(new byte[] { 0x48, 0xB8 }, 0, 2);
            ms.Write(BitConverter.GetBytes(LdrLoadDllAddress.ToInt64()), 0, 8);
            ms.Write(new byte[] { 0xFF, 0xD0 }, 0, 2);
            ms.Write(new byte[] { 0x48, 0x83, 0xC4, 0x20 }, 0, 4);
            ms.Write(new byte[] { 0x5D }, 0, 1);
            ms.Write(new byte[] { 0x48, 0xC7, 0xC1, 0xFF, 0xFF, 0xFF, 0xFF }, 0, 7);
            ms.Write(new byte[] { 0x48, 0x31, 0xD2 }, 0, 3);
            ms.Write(new byte[] { 0x48, 0xB8 }, 0, 2);
            ms.Write(BitConverter.GetBytes(SleepExAddress.ToInt64()), 0, 8);
            ms.Write(new byte[] { 0xFF, 0xD0 }, 0, 2);
            return ms.ToArray();
        }
    }

    public static bool InjectLdrLoadDll(int processId, string dllPath)
    {
        IntPtr hProcess = IntPtr.Zero;
        IntPtr remoteDllPath = IntPtr.Zero;
        IntPtr remoteUnicodeStr = IntPtr.Zero;
        IntPtr remoteShellcode = IntPtr.Zero;
        IntPtr remoteThread = IntPtr.Zero;

        try
        {
            IntPtr ntdll = GetModuleHandle("ntdll.dll");
            IntPtr kernel32 = GetModuleHandle("kernel32.dll");

            IntPtr LdrLoadDllAddress = GetProcAddress(ntdll, "LdrLoadDll");
            IntPtr SleepExAddress = GetProcAddress(kernel32, "SleepEx");

            if (LdrLoadDllAddress == IntPtr.Zero || SleepExAddress == IntPtr.Zero)
            {
                return false;
            }

            hProcess = OpenProcessNt(processId, PROCESS_ALL_ACCESS);

            if (hProcess == IntPtr.Zero)
            {
                return false;
            }

            {
                byte[] dllPathBytes = Encoding.Unicode.GetBytes(dllPath);
                long pathSize = dllPathBytes.Length + 2;

                IntPtr sectionHandle1;

                int status1 = NtCreateSection(out sectionHandle1, SECTION_ALL_ACCESS, IntPtr.Zero, ref pathSize, PAGE_READWRITE, SEC_COMMIT, IntPtr.Zero);

                IntPtr localBase1;
                long offset1 = 0;
                ulong viewSize1 = 0;

                status1 = NtMapViewOfSection(sectionHandle1, (IntPtr)(-1), out localBase1, IntPtr.Zero, IntPtr.Zero, out offset1, out viewSize1, ViewShare, 0, PAGE_READWRITE);

                Marshal.Copy(dllPathBytes, 0, localBase1, dllPathBytes.Length);

                offset1 = 0;
                viewSize1 = 0;

                status1 = NtMapViewOfSection(sectionHandle1, hProcess, out remoteDllPath, IntPtr.Zero, IntPtr.Zero, out offset1, out viewSize1, ViewShare, 0, PAGE_READWRITE);
                NtUnmapViewOfSection((IntPtr)(-1), localBase1);
                CloseHandleNt(sectionHandle1);
            }

            {
                int unicodeStructSize = Marshal.SizeOf<UNICODE_STRING>();
                long sectionSize = unicodeStructSize;

                UNICODE_STRING unicodeStr = new UNICODE_STRING
                {
                    Length = (ushort)(dllPath.Length * 2),
                    MaximumLength = (ushort)((dllPath.Length + 1) * 2),
                    Buffer = remoteDllPath
                };

                byte[] unicodeStructBytes = new byte[unicodeStructSize];
                IntPtr localStructPtr = Marshal.AllocHGlobal(unicodeStructSize);
                Marshal.StructureToPtr(unicodeStr, localStructPtr, false);
                Marshal.Copy(localStructPtr, unicodeStructBytes, 0, unicodeStructBytes.Length);
                Marshal.FreeHGlobal(localStructPtr);

                IntPtr sectionHandle2 = IntPtr.Zero;
                int status2 = NtCreateSection(out sectionHandle2, SECTION_ALL_ACCESS, IntPtr.Zero, ref sectionSize, PAGE_READWRITE, SEC_COMMIT, IntPtr.Zero);

                if (status2 != 0)
                {
                    return false;
                }

                IntPtr localBase2 = IntPtr.Zero;
                long offset2 = 0;
                ulong viewSize2 = 0;

                status2 = NtMapViewOfSection(sectionHandle2, (IntPtr)(-1), out localBase2, IntPtr.Zero, IntPtr.Zero, out offset2, out viewSize2, ViewShare, 0, PAGE_READWRITE);

                if (status2 != 0)
                {
                    CloseHandleNt(sectionHandle2);
                    return false;
                }

                Marshal.Copy(unicodeStructBytes, 0, localBase2, unicodeStructBytes.Length);

                offset2 = 0;
                viewSize2 = 0;
                status2 = NtMapViewOfSection(sectionHandle2, hProcess, out remoteUnicodeStr, IntPtr.Zero, IntPtr.Zero, out offset2, out viewSize2, ViewShare, 0, PAGE_READWRITE);

                if (status2 != 0)
                {
                    NtUnmapViewOfSection((IntPtr)(-1), localBase2);
                    CloseHandleNt(sectionHandle2);
                    return false;
                }

                NtUnmapViewOfSection((IntPtr)(-1), localBase2);
                CloseHandleNt(sectionHandle2);
            }

            byte[] shellcode = GenerateLdrLoadDllShellcode(LdrLoadDllAddress, remoteUnicodeStr, SleepExAddress);
            long maxSize = shellcode.Length;
            IntPtr sectionHandle;

            int status = NtCreateSection(out sectionHandle, SECTION_ALL_ACCESS, IntPtr.Zero, ref maxSize, PAGE_EXECUTE_READWRITE, SEC_COMMIT, IntPtr.Zero);

            IntPtr localBase;
            long offset = 0;
            ulong viewSize = 0;

            status = NtMapViewOfSection(sectionHandle, (IntPtr)(-1), out localBase, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PAGE_EXECUTE_READWRITE);

            Marshal.Copy(shellcode, 0, localBase, shellcode.Length);

            offset = 0;
            viewSize = 0;

            status = NtMapViewOfSection(sectionHandle, hProcess, out remoteShellcode, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PAGE_EXECUTE_READWRITE);
            NtUnmapViewOfSection((IntPtr)(-1), localBase);
            CloseHandleNt(sectionHandle);
            NtCreateThreadEx(out remoteThread, THREAD_ALL_ACCESS, IntPtr.Zero, hProcess, remoteShellcode, IntPtr.Zero, false, 0, 0, 0, IntPtr.Zero);

            if (remoteThread == IntPtr.Zero)
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (remoteThread != IntPtr.Zero)
            {
                CloseHandleNt(remoteThread);
            }

            if (hProcess != IntPtr.Zero)
            {
                CloseHandleNt(hProcess);
            }
        }
    }
}