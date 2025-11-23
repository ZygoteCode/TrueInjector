public class InjectorProcess
{
    public uint ProcessId { get; private set; }
    public string ProcessName { get; private set; }

    public InjectorProcess(uint processId, string processName)
    {
        ProcessId = processId;
        ProcessName = processName;
    }
}