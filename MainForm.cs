using MetroSuite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    private static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll")]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);
    
    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern IntPtr NtWriteVirtualMemory(IntPtr ProcessHandle, IntPtr BaseAddress, byte[] Buffer, UInt32 NumberOfBytesToWrite, ref UInt32 NumberOfBytesWritten);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern IntPtr RtlCreateUserThread(IntPtr processHandle, IntPtr threadSecurity, bool createSuspended, Int32 stackZeroBits, IntPtr stackReserved, IntPtr stackCommit, IntPtr startAddress, IntPtr parameter, ref IntPtr threadHandle, IntPtr clientId);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern IntPtr NtCreateThreadEx(ref IntPtr threadHandle, UInt32 desiredAccess, IntPtr objectAttributes, IntPtr processHandle, IntPtr startAddress, IntPtr parameter, bool inCreateSuspended, Int32 stackZeroBits, Int32 sizeOfStack, Int32 maximumStackSize, IntPtr attributeList);

    private uint MEM_COMMIT = 0x00001000;
    private uint MEM_RESERVE = 0x00002000;
    private uint PAGE_READWRITE = 4;

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

                IntPtr allocatedMemoryAddress = VirtualAllocEx(processHandle, IntPtr.Zero, (uint)((dllPath.Length + 1) * Marshal.SizeOf(typeof(char))), MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

                if (guna2ComboBox4.SelectedIndex == 0)
                {
                    WriteProcessMemory(processHandle, allocatedMemoryAddress, Encoding.Default.GetBytes(dllPath), (uint)((dllPath.Length + 1) * Marshal.SizeOf(typeof(char))), 0);
                }
                else
                {
                    uint bytesWritten = 0;
                    NtWriteVirtualMemory(processHandle, allocatedMemoryAddress, Encoding.Default.GetBytes(dllPath), (uint)((dllPath.Length + 1) * Marshal.SizeOf(typeof(char))), ref bytesWritten);
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
                }
            }
            else if (guna2ComboBox3.SelectedIndex == 1)
            {
                // TODO
            }

            MessageBox.Show("Succesfully injected!", "TrueInjector", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch
        {
            MessageBox.Show("An error occurred.", "TrueInjector", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void guna2ComboBox3_SelectedIndexChanged(object sender, EventArgs e)
    {
        guna2ComboBox4.Items.Clear();

        if (guna2ComboBox3.SelectedIndex == 0)
        {
            guna2ComboBox4.Items.Add("KERNEL32 Execution");
            guna2ComboBox4.Items.Add("NTDLL Execution");
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