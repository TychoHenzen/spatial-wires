using System.Collections.Immutable;
using Godot;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Persistence;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.GodotAdapter;

public static class SpatialCircuitEditorIdentity
{
    public static string HashPanel(PanelDefinition panel)
    {
        return DurableDefinitionCodec.ToDto(panel).ContentHash;
    }

    public static string HashDevice(DeviceDefinition device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var graph = DeviceGraphDefinition.Create(
            new DefinitionId("graph/editor-reference"),
            [device],
            []);
        return DurableDefinitionCodec.ToDto(graph).ContentHash;
    }
}

public sealed class SpatialCircuitEditorAuthoringSession
{
    private readonly SpatialCircuitDefinitionPublisher _publisher = new();

    public SpatialCircuitEditorAuthoringSession(PanelWorkbenchSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        References = new SpatialCircuitReferenceCatalog();
        RegisterPanel(session.Definition.Panel);
    }

    public PanelWorkbenchSession Session { get; private set; }

    public SpatialCircuitReferenceCatalog References { get; }

    public ChipDefinition? CurrentChip => _publisher.CurrentDefinition;

    public SpatialCircuitReference? OpenedReference { get; private set; }

    public int SchemaVersion => CurrentChip?.SchemaVersion ?? 1;

    public string ContentHash => CurrentChip?.ContentHash ??
                                 SpatialCircuitEditorIdentity.HashPanel(Session.Definition.Panel);

    public string IdentityText => $"Schema {SchemaVersion} | Hash {ContentHash}";

    public void BindSession(PanelWorkbenchSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        RegisterPanel(session.Definition.Panel);
    }

    public bool TryPaint(
        GridCoordinate location,
        CellKind kind,
        CardinalDirection orientation,
        out WorkbenchDiagnostic? diagnostic,
        PortId? portId = null,
        IEnumerable<KeyValuePair<string, string>>? parameters = null,
        BehaviorId? behaviorId = null) =>
        Session.TryPaint(location, kind, orientation, out diagnostic, portId, parameters, behaviorId);

    public bool TryCommit(out WorkbenchDiagnostic? diagnostic) => Session.CommitStaged(out diagnostic);

    public bool TrySavePanel(
        out SpatialCircuitPanelResource? resource,
        out SpatialCircuitReference? reference,
        out string diagnostic)
    {
        resource = CreatePanelResource(out reference);
        var savedResource = resource;
        References.Register(reference, savedResource, () => CreatePanelReference(savedResource));
        diagnostic = string.Empty;
        return true;
    }

    public bool TrySavePanel(
        string path,
        out SpatialCircuitReference? reference,
        out string diagnostic)
    {
        reference = null;
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("res://", StringComparison.Ordinal) &&
            !path.StartsWith("user://", StringComparison.Ordinal))
        {
            diagnostic = $"{SpatialCircuitEditorDiagnosticCodes.SaveInvalid}: resource path must use res:// or user://.";
            return false;
        }

        var resource = CreatePanelResource(out reference);

        var error = ResourceSaver.Save(resource, path);
        if (error != Error.Ok)
        {
            resource.Dispose();
            reference = null;
            diagnostic = $"{SpatialCircuitEditorDiagnosticCodes.SaveFailed}: could not save '{path}' ({error}).";
            return false;
        }

        var savedResource = resource;
        References.Register(reference, savedResource, () => CreatePanelReference(savedResource));
        LastSavedPath = path;
        diagnostic = string.Empty;
        return true;
    }

    public bool TryImportPanel(
        SpatialCircuitPanelResource resource,
        out SpatialCircuitReference? reference,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(resource);
        try
        {
            var panel = SpatialCircuitResourceAdapter.ToPanelDefinition(resource);
            reference = CreatePanelReference(resource);
            var nextSession = new PanelWorkbenchSession(panel);
            Session = nextSession;
            References.Register(reference, resource, () => CreatePanelReference(resource));
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or ChipDefinitionException)
        {
            reference = null;
            diagnostic = $"{SpatialCircuitEditorDiagnosticCodes.ImportInvalid}: {exception.Message}";
            return false;
        }
    }

    public bool TryPackageChip(
        DefinitionId definitionId,
        string symbol,
        out SpatialCircuitResource? resource,
        out SpatialCircuitReference? reference,
        out string diagnostic)
    {
        resource = null;
        reference = null;
        if (!Session.TryPackagePanelAsChip(definitionId, symbol, out var chip, out var workbenchDiagnostic) ||
            !Session.CommitStaged(out workbenchDiagnostic) || chip is null)
        {
            diagnostic = workbenchDiagnostic?.Message ??
                         $"{SpatialCircuitEditorDiagnosticCodes.ImportInvalid}: chip package failed.";
            return false;
        }

        resource = SpatialCircuitResourceAdapter.ToResource(chip);
        ChipDefinition published;
        try
        {
            published = SpatialCircuitResourceAdapter.ToDefinition(resource);
            reference = SpatialCircuitReference.Create(
                SpatialCircuitReferenceKind.Chip,
                published.Id.Value,
                published.ContentHash);
        }
        catch (Exception exception) when (exception is ArgumentException or ChipDefinitionException)
        {
            reference = null;
            diagnostic = $"{SpatialCircuitEditorDiagnosticCodes.ImportInvalid}: {exception.Message}";
            return false;
        }

        if (!_publisher.TryPublish(resource, out var publisherDiagnostic) ||
            _publisher.CurrentDefinition is not { } publishedRevision)
        {
            reference = null;
            diagnostic = $"{SpatialCircuitEditorDiagnosticCodes.ImportInvalid}: {publisherDiagnostic}";
            return false;
        }

        published = publishedRevision;
        reference = reference with { ContentHash = published.ContentHash };
        var publishedResource = resource;
        References.Register(reference, publishedResource, () => CreateChipReference(publishedResource));
        diagnostic = string.Empty;
        return true;
    }

    public bool TryImportChip(
        SpatialCircuitResource resource,
        out SpatialCircuitReference? reference,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ChipDefinition chip;
        SpatialCircuitReference nextReference;
        PanelWorkbenchSession nextSession;
        try
        {
            chip = SpatialCircuitResourceAdapter.ToDefinition(resource);
            nextReference = SpatialCircuitReference.Create(
                SpatialCircuitReferenceKind.Chip,
                chip.Id.Value,
                chip.ContentHash);
            nextSession = CreateSessionForChip(chip);
        }
        catch (Exception exception) when (exception is ArgumentException or ChipDefinitionException)
        {
            reference = null;
            diagnostic = $"{SpatialCircuitEditorDiagnosticCodes.ImportInvalid}: {exception.Message}";
            return false;
        }

        if (!_publisher.TryPublish(resource, out var publisherDiagnostic))
        {
            reference = null;
            diagnostic = $"{SpatialCircuitEditorDiagnosticCodes.ImportInvalid}: {publisherDiagnostic}";
            return false;
        }

        Session = nextSession;
        reference = nextReference;
        References.Register(reference, resource, () => CreateChipReference(resource));
        RegisterPanel(chip.SourcePanel);
        diagnostic = string.Empty;
        return true;
    }

    public void RegisterDeviceReference(DeviceDefinition device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var reference = SpatialCircuitReference.Create(
            SpatialCircuitReferenceKind.Device,
            device.Id.Value,
            SpatialCircuitEditorIdentity.HashDevice(device));
        References.Register(reference, device, () =>
            SpatialCircuitReference.Create(
                SpatialCircuitReferenceKind.Device,
                device.Id.Value,
                SpatialCircuitEditorIdentity.HashDevice(device)));
    }

    public bool TryOpenReference(SpatialCircuitReference reference, out string diagnostic)
    {
        if (References.TryOpen(reference, out var opened, out var target, out diagnostic))
        {
            OpenedReference = opened;
            OpenedTarget = target;
            return true;
        }

        OpenedTarget = null;
        return false;
    }

    public string? LastSavedPath { get; private set; }

    public object? OpenedTarget { get; private set; }

    private void RegisterPanel(PanelDefinition panel)
    {
        References.Register(SpatialCircuitReference.Create(
            SpatialCircuitReferenceKind.Panel,
            panel.Id.Value,
            SpatialCircuitEditorIdentity.HashPanel(panel)));
    }

    private SpatialCircuitPanelResource CreatePanelResource(out SpatialCircuitReference reference)
    {
        var resource = SpatialCircuitResourceAdapter.ToResource(Session.Definition.Panel);
        reference = CreatePanelReference(resource);
        return resource;
    }

    private static SpatialCircuitReference CreatePanelReference(SpatialCircuitPanelResource resource) =>
        SpatialCircuitReference.Create(
            SpatialCircuitReferenceKind.Panel,
            resource.PanelId,
            SpatialCircuitEditorIdentity.HashPanel(
                SpatialCircuitResourceAdapter.ToPanelDefinition(resource)));

    private static SpatialCircuitReference CreateChipReference(SpatialCircuitResource resource)
    {
        var chip = SpatialCircuitResourceAdapter.ToDefinition(resource);
        return SpatialCircuitReference.Create(
            SpatialCircuitReferenceKind.Chip,
            chip.Id.Value,
            chip.ContentHash);
    }

    private PanelWorkbenchSession CreateSessionForChip(ChipDefinition chip)
    {
        var catalog = ChipDefinitionCatalog.Create(
            Session.Definition.ChipCatalog.Definitions
                .Append(chip)
                .DistinctBy(item => (item.Id.Value, item.ContentHash)));
        var definition = WorkbenchDefinition.Restore(
            chip.SourcePanel,
            catalog,
            PanelOwnedChipNetworkDefinition.Create(chip.SourcePanel.Id, []),
            [],
            null,
            []);
        return new PanelWorkbenchSession(definition);
    }
}

public sealed record SpatialCircuitEditorAuthoringPracticeResult(
    bool Succeeded,
    PanelWorkbenchSession Session,
    SpatialCircuitPanelResource PanelResource,
    SpatialCircuitResource ChipResource,
    SpatialCircuitReference PanelReference,
    SpatialCircuitReference ChipReference,
    ImmutableArray<LogicValue> OutputTrace,
    bool InvalidImportPreserved,
    bool ReferencesOpened);

public static class SpatialCircuitEditorAuthoringPractice
{
    public static SpatialCircuitEditorAuthoringPracticeResult Run()
    {
        var panelPath = $"user://editor-authoring-panel-{Guid.NewGuid():N}.tres";
        var chipPath = $"user://editor-authoring-chip-{Guid.NewGuid():N}.tres";
        Resource? loadedPanel = null;
        Resource? loadedChip = null;
        var editor = new SpatialCircuitEditorAuthoringSession(new PanelWorkbenchSession(
            PanelDefinition.Create(new CircuitId("panel/editor-authoring"), 4, 1, [])));
        try
        {
            WorkbenchDiagnostic? workbenchDiagnostic;
            if (!editor.TryPaint(new GridCoordinate(0, 0), CellKind.InputPort, CardinalDirection.East,
                    out workbenchDiagnostic, new PortId("in")) ||
                !editor.TryPaint(new GridCoordinate(1, 0), CellKind.Wire, CardinalDirection.East,
                    out workbenchDiagnostic) ||
                !editor.TryPaint(new GridCoordinate(2, 0), CellKind.OutputPort, CardinalDirection.East,
                    out workbenchDiagnostic, new PortId("out")) ||
                !editor.TryCommit(out workbenchDiagnostic))
            {
                throw new InvalidOperationException(workbenchDiagnostic?.Message);
            }

            if (!editor.TrySavePanel(panelPath, out var panelReference, out var diagnostic) ||
                panelReference is null)
            {
                throw new InvalidOperationException(diagnostic);
            }

            var panelResource = SpatialCircuitResourceAdapter.ToResource(editor.Session.Definition.Panel);
            if (ClassDB.ClassExists(nameof(SpatialCircuitPanelResource)))
            {
                loadedPanel = ResourceLoader.Load<Resource>(panelPath);
            }
            if (loadedPanel is SpatialCircuitPanelResource loadedPanelResource &&
                SpatialCircuitEditorIdentity.HashPanel(
                    SpatialCircuitResourceAdapter.ToPanelDefinition(loadedPanelResource)) !=
                panelReference.ContentHash)
            {
                throw new InvalidOperationException("Saved panel hash does not match its pinned reference.");
            }

            if (!editor.TryPackageChip(
                    new DefinitionId("chip/editor-authoring"),
                    "editor-chip",
                    out var chipResource,
                    out var chipReference,
                    out diagnostic) || chipResource is null || chipReference is null)
            {
                throw new InvalidOperationException(diagnostic);
            }

            if (ResourceSaver.Save(chipResource, chipPath) != Error.Ok)
            {
                throw new InvalidOperationException("Saved chip resource could not be written.");
            }

            if (ClassDB.ClassExists(nameof(SpatialCircuitResource)))
            {
                loadedChip = ResourceLoader.Load<Resource>(chipPath);
            }
            var runtimeChip = editor.CurrentChip ?? throw new InvalidOperationException("Editor chip is missing.");
            if (loadedChip is SpatialCircuitResource loadedChipResource)
            {
                runtimeChip = SpatialCircuitResourceAdapter.ToDefinition(loadedChipResource);
            }

            var invalid = new SpatialCircuitResource
            {
                DefinitionId = "chip/invalid",
                SourcePanel = new SpatialCircuitPanelResource
                {
                    PanelId = "panel/invalid",
                    Width = 0,
                    Height = 1
                },
                Symbol = "invalid"
            };
            var priorChip = editor.CurrentChip;
            var invalidImportPreserved = !editor.TryImportChip(invalid, out _, out _) &&
                                         ReferenceEquals(editor.CurrentChip, priorChip);
            invalid.Dispose();

            var referencesOpened = editor.TryOpenReference(panelReference, out diagnostic) &&
                                   editor.OpenedTarget is SpatialCircuitPanelResource &&
                                   editor.TryOpenReference(chipReference, out diagnostic) &&
                                   editor.OpenedTarget is SpatialCircuitResource;

            var runtime = new PanelWorkbenchSession(PanelDefinition.Create(
                new CircuitId("panel/editor-runtime"), 1, 1, []));
            var instanceId = new ComponentId("chip/editor-instance");
            if (!runtime.TryPlaceChip(runtimeChip, instanceId, new GridCoordinate(0, 0),
                    out workbenchDiagnostic) ||
                !runtime.CommitStaged(out workbenchDiagnostic) ||
                !runtime.TryDriveChipInput(instanceId, "in", LogicValue.High, out workbenchDiagnostic))
            {
                throw new InvalidOperationException(workbenchDiagnostic?.Message);
            }

            var outputTrace = ImmutableArray.CreateBuilder<LogicValue>();
            for (var index = 0; index < 4; index++)
            {
                runtime.StepMicrotick();
                outputTrace.Add(runtime.GetChipOutput(instanceId, "out"));
            }

            var panelHashMatches = SpatialCircuitEditorIdentity.HashPanel(
                SpatialCircuitResourceAdapter.ToPanelDefinition(panelResource)) ==
                panelReference.ContentHash;
            var chipHashMatches = SpatialCircuitResourceAdapter.ToDefinition(chipResource).ContentHash ==
                                  chipReference.ContentHash;
            var succeeded = panelHashMatches && chipHashMatches && invalidImportPreserved &&
                            referencesOpened && outputTrace.Contains(LogicValue.High);
            return new SpatialCircuitEditorAuthoringPracticeResult(
                succeeded,
                runtime,
                panelResource,
                chipResource,
                panelReference,
                chipReference,
                outputTrace.ToImmutable(),
                invalidImportPreserved,
                referencesOpened);
        }
        finally
        {
            loadedPanel?.Dispose();
            loadedChip?.Dispose();
            RemoveUserResource(panelPath);
            RemoveUserResource(chipPath);
        }
    }

    private static void RemoveUserResource(string path)
    {
        var absolutePath = ProjectSettings.GlobalizePath(path);
        if (Godot.FileAccess.FileExists(path))
        {
            DirAccess.RemoveAbsolute(absolutePath);
        }
    }
}
