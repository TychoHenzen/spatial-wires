using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using SpatialCircuits.Core;

namespace SpatialCircuits.Cells;

public readonly record struct CustomCellPort(
    string Name,
    CardinalDirection Direction,
    bool CanReceive,
    bool CanDrive)
{
}

public sealed record CustomCellEvaluationContext(
    long Tick,
    ImmutableSortedDictionary<string, LogicValue> Inputs,
    ImmutableSortedDictionary<string, string> Parameters,
    ImmutableArray<byte> State,
    ulong RandomState)
{
}

public readonly record struct CustomCellProposal(string OutputPort, LogicValue Value)
{
}

public sealed record CustomCellTransition(
    ImmutableArray<CustomCellProposal> Proposals,
    ImmutableArray<byte> NextState,
    ulong NextRandomState)
{
}

/// <summary>A registered rule must be a stateless transition over its explicit context.</summary>
public interface ICustomCellRule
{
    ImmutableArray<byte> CreateInitialState(ImmutableSortedDictionary<string, string> parameters);

    void ValidateState(
        ImmutableSortedDictionary<string, string> parameters,
        ImmutableArray<byte> state);

    CustomCellTransition Evaluate(CustomCellEvaluationContext context);

    bool TryMigrateState(
        BehaviorId sourceBehaviorId,
        ImmutableArray<byte> sourceState,
        out ImmutableArray<byte> migratedState);
}

public static class CustomCellDiagnosticCodes
{
    public const string RegistrationInvalid = "custom-cell.registration-invalid";
    public const string RegistrationMissing = "custom-cell.registration-missing";
    public const string RegistrationDuplicate = "custom-cell.registration-duplicate";
    public const string ParametersInvalid = "custom-cell.parameters-invalid";
    public const string StateInvalid = "custom-cell.state-invalid";
    public const string TransitionInvalid = "custom-cell.transition-invalid";
    public const string MigrationUnavailable = "custom-cell.migration-unavailable";
    public const string MigrationFailed = "custom-cell.migration-failed";
}

public sealed class CustomCellRuleException : InvalidOperationException
{
    public CustomCellRuleException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class CustomCellRuleRegistry
{
    private readonly ImmutableDictionary<BehaviorId, CustomCellRuleRegistration> _rules;

    internal static CustomCellRuleRegistry Empty { get; } = new([]);

    public CustomCellRuleRegistry(IEnumerable<CustomCellRuleRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var builder = ImmutableDictionary.CreateBuilder<BehaviorId, CustomCellRuleRegistration>();
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (registration.ParameterValidator is null || registration.RuleFactory is null)
            {
                throw new CustomCellRuleException(
                    CustomCellDiagnosticCodes.RegistrationInvalid,
                    "Custom cell rule registrations require a parameter validator and rule factory.");
            }

            if (ReferencesGodotAssembly(registration.ParameterValidator.Method.Module.Assembly) ||
                ReferencesGodotAssembly(registration.RuleFactory.Method.Module.Assembly))
            {
                throw new CustomCellRuleException(
                    CustomCellDiagnosticCodes.RegistrationInvalid,
                    "Custom cell rules cannot reference Godot assemblies.");
            }

            if (!CircuitValidator.IsSupportedBehaviorId(registration.BehaviorId))
            {
                throw new CustomCellRuleException(
                    CustomCellDiagnosticCodes.RegistrationInvalid,
                    "Custom cell rules require a namespaced, versioned behavior identifier.");
            }

            var ports = registration.Ports;
            if (ports.IsDefault)
            {
                throw new CustomCellRuleException(
                    CustomCellDiagnosticCodes.RegistrationInvalid,
                    $"Custom cell rule '{registration.BehaviorId}' has invalid ports.");
            }

            var portNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var port in ports)
            {
                if (string.IsNullOrEmpty(port.Name) ||
                    !StableData.IsParameterName(port.Name) ||
                    !Enum.IsDefined(port.Direction) ||
                    (!port.CanReceive && !port.CanDrive) ||
                    !portNames.Add(port.Name))
                {
                    throw new CustomCellRuleException(
                        CustomCellDiagnosticCodes.RegistrationInvalid,
                        $"Custom cell rule '{registration.BehaviorId}' has invalid ports.");
                }
            }

            if (!builder.TryAdd(registration.BehaviorId, registration))
            {
                throw new CustomCellRuleException(
                    CustomCellDiagnosticCodes.RegistrationDuplicate,
                    $"Custom cell rule '{registration.BehaviorId}' is registered more than once.");
            }
        }

        _rules = builder.ToImmutable();
    }

    private static bool ReferencesGodotAssembly(Assembly ruleAssembly)
    {
        var loadContext = AssemblyLoadContext.GetLoadContext(ruleAssembly) ?? AssemblyLoadContext.Default;
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        var pending = new Queue<Assembly>();
        var visited = new HashSet<string>(StringComparer.Ordinal)
        {
            ruleAssembly.FullName ?? string.Empty
        };
        pending.Enqueue(ruleAssembly);

        while (pending.TryDequeue(out var assembly))
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name?.StartsWith("Godot", StringComparison.Ordinal) == true)
                {
                    return true;
                }

                Assembly dependency;
                try
                {
                    dependency = loadContext.LoadFromAssemblyName(reference);
                }
                catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
                {
                    throw new CustomCellRuleException(
                        CustomCellDiagnosticCodes.RegistrationInvalid,
                        "Custom cell rule dependencies could not be inspected.",
                        exception);
                }

                if (string.Equals(
                        Path.GetDirectoryName(dependency.Location),
                        runtimeDirectory,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                    !visited.Add(dependency.FullName ?? reference.FullName ?? string.Empty))
                {
                    continue;
                }

                pending.Enqueue(dependency);
            }
        }

        return false;
    }

    internal CustomCellRuleRegistration Resolve(BehaviorId behaviorId)
    {
        if (_rules.TryGetValue(behaviorId, out var registration))
        {
            return registration;
        }

        throw new CustomCellRuleException(
            CustomCellDiagnosticCodes.RegistrationMissing,
            $"Custom cell rule '{behaviorId}' is not registered.");
    }

    internal ICustomCellRule CreateRule(CustomCellRuleRegistration registration)
    {
        try
        {
            var rule = registration.RuleFactory();
            if (rule is null || ReferencesGodotAssembly(rule.GetType().Assembly))
            {
                throw new InvalidOperationException("Custom cell rules cannot reference Godot assemblies.");
            }

            return rule;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new CustomCellRuleException(
                CustomCellDiagnosticCodes.RegistrationInvalid,
                "Custom cell rule factory failed or returned a rule with a Godot dependency.",
                exception);
        }
    }
}

public sealed record CustomCellRuleRegistration(
    BehaviorId BehaviorId,
    ImmutableArray<CustomCellPort> Ports,
    Action<ImmutableSortedDictionary<string, string>> ParameterValidator,
    Func<ICustomCellRule> RuleFactory)
{
}

internal sealed record CustomCellRuleBinding(
    ICustomCellRule Rule,
    ImmutableArray<CustomCellPort> Ports,
    ImmutableArray<byte> InitialState)
{
}

public static class CustomCellRandom
{
    public static ulong Next(ref ulong state)
    {
        var value = (state += 0x9E3779B97F4A7C15UL);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}

public sealed record PanelRuntimeSnapshot(
    CircuitId PanelId,
    int Width,
    int Height,
    ImmutableArray<PanelRuntimeCellSnapshot?> Cells,
    SchedulerSnapshot Scheduler,
    ImmutableArray<ProbeSample> ProbeHistory)
{
}

public sealed record PanelRuntimeCellSnapshot(
    ComponentId CellId,
    CellKind Kind,
    GridCoordinate Location,
    CardinalDirection Orientation,
    PortId? PortId,
    ImmutableSortedDictionary<string, string> Parameters,
    BehaviorId? BehaviorId,
    LogicValue ExternalInput,
    LogicValue CommittedOutput,
    LogicValue PendingValue,
    long PendingTick,
    LogicValue FilterCandidate,
    long FilterCandidateSinceTick,
    LogicValue PreviousData,
    LogicValue PreviousClock,
    long DataChangedTick,
    long NextClockTransitionTick,
    LogicValue ObservedValue,
    ImmutableArray<KeyValuePair<string, LogicValue>> LastForwarded,
    ImmutableArray<byte> CustomState,
    ulong CustomRandomState)
{
}
