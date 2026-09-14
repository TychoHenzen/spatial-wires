using System.Text.RegularExpressions;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;

namespace SpatialCircuits.Runner;

public static partial class FixtureValidator
{
    /// <summary>Maximum number of scheduled-drive microticks accepted by the runner.</summary>
    public const int MaxScheduledDriveMicroticks = 100_000;

    public static IReadOnlyList<FixtureDiagnostic> Validate(RunnerFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var diagnostics = new List<FixtureDiagnostic>();
        if (fixture.FixtureSchema != FixtureVersion.Current)
        {
            Add(diagnostics, FixtureDiagnosticCodes.SchemaUnsupported, "$.fixtureSchema", "Fixture schema is not supported.");
        }

        if (fixture.TraceSchema != TraceVersion.Current)
        {
            Add(diagnostics, FixtureDiagnosticCodes.TraceSchemaUnsupported, "$.traceSchema", "Trace schema is not supported.");
        }

        if (!StableIdPattern().IsMatch(fixture.FixtureId))
        {
            Add(diagnostics, FixtureDiagnosticCodes.IdInvalid, "$.fixtureId", "Fixture identifier is not stable data.");
        }

        if (fixture.Action is not (
                FixtureAction.ResolveDrives or
                FixtureAction.ScheduledDrive or
                FixtureAction.PanelScenario or
                FixtureAction.ChipNetworkScenario))
        {
            Add(diagnostics, FixtureDiagnosticCodes.ActionUnsupported, "$.action", "Fixture action is not supported.");
        }

        if (fixture.Action == FixtureAction.ScheduledDrive)
        {
            ValidateScheduledDrive(fixture.ScheduledDrive, diagnostics);
        }
        else if (fixture.Action == FixtureAction.PanelScenario)
        {
            ValidatePanelScenario(fixture.PanelScenario, diagnostics);
        }
        else if (fixture.Action == FixtureAction.ChipNetworkScenario)
        {
            ValidateChipNetworkScenario(fixture.ChipNetworkScenario, diagnostics);
        }
        else if (fixture.Cases.Length == 0)
        {
            Add(diagnostics, FixtureDiagnosticCodes.CasesRequired, "$.cases", "Fixture must contain at least one case.");
        }
        else
        {
            ValidateCases(fixture, diagnostics);
        }
        return diagnostics.AsReadOnly();
    }

    private static void ValidateChipNetworkScenario(
        ChipNetworkScenarioPlan? plan,
        ICollection<FixtureDiagnostic> diagnostics)
    {
        const string root = "$.chipNetworkScenario";
        if (plan is null)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.ChipScenarioRequired,
                root,
                "Chip-network scenarios require a chipNetworkScenario object.");
            return;
        }

        if (plan.Microticks < 1 || plan.Microticks > MaxScheduledDriveMicroticks)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.ChipScenarioInvalid,
                $"{root}.microticks",
                $"Microticks must be between 1 and {MaxScheduledDriveMicroticks}.");
        }

        if (plan.Definitions.IsDefault || plan.Instances.IsDefault || plan.Connections.IsDefault ||
            plan.Inputs.IsDefault || plan.Expectations.IsDefault || plan.Definitions.Length == 0 || plan.Instances.Length == 0)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.ChipScenarioInvalid,
                root,
                "Chip definitions, instances, connections, inputs, and expectations must be initialized, with at least one definition and instance.");
            return;
        }

        PanelDefinition parentPanel;
        ChipDefinitionCatalog catalog;
        PanelOwnedChipNetworkDefinition network;
        try
        {
            parentPanel = plan.ParentPanel.CreatePanelDefinition();
            catalog = plan.CreateCatalog();
            network = plan.CreateNetworkDefinition();
        }
        catch (ChipDefinitionException exception)
        {
            foreach (var diagnostic in exception.Diagnostics)
            {
                Add(
                    diagnostics,
                    FixtureDiagnosticCodes.ChipScenarioInvalid,
                    $"{root}.{diagnostic.Path}",
                    diagnostic.Message);
            }

            return;
        }
        catch (ArgumentException)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.ChipScenarioInvalid,
                root,
                "Chip panel, definition, port, instance, or connection data is invalid.");
            return;
        }

        foreach (var diagnostic in network.Validate(parentPanel, catalog))
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.ChipScenarioInvalid,
                $"{root}.{diagnostic.Path}",
                diagnostic.Message);
        }

        var inputChanges = new HashSet<(int Tick, string InstanceId, string PortName)>();
        for (var index = 0; index < plan.Inputs.Length; index++)
        {
            var input = plan.Inputs[index];
            var path = $"{root}.inputs[{index}]";
            var instance = plan.Instances.FirstOrDefault(item =>
                string.Equals(item.InstanceId, input.InstanceId, StringComparison.Ordinal));
            var isInputPort = instance is not null &&
                catalog.TryResolve(new DefinitionId(instance.DefinitionId), instance.ContentHash, out var definition) &&
                definition is not null && definition.Ports.Any(port =>
                    string.Equals(port.Name, input.PortName, StringComparison.Ordinal) &&
                    port.Direction == ChipPortDirection.Input);
            var isConnected = input.Tick >= 0 && input.Tick < plan.Microticks &&
                network.Connections.Any(connection =>
                    connection.Target.InstanceId is { } targetInstance &&
                    string.Equals(targetInstance.Value, input.InstanceId, StringComparison.Ordinal) &&
                    string.Equals(connection.Target.PortName, input.PortName, StringComparison.Ordinal));
            if (input.Tick < 0 || input.Tick >= plan.Microticks || !isInputPort || isConnected ||
                !FixtureLogicValue.TryParse(input.Value, out _))
            {
                Add(
                    diagnostics,
                    FixtureDiagnosticCodes.ChipScenarioInvalid,
                    path,
                    "Inputs must name a defined chip input, an in-range tick, and a four-state value.");
            }

            if (!inputChanges.Add((input.Tick, input.InstanceId, input.PortName)))
            {
                Add(
                    diagnostics,
                    FixtureDiagnosticCodes.ChipScenarioInvalid,
                    path,
                    "A named chip input can change only once at a microtick.");
            }
        }

        var expectedRecordCount = (long)plan.Microticks * plan.Instances.Length;
        if (expectedRecordCount != plan.Expectations.Length)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.ChipScenarioInvalid,
                $"{root}.expectations",
                "Each chip instance must have an expected output trace and hash for every microtick.");
        }

        var expectations = new HashSet<(int Tick, string InstanceId)>();
        for (var index = 0; index < plan.Expectations.Length; index++)
        {
            var expectation = plan.Expectations[index];
            var path = $"{root}.expectations[{index}]";
            var instance = plan.Instances.FirstOrDefault(item =>
                string.Equals(item.InstanceId, expectation.InstanceId, StringComparison.Ordinal));
            var definition = instance is not null && catalog.TryResolve(
                new DefinitionId(instance.DefinitionId),
                instance.ContentHash,
                out var resolved)
                ? resolved
                : null;
            var expectedPortNames = definition?.Ports
                .Where(port => port.Direction == ChipPortDirection.Output)
                .Select(port => port.Name)
                .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
            if (expectation.Tick < 0 || expectation.Tick >= plan.Microticks || definition is null ||
                !ChipDataHashIsValid(expectation.Hash) ||
                !expectation.Outputs.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expectedPortNames) ||
                expectation.Outputs.Values.Any(value => !FixtureLogicValue.TryParse(value, out _)))
            {
                Add(
                    diagnostics,
                    FixtureDiagnosticCodes.ChipScenarioInvalid,
                    path,
                    "Expectations must name a defined chip instance, include every output, and use valid values and a SHA-256 hash.");
            }

            if (!expectations.Add((expectation.Tick, expectation.InstanceId)))
            {
                Add(
                    diagnostics,
                    FixtureDiagnosticCodes.ChipScenarioInvalid,
                    path,
                    "Chip expectations cannot duplicate a microtick and instance pair.");
            }
        }
    }

    private static bool ChipDataHashIsValid(string value) => ChipHashPattern().IsMatch(value);

    private static void ValidatePanelScenario(
        PanelScenarioPlan? plan,
        ICollection<FixtureDiagnostic> diagnostics)
    {
        if (plan is null)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.PanelScenarioRequired,
                "$.panelScenario",
                "Panel-scenario fixtures require a panelScenario object.");
            return;
        }

        if (plan.Microticks < 1 || plan.Microticks > MaxScheduledDriveMicroticks)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.PanelScenarioInvalid,
                "$.panelScenario.microticks",
                $"Microticks must be between 1 and {MaxScheduledDriveMicroticks}.");
        }

        if (plan.Cells.IsDefault || plan.Inputs.IsDefault || plan.Expectations.IsDefault)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.PanelScenarioInvalid,
                "$.panelScenario",
                "Panel cells, inputs, and expectations must be initialized arrays.");
            return;
        }

        PanelDefinition panel;
        try
        {
            panel = plan.CreatePanelDefinition();
        }
        catch (ArgumentException)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.PanelScenarioInvalid,
                "$.panelScenario.cells",
                "Panel dimensions, cells, orientations, or cell parameters are invalid.");
            return;
        }

        var inputPortIds = panel.Cells
            .OfType<PanelCellDefinition>()
            .Where(cell => cell.Kind == CellKind.InputPort)
            .Select(cell => cell.PortId!.Value.Value)
            .ToHashSet(StringComparer.Ordinal);
        var inputChanges = new HashSet<(int Tick, string PortId)>();
        for (var index = 0; index < plan.Inputs.Length; index++)
        {
            var input = plan.Inputs[index];
            var path = $"$.panelScenario.inputs[{index}]";
            if (input.Tick < 0 || input.Tick >= plan.Microticks ||
                !inputPortIds.Contains(input.PortId) ||
                !FixtureLogicValue.TryParse(input.Value, out _))
            {
                Add(
                    diagnostics,
                    FixtureDiagnosticCodes.PanelScenarioInvalid,
                    path,
                    "Input changes must use a defined panel input, an in-range tick, and a four-state value.");
            }

            if (!inputChanges.Add((input.Tick, input.PortId)))
            {
                Add(
                    diagnostics,
                    FixtureDiagnosticCodes.PanelScenarioInvalid,
                    path,
                    "A panel input can change only once at a microtick.");
            }
        }

        var probeIds = panel.Cells
            .OfType<PanelCellDefinition>()
            .Where(cell => cell.Kind == CellKind.Probe)
            .Select(cell => cell.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (probeIds.Count == 0)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.PanelScenarioInvalid,
                "$.panelScenario.cells",
                "Panel scenarios require at least one probe.");
        }

        var expectedCount = (long)plan.Microticks * probeIds.Count;
        if (expectedCount != plan.Expectations.Length)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.PanelScenarioInvalid,
                "$.panelScenario.expectations",
                "Each probe must have one expected value for every microtick.");
        }

        var expectedSamples = new HashSet<(int Tick, string ProbeId)>();
        for (var index = 0; index < plan.Expectations.Length; index++)
        {
            var expectation = plan.Expectations[index];
            var path = $"$.panelScenario.expectations[{index}]";
            if (expectation.Tick < 0 || expectation.Tick >= plan.Microticks ||
                !probeIds.Contains(expectation.ProbeId) ||
                !FixtureLogicValue.TryParse(expectation.Value, out _))
            {
                Add(
                    diagnostics,
                    FixtureDiagnosticCodes.PanelScenarioInvalid,
                    path,
                    "Expectations must name a defined probe, an in-range tick, and a four-state value.");
            }

            if (!expectedSamples.Add((expectation.Tick, expectation.ProbeId)))
            {
                Add(
                    diagnostics,
                    FixtureDiagnosticCodes.PanelScenarioInvalid,
                    path,
                    "Probe expectations cannot duplicate a microtick and probe pair.");
            }
        }
    }

    private static void ValidateScheduledDrive(
        ScheduledDrivePlan? plan,
        ICollection<FixtureDiagnostic> diagnostics)
    {
        if (plan is null)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.ScheduledDriveRequired,
                "$.scheduledDrive",
                "Scheduled-drive fixtures require a scheduledDrive object.");
            return;
        }

        if (plan.Microticks < 1 || plan.Microticks > MaxScheduledDriveMicroticks ||
            plan.SnapshotAfter < 1 || plan.SnapshotAfter >= plan.Microticks)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.ScheduledDriveInvalid,
                "$.scheduledDrive.microticks",
                $"Microticks must be between 1 and {MaxScheduledDriveMicroticks}, and snapshotAfter must be inside the run.");
        }

        if (plan.ReleaseAt < plan.SnapshotAfter || plan.ReleaseAt >= plan.Microticks)
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.ScheduledDriveInvalid,
                "$.scheduledDrive.releaseAt",
                "releaseAt must occur after the snapshot and before the run ends.");
        }

        if (!StableIdPattern().IsMatch(plan.SourceId) || !StableIdPattern().IsMatch(plan.TargetId) ||
            string.Equals(plan.SourceId, plan.TargetId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(plan.SourcePort) || string.IsNullOrWhiteSpace(plan.TargetPort) ||
            !FixtureLogicValue.TryParse(plan.Drive, out _))
        {
            Add(
                diagnostics,
                FixtureDiagnosticCodes.ScheduledDriveInvalid,
                "$.scheduledDrive",
                "Scheduled-drive identifiers, ports, and drive value are invalid.");
        }
    }

    private static void ValidateCases(RunnerFixture fixture, ICollection<FixtureDiagnostic> diagnostics)
    {
        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        for (var caseIndex = 0; caseIndex < fixture.Cases.Length; caseIndex++)
        {
            var item = fixture.Cases[caseIndex];
            var path = $"$.cases[{caseIndex}]";
            if (!StableIdPattern().IsMatch(item.CaseId))
            {
                Add(diagnostics, FixtureDiagnosticCodes.CaseIdInvalid, $"{path}.caseId", "Case identifier is not stable data.");
            }
            else if (!caseIds.Add(item.CaseId))
            {
                Add(diagnostics, FixtureDiagnosticCodes.CaseIdDuplicate, $"{path}.caseId", "Case identifier is duplicated.");
            }

            for (var driveIndex = 0; driveIndex < item.Drives.Length; driveIndex++)
            {
                if (!FixtureLogicValue.TryParse(item.Drives[driveIndex], out _))
                {
                    Add(
                        diagnostics,
                        FixtureDiagnosticCodes.DriveInvalid,
                        $"{path}.drives[{driveIndex}]",
                        "Drive value is not a four-state logic value.");
                }
            }

            if (!FixtureLogicValue.TryParse(item.Expected, out _))
            {
                Add(
                    diagnostics,
                    FixtureDiagnosticCodes.ExpectedInvalid,
                    $"{path}.expected",
                    "Expected value is not a four-state logic value.");
            }
        }
    }

    private static void Add(
        ICollection<FixtureDiagnostic> diagnostics,
        string code,
        string path,
        string message) => diagnostics.Add(new FixtureDiagnostic(code, path, message));

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._/-]*[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdPattern();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChipHashPattern();
}
