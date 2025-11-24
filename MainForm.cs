using MetroSuite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public partial class MainForm : MetroForm
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll")]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, uint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll")]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern IntPtr NtWriteVirtualMemory(IntPtr ProcessHandle, IntPtr BaseAddress, byte[] Buffer, UInt32 NumberOfBytesToWrite, ref UInt32 NumberOfBytesWritten);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern IntPtr ZwWriteVirtualMemory(IntPtr ProcessHandle, IntPtr BaseAddress, byte[] Buffer, UInt32 NumberOfBytesToWrite, ref UInt32 NumberOfBytesWritten);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern IntPtr RtlCreateUserThread(IntPtr processHandle, IntPtr threadSecurity, bool createSuspended, Int32 stackZeroBits, IntPtr stackReserved, IntPtr stackCommit, IntPtr startAddress, IntPtr parameter, ref IntPtr threadHandle, IntPtr clientId);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern IntPtr NtCreateThreadEx(ref IntPtr threadHandle, UInt32 desiredAccess, IntPtr objectAttributes, IntPtr processHandle, IntPtr startAddress, IntPtr parameter, bool inCreateSuspended, Int32 stackZeroBits, Int32 sizeOfStack, Int32 maximumStackSize, IntPtr attributeList);

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtQueueApcThread(IntPtr ThreadHandle, IntPtr ApcRoutine, IntPtr ApcArgument1, IntPtr ApcArgument2, IntPtr ApcArgument3);

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtQueueApcThreadEx(IntPtr ThreadHandle, IntPtr UserApcReserveHandle, IntPtr ApcRoutine, IntPtr ApcArgument1, IntPtr ApcArgument2, IntPtr ApcArgument3);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle,uint dwThreadId);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtAllocateVirtualMemory(IntPtr ProcessHandle, ref IntPtr BaseAddress, IntPtr ZeroBits, ref ulong RegionSize, uint AllocationType, uint Protect);

    [DllImport("ntdll.dll")]
    private static extern int NtCreateSection(out IntPtr SectionHandle, uint DesiredAccess, IntPtr ObjectAttributes, ref long MaximumSize, uint SectionPageProtection, uint AllocationAttributes, IntPtr FileHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtMapViewOfSection(IntPtr SectionHandle, IntPtr ProcessHandle, out IntPtr BaseAddress, IntPtr ZeroBits, IntPtr CommitSize, out long SectionOffset, out ulong ViewSize, uint InheritDisposition, uint AllocationType, uint Win32Protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateFileMapping( IntPtr hFile, IntPtr lpFileMappingAttributes, uint flProtect, uint dwMaximumSizeHigh, uint dwMaximumSizeLow, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(IntPtr hSourceProcessHandle, IntPtr hSourceHandle, IntPtr hTargetProcessHandle, out IntPtr lpTargetHandle, uint dwDesiredAccess, bool bInheritHandle, uint dwOptions);

    [DllImport("ntdll")]
    private static extern uint NtUnmapViewOfSection(IntPtr hProc, IntPtr baseAddr);

    [Flags]
    public enum AllocationType : uint
    {
        MEM_COMMIT = 0x1000,
        MEM_RESERVE = 0x2000
    }

    [Flags]
    public enum MemoryProtection : uint
    {
        PAGE_READWRITE = 0x04
    }

    [DllImport("kernelbase.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc2(IntPtr processHandle, IntPtr baseAddress, UIntPtr size, AllocationType allocationType, MemoryProtection protection, IntPtr extendedParameters, IntPtr parameterCount);

    private const uint MEM_COMMIT = 0x00001000;
    private const uint MEM_RESERVE = 0x00002000;
    private const uint PAGE_READWRITE = 4;
    private const uint SECTION_ALL_ACCESS = 0x10000000;
    private const uint SEC_COMMIT = 0x8000000;
    private const uint ViewShare = 1;
    private const uint FILE_MAP_WRITE = 0x0002;

    private List<InjectorProcess> _injectorProcesses;

    public MainForm()
    {
        InitializeComponent();
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;
        CheckForIllegalCrossThreadCalls = false;

        _injectorProcesses = new List<InjectorProcess>();
        RefreshProcesses();
        SearchProcess("");

        Thread checkerThread = new Thread(CheckerThread);
        checkerThread.Priority = ThreadPriority.Highest;
        checkerThread.Start();

        guna2ComboBox1.SelectedIndex = 0;
        guna2ComboBox2.SelectedIndex = 0;
        guna2ComboBox3.SelectedIndex = 0;
        guna2ComboBox4.SelectedIndex = 0;
        guna2ComboBox5.SelectedIndex = 0;
    }

    public void CheckerThread()
    {
        while (true)
        {
            try
            {
                Thread.Sleep(100);
                bool canBeInjected = Utils.CanBeInjected(listView1, guna2TextBox2.Text);

                guna2Button3.Invoke(new Action(() =>
                {
                    guna2Button3.Enabled = canBeInjected;
                }));
            }
            catch
            {

            }
        }
    }

    public void RefreshProcesses()
    {
        _injectorProcesses.Clear();

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                _injectorProcesses.Add(new InjectorProcess((uint)process.Id, process.ProcessName));
            }
            catch
            {

            }
        }
    }

    public void SearchProcess(string query)
    {
        query = query.ToLower().Replace(" ", "");
        listView1.Items.Clear();

        foreach (InjectorProcess injectorProcess in _injectorProcesses)
        {
            string formatted = injectorProcess.ProcessId.ToString() + injectorProcess.ProcessName.ToLower();
            formatted = formatted.ToLower().Replace(" ", "");

            if (query.Contains(formatted) || formatted.Contains(query))
            {
                ListViewItem listViewItem = new ListViewItem(injectorProcess.ProcessId.ToString());
                listViewItem.SubItems.Add(injectorProcess.ProcessName);
                listView1.Items.Add(listViewItem);
            }
        }
    }

    private void guna2TextBox1_TextChanged(object sender, EventArgs e)
    {
        SearchProcess(guna2TextBox1.Text);
    }

    private void guna2Button1_Click(object sender, EventArgs e)
    {
        RefreshProcesses();
        SearchProcess(guna2TextBox1.Text);
    }

    private void guna2Button4_Click(object sender, EventArgs e)
    {
        Process.Start("https://github.com/ZygoteCode/TrueInjector/");
    }

    private void guna2Button2_Click(object sender, EventArgs e)
    {
        if (openFileDialog1.ShowDialog().Equals(DialogResult.OK))
        {
            guna2TextBox2.Text = openFileDialog1.FileName;
        }
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
    {
        Process.GetCurrentProcess().Kill();
    }

    private void guna2Button3_Click(object sender, EventArgs e)
    {
        if (!Utils.CanBeInjected(listView1, guna2TextBox2.Text))
        {
            MessageBox.Show("An error occurred.", "TrueInjector", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            if (guna2ComboBox3.SelectedIndex == 0)
            {
                uint processId = uint.Parse(listView1.SelectedItems[0].Text);
                IntPtr processHandle = OpenProcess(0x001F0FFF, false, (int)processId);

                string dllPath = guna2TextBox2.Text;

                byte[] dllBytes = guna2ComboBox1.SelectedIndex == 0 ?
                    Encoding.ASCII.GetBytes(dllPath + "\0") :
                     Encoding.Unicode.GetBytes(dllPath + "\0");

                uint dllSize = (uint)dllBytes.Length;

                IntPtr remoteThread = new IntPtr(0);
                IntPtr loadLibraryAddress = IntPtr.Zero;

                switch (guna2ComboBox1.SelectedIndex)
                {
                    case 0:
                        loadLibraryAddress = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
                        break;
                    case 1:
                        loadLibraryAddress = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
                        break;
                }

                IntPtr allocatedMemoryAddress = IntPtr.Zero;
                bool memoryAlreadyFilled = false;

                if (guna2ComboBox4.SelectedIndex == 3)
                {
                    long maxSize = dllSize;
                    IntPtr sectionHandle;

                    int status = NtCreateSection(out sectionHandle, SECTION_ALL_ACCESS, IntPtr.Zero, ref maxSize, PAGE_READWRITE, SEC_COMMIT, IntPtr.Zero);

                    IntPtr localBase;
                    long offset = 0;
                    ulong viewSize = 0;

                    status = NtMapViewOfSection(sectionHandle, (IntPtr)(-1), out localBase, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PAGE_READWRITE);

                    Marshal.Copy(dllBytes, 0, localBase, dllBytes.Length);

                    IntPtr remoteBase;
                    offset = 0;
                    viewSize = 0;

                    status = NtMapViewOfSection(sectionHandle, processHandle, out remoteBase, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PAGE_READWRITE);

                    allocatedMemoryAddress = remoteBase;
                    memoryAlreadyFilled = true;

                    NtUnmapViewOfSection((IntPtr)(-1), localBase);
                    CloseHandle(sectionHandle);
                }
                else if (guna2ComboBox4.SelectedIndex == 4)
                {
                    IntPtr hSection = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, PAGE_READWRITE, 0, dllSize, null);
                    IntPtr localView = MapViewOfFile( hSection, FILE_MAP_WRITE, 0, 0, (UIntPtr)dllSize );
                    Marshal.Copy(dllBytes, 0, localView, dllBytes.Length);

                    long offset = 0;
                    ulong viewSize = 0;
                    IntPtr remoteBase;

                    int status = NtMapViewOfSection(hSection, processHandle, out remoteBase, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PAGE_READWRITE);

                    allocatedMemoryAddress = remoteBase;
                    memoryAlreadyFilled = true;

                    UnmapViewOfFile(localView);
                    CloseHandle(hSection);
                }
                else
                {
                    if (guna2ComboBox5.SelectedIndex == 0)
                    {
                        allocatedMemoryAddress = VirtualAllocEx(processHandle, IntPtr.Zero, dllSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                    }
                    else if (guna2ComboBox5.SelectedIndex == 1)
                    {
                        ulong newDllSize = (ulong)dllSize;
                        NtAllocateVirtualMemory(processHandle, ref allocatedMemoryAddress, IntPtr.Zero, ref newDllSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                    }
                    else if (guna2ComboBox5.SelectedIndex == 2)
                    {
                        long maxSize = dllSize;
                        IntPtr sectionHandle;

                        int status = NtCreateSection(out sectionHandle, SECTION_ALL_ACCESS, IntPtr.Zero, ref maxSize, PAGE_READWRITE, SEC_COMMIT, IntPtr.Zero);

                        IntPtr localBase;
                        long offset = 0;
                        ulong viewSize = 0;

                        status = NtMapViewOfSection(sectionHandle, (IntPtr)(-1), out localBase, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PAGE_READWRITE);

                        Marshal.Copy(dllBytes, 0, localBase, dllBytes.Length);

                        IntPtr remoteBase;
                        offset = 0;
                        viewSize = 0;

                        status = NtMapViewOfSection(sectionHandle, processHandle, out remoteBase, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PAGE_READWRITE);
                        allocatedMemoryAddress = remoteBase;
                    }
                    else if (guna2ComboBox5.SelectedIndex == 3)
                    {
                        IntPtr hSection = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, PAGE_READWRITE, 0, dllSize, null);
                        IntPtr localView = MapViewOfFile(hSection, FILE_MAP_WRITE, 0, 0, (UIntPtr)dllSize);
                        Marshal.Copy(dllBytes, 0, localView, dllBytes.Length);

                        long offset = 0;
                        ulong viewSize = 0;
                        IntPtr remoteBase;
                        int status = NtMapViewOfSection(hSection, processHandle, out remoteBase, IntPtr.Zero, IntPtr.Zero, out offset, out viewSize, ViewShare, 0, PAGE_READWRITE);

                        allocatedMemoryAddress = remoteBase;
                        UnmapViewOfFile(localView);
                        CloseHandle(hSection);
                    }
                    else if (guna2ComboBox5.SelectedIndex == 4)
                    {
                        allocatedMemoryAddress = VirtualAlloc2(processHandle, IntPtr.Zero, (UIntPtr)dllSize, AllocationType.MEM_RESERVE | AllocationType.MEM_COMMIT, MemoryProtection.PAGE_READWRITE, IntPtr.Zero, IntPtr.Zero);
                    }
                }

                if (!memoryAlreadyFilled)
                {
                    if (guna2ComboBox4.SelectedIndex == 0)
                    {
                        WriteProcessMemory(processHandle, allocatedMemoryAddress, dllBytes, dllSize, 0);
                    }
                    else if (guna2ComboBox4.SelectedIndex == 1)
                    {
                        uint bytesWritten = 0;
                        NtWriteVirtualMemory(processHandle, allocatedMemoryAddress, dllBytes, dllSize, ref bytesWritten);
                    }
                    else if (guna2ComboBox4.SelectedIndex == 2)
                    {
                        uint bytesWritten = 0;
                        ZwWriteVirtualMemory(processHandle, allocatedMemoryAddress, dllBytes, dllSize, ref bytesWritten);
                    }
                }

                switch (guna2ComboBox2.SelectedIndex)
                {
                    case 0:
                        CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibraryAddress, allocatedMemoryAddress, 0, IntPtr.Zero);
                        break;
                    case 1:
                        RtlCreateUserThread(processHandle, IntPtr.Zero, false, 0, IntPtr.Zero, IntPtr.Zero, loadLibraryAddress, allocatedMemoryAddress, ref remoteThread, IntPtr.Zero);
                        break;
                    case 2:
                        NtCreateThreadEx(ref remoteThread, 0x1FFFFF, IntPtr.Zero, processHandle, loadLibraryAddress, allocatedMemoryAddress, false, 0, 0, 0, IntPtr.Zero);
                        break;
                    case 3:
                        foreach (ProcessThread t in Process.GetProcessById((int) processId).Threads)
                        {
                            IntPtr hThread = OpenThread(0x0010, false, (uint)t.Id);

                            if (hThread == IntPtr.Zero)
                            {
                                continue;
                            }

                            int status = NtQueueApcThread(
                                hThread,
                                loadLibraryAddress,
                                allocatedMemoryAddress,
                                IntPtr.Zero,
                                IntPtr.Zero
                            );

                            CloseHandle(hThread);
                        }
                        break;
                    case 4:
                        foreach (ProcessThread t in Process.GetProcessById((int)processId).Threads)
                        {
                            IntPtr hThread = OpenThread(0x1FFFFF, false, (uint)t.Id);

                            if (hThread == IntPtr.Zero)
                            {
                                continue;
                            }

                            int status = NtQueueApcThreadEx(hThread, IntPtr.Zero, loadLibraryAddress, allocatedMemoryAddress, IntPtr.Zero, IntPtr.Zero);
                            CloseHandle(hThread);
                        }
                        break;
                }

                CloseHandle(processHandle);                
            }
            else if (guna2ComboBox3.SelectedIndex == 1)
            {
                // TODO
            }

            MessageBox.Show("Succesfully injected!", "TrueInjector", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("An error occurred.", "TrueInjector", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void guna2ComboBox3_SelectedIndexChanged(object sender, EventArgs e)
    {
        guna2ComboBox4.Items.Clear();

        if (guna2ComboBox3.SelectedIndex == 0)
        {
            guna2ComboBox4.Items.Add("WriteProcessMemory");
            guna2ComboBox4.Items.Add("NtWriteVirtualMemory");
            guna2ComboBox4.Items.Add("ZwWriteVirtualMemory");
            guna2ComboBox4.Items.Add("NtCreateSection + NtMapViewOfSection");
            guna2ComboBox4.Items.Add("CreateFileMapping + MapViewOfFile + NtMapViewOfSection");
        }
        else if (guna2ComboBox3.SelectedIndex == 1)
        {
            guna2ComboBox4.Items.Add("Normal Mapping");
            guna2ComboBox4.Items.Add("Skip Init Routines");
            guna2ComboBox4.Items.Add("Discard Headers");
        }

        guna2ComboBox4.SelectedIndex = 0;
    }
}