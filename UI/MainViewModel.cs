using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using TrueInjector.Core;
using TrueInjector.Infrastructure;
using TrueInjector.Models;
using ActivityKind = TrueInjector.Models.ActivityKind;

namespace TrueInjector.UI;

public sealed record ToastRequest(string Title, string Message, ActivityKind Kind);

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMilliseconds(500);

    private const int ActivityCapacity = 400;

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _heartbeat;
    private readonly DispatcherTimer _searchDebounce;
    private readonly CancellationTokenSource _watchdogCancellation = new();
    private readonly Dictionary<uint, bool> _architectureCache = [];
    private int _autoScanCounter;
    private DllInspection? _inspection;
    private DateTime _inspectionStamp = DateTime.MinValue;
    private long _inspectionSize = -1;
    private bool _isBusy;
    private bool _isRefreshing;
    private string _searchQuery = string.Empty;
    private AppProcess? _selectedProcess;
    private string _dllPath = string.Empty;
    private string _canInjectHint = "Pick a target process and a payload DLL.";
    private Readiness _readiness = Readiness.Waiting;
    private WatchdogState _watchdog = WatchdogState.Disarmed;
    private uint? _watchdogTargetPid;

    public MainViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Processes = [];
        Activity = [];
        Settings = new InjectionSettings();
        Settings.PropertyChanged += (_, args) =>
        {
            OnPropertyChanged(nameof(PipelinePreview));

            if (args.PropertyName == nameof(InjectionSettings.Method))
            {
                OnPropertyChanged(nameof(MethodDescription));
            }
        };

        ProcessesView = (ListCollectionView)CollectionViewSource.GetDefaultView(Processes);
        ProcessesView.Filter = MatchesSearch;

        RefreshCommand = new RelayCommand(async () => await RefreshAsync(manual: true), () => !IsBusy);
        BrowseCommand = new RelayCommand(BrowseForDll);
        ClearDllCommand = new RelayCommand(ClearDll, () => !string.IsNullOrEmpty(DllPath));
        InjectCommand = new RelayCommand(async () => await InjectAsync(), () => CanInject);
        OpenGitHubCommand = new RelayCommand(OpenGitHub);
        ClearActivityCommand = new RelayCommand(Activity.Clear);

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            ProcessesView.Refresh();
            OnPropertyChanged(nameof(ProcessStats));
        };

        _heartbeat = new DispatcherTimer { Interval = HeartbeatInterval };
        _heartbeat.Tick += OnHeartbeat;
        _heartbeat.Start();

        _ = Task.Run(WatchdogLoopAsync);

        _ = WarmUpAsync();
    }

    public event Action<ToastRequest>? ToastRequested;

    public event Action? ShutdownRequested;

    public ObservableCollection<AppProcess> Processes { get; }

    public ListCollectionView ProcessesView { get; }

    public ObservableCollection<ActivityEntry> Activity { get; }

    public InjectionSettings Settings { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand BrowseCommand { get; }

    public RelayCommand ClearDllCommand { get; }

    public RelayCommand InjectCommand { get; }

    public RelayCommand OpenGitHubCommand { get; }

    public RelayCommand ClearActivityCommand { get; }

    public WatchdogState Watchdog
    {
        get => _watchdog;
        set
        {
            if (SetProperty(ref _watchdog, value))
            {
                if (value == WatchdogState.Armed)
                {
                    PostActivity(ActivityKind.Action, "Startup watchdog armed.");
                }
                else
                {
                    _watchdogTargetPid = null;
                    PostActivity(ActivityKind.Action, "Startup watchdog disarmed.");
                }
            }
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                _searchDebounce.Stop();
                _searchDebounce.Start();
            }
        }
    }

    public AppProcess? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (SetProperty(ref _selectedProcess, value))
            {
                OnPropertyChanged(nameof(TargetLabel));
                EvaluateReadiness();
            }
        }
    }

    public string DllPath
    {
        get => _dllPath;
        set
        {
            if (SetProperty(ref _dllPath, value ?? string.Empty))
            {
                InspectDll(force: true);
                OnPropertyChanged(nameof(DllFileName));
                OnPropertyChanged(nameof(HasDll));
                ClearDllCommand.RaiseCanExecuteChanged();
                EvaluateReadiness();
            }
        }
    }

    public DllInspection? Inspection
    {
        get => _inspection;
        private set
        {
            if (SetProperty(ref _inspection, value))
            {
                OnPropertyChanged(nameof(DllFileName));
                OnPropertyChanged(nameof(HasDll));
            }
        }
    }

    public bool HasDll => !string.IsNullOrEmpty(_dllPath);

    public string DllFileName => HasDll ? Path.GetFileName(_dllPath) : "No payload selected";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                EvaluateReadiness();
            }
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    public string CanInjectHint
    {
        get => _canInjectHint;
        private set => SetProperty(ref _canInjectHint, value);
    }

    public bool CanInject
    {
        get => _readiness == Readiness.Ready && !_isBusy;
        private set { /* driven by EvaluateReadiness */ }
    }

    public WatchdogState CurrentWatchdog => _watchdog;

    public string TargetLabel => SelectedProcess is null ? "no target" : $"{SelectedProcess.ProcessName} ({SelectedProcess.DisplayId})";

    public string ProcessStats
    {
        get
        {
            int total = Processes.Count;
            int visible = 0;

            foreach (object? _ in ProcessesView)
            {
                visible++;
            }

            return visible == total ? $"{total} processes" : $"{visible} / {total} processes";
        }
    }

    public string PipelinePreview => TechniqueDescriptions.Pipeline(Settings);

    public void CopyDllInspectionToClipboard()
    {
        if (Inspection is { } dll)
        {
            Clipboard.SetText(dll.Path);
        }
    }

    private async Task WarmUpAsync()
    {
        await RefreshAsync(manual: false);
        PostActivity(ActivityKind.Info, $"TrueInjector ready · {ProcessStats.Replace(" processes", "")} processes visible.");
    }

    private bool MatchesSearch(object item)
    {
        if (item is not AppProcess process)
        {
            return false;
        }

        string query = _searchQuery.Trim().ToLowerInvariant().Replace(" ", "");

        if (query.Length == 0)
        {
            return true;
        }

        string nameKey = process.ProcessName.ToLowerInvariant();
        string idKey = process.DisplayId;

        return nameKey.Contains(query) || idKey.Contains(query);
    }

    private async Task RefreshAsync(bool manual)
    {
        if (IsBusy)
        {
            return;
        }

        IsRefreshing = true;

        try
        {
            List<AppProcess> scanned = await Task.Run(ProcessScanner.Scan);
            MergeScanned(scanned);

            if (manual)
            {
                PostActivity(ActivityKind.Info, $"Process table refreshed · {Processes.Count} entries.");
            }

            OnPropertyChanged(nameof(ProcessStats));
            EvaluateReadiness();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Applies scan results in place so live list scrolling, selection and
    /// virtualisation state stay untouched between heartbeats.
    /// </summart>
    private void MergeScanned(List<AppProcess> scanned)
    {
        _architectureCache.Clear();
        HashSet<uint> scannedIds = [.. scanned.Select(p => p.ProcessId)];

        for (int i = Processes.Count - 1; i >= 0; i--)
        {
            if (!scannedIds.Contains(Processes[i].ProcessId))
            {
                Processes.RemoveAt(i);
            }
        }

        int insertCursor = 0;

        foreach (AppProcess fresh in scanned)
        {
            AppProcess? existing = Processes.FirstOrDefault(p => p.ProcessId == fresh.ProcessId);

            if (existing is null)
            {
                Processes.Insert(Math.Min(insertCursor, Processes.Count), fresh);
            }
            else if (insertCursor < Processes.Count)
            {
                insertCursor = Math.Max(insertCursor, Processes.IndexOf(existing) + 1);
            }

            insertCursor++;
        }

        if (_selectedProcess is not null)
        {
            AppProcess? rematch = Processes.FirstOrDefault(p => p.ProcessId == _selectedProcess.ProcessId);

            if (!ReferenceEquals(rematch, _selectedProcess))
            {
                SelectedProcess = rematch;
            }
        }
    }

    private void OnHeartbeat(object? sender, EventArgs e)
    {
        _autoScanCounter++;

        if (!IsBusy && _autoScanCounter >= 4)
        {
            _autoScanCounter = 0;
            _ = RefreshAsync(manual: false);
        }

        InspectDll(force: false);
        EvaluateReadiness();
    }

    private void InspectDll(bool force)
    {
        if (string.IsNullOrEmpty(_dllPath))
        {
            Inspection = null;
            return;
        }

        try
        {
            FileInfo info = new(_dllPath);

            if (!info.Exists)
            {
                Inspection = null;
                _inspectionStamp = DateTime.MinValue;
                _inspectionSize = -1;
                return;
            }

            DateTime stamp = info.LastWriteTimeUtc;

            if (!force && stamp == _inspectionStamp && info.Length == _inspectionSize)
            {
                return;
            }

            _inspectionStamp = stamp;
            _inspectionSize = info.Length;

            if (!PortableExecutable.TryRead(_dllPath, out PortableExecutable? pe) || pe is null)
            {
                Inspection = new DllInspection(_dllPath, "unreadable", false, false, info.Length, info.LastWriteTimeUtc);
                return;
            }

            Inspection = new DllInspection(
                _dllPath,
                pe.MachineLabel,
                pe.IsDll,
                pe.HasEntryPoint,
                info.Length,
                info.LastWriteTimeUtc);
        }
        catch
        {
            Inspection = null;
        }
    }

    private void EvaluateReadiness()
    {
        (Readiness readiness, string hint) = ComputeReadiness();

        _readiness = readiness;
        CanInjectHint = hint;
        OnPropertyChanged(nameof(CanInject));
        OnPropertyChanged(nameof(CanInjectHint));
        OnPropertyChanged(nameof(IsWaitingStatus));
        OnPropertyChanged(nameof(IsBlockedStatus));
        OnPropertyChanged(nameof(IsReadyStatus));
        OnPropertyChanged(nameof(TargetLabel));
        InjectCommand.RaiseCanExecuteChanged();
    }

    public bool IsWaitingStatus => _readiness == Readiness.Waiting;

    public bool IsBlockedStatus => _readiness == Readiness.Blocked;

    public bool IsReadyStatus => _readiness == Readiness.Ready;

    private (Readiness, string) ComputeReadiness()
    {
        if (IsBusy)
        {
            return (Readiness.Busy, "Injectingâ€¦");
        }

        if (SelectedProcess is null)
        {
            return (Readiness.Waiting, "Pick a target process to continue.");
        }

        if (!ProcessScanner.IsAlive(SelectedProcess.ProcessId))
        {
            return (Readiness.Waiting, "The selected process just exited - pick another target.");
        }

        if (string.IsNullOrWhiteSpace(_dllPath))
        {
            return (Readiness.Waiting, "Choose a payload DLL to inject.");
        }

        if (!File.Exists(_dllPath))
        {
            return (Readiness.Blocked, "The payload file is missing on disk.");
        }

        if (!_dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return (Readiness.Blocked, "The payload must be a .dll file.");
        }

        DllInspection? dll = Inspection;

        if (dll is null)
        {
            return (Readiness.Blocked, "The payload header could not be parsed.");
        }

        if (!dll.HasEntryPoint)
        {
            return (Readiness.Blocked, "The payload has no entry point - it cannot be loaded.");
        }

        bool processIs64 = _architectureCache.TryGetValue(SelectedProcess.ProcessId, out bool cached)
            ? cached
            : (_architectureCache[SelectedProcess.ProcessId] = ProcessScanner.IsProcess64Bit(SelectedProcess.ProcessId));

        bool payloadIs64 = dll.Architecture == "x64";
        bool payloadSupported = dll.Architecture is "x64" or "x86";

        if (!payloadSupported)
        {
            return (Readiness.Blocked, $"Unsupported payload architecture ({dll.Architecture}).");
        }

        if (payloadIs64 && !processIs64)
        {
            return (Readiness.Blocked, "A 64-bit payload cannot enter a 32-bit process.");
        }

        if (!payloadIs64 && processIs64)
        {
            return (Readiness.Blocked, "A 32-bit payload cannot enter a 64-bit process.");
        }

        return (Readiness.Ready, $"Ready · {Settings.MethodHeader}");
    }

    private async Task InjectAsync(uint processIdOverride = 0, bool initiatedByWatchdog = false)
    {
        uint target = processIdOverride != 0 ? processIdOverride : SelectedProcess?.ProcessId ?? 0;
        string dllPath = _dllPath;

        if (target == 0 || !File.Exists(dllPath))
        {
            return;
        }

        InjectionJob job = new(
            target,
            dllPath,
            Settings,
            RandomizePayload: RandomizePayloadFlag,
            HidePayload: HidePayloadFlag);

        IsBusy = true;
        PostActivity(ActivityKind.Action, $"Injecting {Path.GetFileName(dllPath)} → {DescribeTarget(target, initiatedByWatchdog)}");

        InjectionOutcome outcome = await Task.Run(() => InjectionEngine.Execute(job, message => PostActivity(ActivityKind.Action, $"  {message}")));

        IsBusy = false;

        if (outcome.Success)
        {
            PostActivity(ActivityKind.Success, outcome.Message);
            ToastRequested?.Invoke(new ToastRequest("Injection complete", outcome.Message, ActivityKind.Success));
        }
        else
        {
            PostActivity(ActivityKind.Error, outcome.Message);
            ToastRequested?.Invoke(new ToastRequest("Injection failed", outcome.Message, ActivityKind.Error));
        }

        if (initiatedByWatchdog && outcome.Success)
        {
            // The original tool shut itself down right after the startup shot.
            PostActivity(ActivityKind.Info, "Watchdog delivered its shot - shutting down.");
            await Task.Delay(400);
            ShutdownRequested?.Invoke();
        }
    }

    public bool RandomizePayloadFlag { get; set; }

    public bool HidePayloadFlag { get; set; }

    private string DescribeTarget(uint pid, bool watchdog) =>
        (watchdog ? "startup watchdog · " : string.Empty) + (SelectedProcess?.ProcessId == pid ? TargetLabel : $"pid {pid}");

    private async Task WatchdogLoopAsync()
    {
        CancellationToken token = _watchdogCancellation.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(WatchdogInterval, token);

                if (_watchdog != WatchdogState.Armed || IsBusy)
                {
                    continue;
                }

                string target = WatchdogTargetName?.Trim().ToLowerInvariant() ?? string.Empty;

                if (target.Length == 0 || string.IsNullOrEmpty(_dllPath))
                {
                    continue;
                }

                Process[] candidates = Process.GetProcessesByName(WatchdogTargetName?.Trim());

                foreach (Process candidate in candidates)
                {
                    uint pid = (uint)candidate.Id;

                    if (_watchdogTargetPid == pid || !ValidateWatchdogCandidate(pid))
                    {
                        continue;
                    }

                    _watchdogTargetPid = pid;
                    await InjectAsync(pid, initiatedByWatchdog: true);
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Keep patrolling; transient Win32 faults are part of the territory.
            }
        }
    }

    public string? WatchdogTargetName { get; set; }

    private bool ValidateWatchdogCandidate(uint pid)
    {
        try
        {
            if (!File.Exists(_dllPath) || !_dllPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!PortableExecutable.TryRead(_dllPath, out PortableExecutable? pe) || pe is null || !pe.HasEntryPoint)
            {
                return false;
            }

            bool processIs64 = ProcessScanner.IsProcess64Bit(pid);

            return pe.Machine switch
            {
                PortableExecutable.MachineAmd64 => processIs64,
                PortableExecutable.MachineI386 => !processIs64,
                _ => false,
            };
        }
        catch
        {
            return false;
        }
    }

    public void BrowseForDll()
    {
        OpenFileDialog dialog = new()
        {
            Title = "Select payload",
            Filter = "Dynamic-link library (*.dll)|*.dll|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
        {
            DllPath = dialog.FileName;
            PostActivity(ActivityKind.Info, $"Payload loaded: {_dllPath}");
        }
    }

    private void ClearDll()
    {
        DllPath = string.Empty;
        Inspection = null;
        _inspectionStamp = DateTime.MinValue;
        _inspectionSize = -1;
    }

    private static void OpenGitHub() => Process.Start(new ProcessStartInfo("https://github.com/ZygoteCode/TrueInjector/") { UseShellExecute = true });

    private void PostActivity(ActivityKind kind, string message)
    {
        void Append()
        {
            Activity.Add(kind switch
            {
                ActivityKind.Info => ActivityEntry.Info(message),
                ActivityKind.Success => ActivityEntry.Success(message),
                ActivityKind.Error => ActivityEntry.Error(message),
                ActivityKind.Action => ActivityEntry.Action(message),
                _ => ActivityEntry.Info(message),
            });

            while (Activity.Count > ActivityCapacity)
            {
                Activity.RemoveAt(0);
            }
        }

        if (_dispatcher.CheckAccess())
        {
            Append();
        }
        else
        {
            _ = _dispatcher.BeginInvoke(Append);
        }
    }

    public void PublishToast(string title, string message, ActivityKind kind) => ToastRequested?.Invoke(new ToastRequest(title, message, kind));

    private enum Readiness
    {
        Waiting,
        Blocked,
        Ready,
        Busy,
    }
}

public enum WatchdogState
{
    Disarmed = 0,
    Armed = 1,
}
