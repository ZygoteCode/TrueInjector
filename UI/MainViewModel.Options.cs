using TrueInjector.Infrastructure;
using TrueInjector.Models;

namespace TrueInjector.UI;

/// <summary>
/// Binding-facing wrappers over <see cref="InjectionSettings"/>: pickers bind to
/// whole <see cref="Choice{T}"/> instances, the engine keeps talking raw enums.
/// </summary>
public sealed partial class MainViewModel
{
    public IReadOnlyList<Choice<LoadLibraryFlavor>> LoadLibraryChoices => OptionSets.LoadLibrary;

    public IReadOnlyList<Choice<ExecutionTechnique>> ExecutionChoices => OptionSets.Execution;

    public IReadOnlyList<Choice<AllocationTechnique>> AllocationChoices => OptionSets.Allocation;

    public IReadOnlyList<Choice<WriteTechnique>> WriteChoices => OptionSets.Write;

    public IReadOnlyList<Choice<HandleTechnique>> HandleChoices => OptionSets.Handles;

    public Choice<LoadLibraryFlavor>? LoadLibraryChoice
    {
        get => FindIn(OptionSets.LoadLibrary, Settings.LoadLibrary);
        set
        {
            if (value is not null)
            {
                Settings.LoadLibrary = value.Value;
            }
        }
    }

    public Choice<ExecutionTechnique>? ExecutionChoice
    {
        get => FindIn(OptionSets.Execution, Settings.Execution);
        set
        {
            if (value is not null)
            {
                Settings.Execution = value.Value;
            }
        }
    }

    public Choice<AllocationTechnique>? AllocationChoice
    {
        get => FindIn(OptionSets.Allocation, Settings.Allocation);
        set
        {
            if (value is not null)
            {
                Settings.Allocation = value.Value;
            }
        }
    }

    public Choice<WriteTechnique>? WriteChoice
    {
        get => FindIn(OptionSets.Write, Settings.Write);
        set
        {
            if (value is not null)
            {
                Settings.Write = value.Value;
                OnPropertyChanged(nameof(AllocationLocked));
            }
        }
    }

    public Choice<HandleTechnique>? HandlesChoice
    {
        get => FindIn(OptionSets.Handles, Settings.Handles);
        set
        {
            if (value is not null)
            {
                Settings.Handles = value.Value;
            }
        }
    }

    public bool AllocationLocked => Settings.AllocationManagedByWrite;

    public string MethodDescription => TechniqueDescriptions.Describe(Settings.Method);

    public bool WatchdogArmed
    {
        get => Watchdog == WatchdogState.Armed;
        set => Watchdog = value ? WatchdogState.Armed : WatchdogState.Disarmed;
    }

    public string WatchdogTarget
    {
        get => WatchdogTargetName ?? string.Empty;
        set => WatchdogTargetName = value.Trim();
    }

    public bool RandomizePayload
    {
        get => RandomizePayloadFlag;
        set => RandomizePayloadFlag = value;
    }

    public bool HidePayload
    {
        get => HidePayloadFlag;
        set => HidePayloadFlag = value;
    }

    private static Choice<T>? FindIn<T>(IReadOnlyList<Choice<T>> list, T value) where T : struct, Enum
    {
        foreach (Choice<T>? choice in list)
        {
            if (EqualityComparer<T>.Default.Equals(choice.Value, value))
            {
                return choice;
            }
        }

        return null;
    }
}
