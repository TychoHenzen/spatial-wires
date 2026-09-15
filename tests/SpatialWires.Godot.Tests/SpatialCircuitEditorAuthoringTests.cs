using GdUnit4;
using Godot;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.GodotAdapter;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Workbench;
using static GdUnit4.Assertions;

namespace SpatialWires.Godot.Tests;

[TestSuite]
public sealed class SpatialCircuitEditorAuthoringTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void EditorPracticeAuthorsAndRunsTheSameHashPinnedChip()
    {
        var result = SpatialCircuitEditorAuthoringPractice.Run();
        try
        {
            AssertThat(result.Succeeded).IsTrue();
            AssertThat(result.PanelReference.ContentHash).IsEqual(
                SpatialCircuitEditorIdentity.HashPanel(
                    SpatialCircuitResourceAdapter.ToPanelDefinition(result.PanelResource)));
            AssertThat(result.ChipReference.ContentHash).IsEqual(
                SpatialCircuitResourceAdapter.ToDefinition(result.ChipResource).ContentHash);
            AssertThat(result.OutputTrace.Contains(LogicValue.High)).IsTrue();
            AssertThat(result.InvalidImportPreserved).IsTrue();
            AssertThat(result.ReferencesOpened).IsTrue();
        }
        finally
        {
            result.PanelResource.Dispose();
            result.ChipResource.Dispose();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void InvalidImportKeepsTheLastPublishedRevisionAndReportsAStableDiagnostic()
    {
        var editor = new SpatialCircuitEditorAuthoringSession(new PanelWorkbenchSession(
            PanelDefinition.Create(new CircuitId("panel/editor-import"), 1, 1, [])));
        var valid = SpatialCircuitEditorAuthoringPractice.Run();
        try
        {
            AssertThat(editor.TryImportChip(valid.ChipResource, out _, out var validDiagnostic)).IsTrue();
            AssertThat(validDiagnostic).IsEqual(string.Empty);
            var published = editor.CurrentChip;
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

            AssertThat(editor.TryImportChip(invalid, out _, out var diagnostic)).IsFalse();
            AssertThat(diagnostic).Contains(SpatialCircuitEditorDiagnosticCodes.ImportInvalid);
            AssertThat(ReferenceEquals(editor.CurrentChip, published)).IsTrue();
            invalid.Dispose();
        }
        finally
        {
            valid.PanelResource.Dispose();
            valid.ChipResource.Dispose();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void DockExposesEditorIdentityAndPracticeControls()
    {
        var sceneTree = Engine.GetMainLoop() as SceneTree;
        AssertThat(sceneTree).IsNotNull();
        var dock = new SpatialCircuitsDock(new PanelWorkbenchSession(
            PanelDefinition.Create(new CircuitId("panel/editor-dock"), 1, 1, [])));
        sceneTree!.Root.AddChild(dock);
        try
        {
            var practiceButton = dock.FindChild("EditorPractice", recursive: true, owned: false) as Button;
            var identity = dock.FindChild("Identity", recursive: true, owned: false) as Label;
            AssertThat(practiceButton).IsNotNull();
            AssertThat(identity).IsNotNull();
            practiceButton!.EmitSignal(BaseButton.SignalName.Pressed);
            AssertThat(dock.LastEditorPracticeResult).IsNotNull();
            AssertThat(dock.LastEditorPracticeResult!.Succeeded).IsTrue();
            AssertThat(identity!.Text).Contains("Schema");
            AssertThat(identity.Text).Contains("Hash");
        }
        finally
        {
            dock.Free();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void DeviceReferenceOpensByItsPinnedIdentity()
    {
        var editor = new SpatialCircuitEditorAuthoringSession(new PanelWorkbenchSession(
            PanelDefinition.Create(new CircuitId("panel/device-reference"), 1, 1, [])));
        var device = DeviceDefinition.Create(
            new ComponentId("device/editor-reference"),
            TimedDeviceBackendDefinition.Create(
                [DevicePortDefinition.Create("out", DevicePortDirection.Output)],
                []));
        editor.RegisterDeviceReference(device);
        var reference = editor.References.References.Single(item =>
            item.Kind == SpatialCircuitReferenceKind.Device);

        AssertThat(editor.TryOpenReference(reference, out var diagnostic)).IsTrue();
        AssertThat(diagnostic).IsEqual(string.Empty);
        AssertThat(editor.OpenedTarget).IsEqual(device);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void ImportedChipIsAvailableForPinnedPlacement()
    {
        var result = SpatialCircuitEditorAuthoringPractice.Run();
        var editor = new SpatialCircuitEditorAuthoringSession(new PanelWorkbenchSession(
            PanelDefinition.Create(new CircuitId("panel/import-placement"), 1, 1, [])));
        try
        {
            AssertThat(editor.TryImportChip(result.ChipResource, out _, out var diagnostic)).IsTrue();
            AssertThat(diagnostic).IsEqual(string.Empty);
            var chip = editor.CurrentChip!;
            AssertThat(editor.Session.Definition.ChipCatalog.TryResolve(
                chip.Id,
                chip.ContentHash,
                out _)).IsTrue();
            AssertThat(editor.Session.TryPlaceChip(
                chip,
                new ComponentId("chip/imported"),
                new GridCoordinate(3, 0),
                out var workbenchDiagnostic)).IsTrue();
            AssertThat(editor.Session.CommitStaged(out workbenchDiagnostic)).IsTrue();
        }
        finally
        {
            result.PanelResource.Dispose();
            result.ChipResource.Dispose();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void MutableReferenceTargetIsRejectedAfterItsHashChanges()
    {
        var panel = new SpatialCircuitPanelResource
        {
            PanelId = "panel/mutable-reference",
            Width = 1,
            Height = 1
        };
        var editor = new SpatialCircuitEditorAuthoringSession(new PanelWorkbenchSession(
            PanelDefinition.Create(new CircuitId("panel/reference-owner"), 1, 1, [])));
        try
        {
            AssertThat(editor.TryImportPanel(panel, out var reference, out var importDiagnostic)).IsTrue();
            AssertThat(importDiagnostic).IsEqual(string.Empty);
            AssertThat(editor.TryOpenReference(reference!, out var openDiagnostic)).IsTrue();
            AssertThat(openDiagnostic).IsEqual(string.Empty);
            panel.Width = 2;
            AssertThat(editor.TryOpenReference(reference!, out openDiagnostic)).IsFalse();
            AssertThat(openDiagnostic).Contains(SpatialCircuitEditorDiagnosticCodes.ReferenceHashMismatch);
        }
        finally
        {
            panel.Dispose();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void FailedPanelSaveDoesNotPublishItsResourceTarget()
    {
        var editor = new SpatialCircuitEditorAuthoringSession(new PanelWorkbenchSession(
            PanelDefinition.Create(new CircuitId("panel/save-failure"), 1, 1, [])));
        var original = editor.References.References.Single(item =>
            item.Kind == SpatialCircuitReferenceKind.Panel);
        var path = $"res://missing-editor-save-{Guid.NewGuid():N}/panel.tres";
        try
        {
            AssertThat(editor.TrySavePanel(path, out _, out var diagnostic)).IsFalse();
            AssertThat(diagnostic).Contains(SpatialCircuitEditorDiagnosticCodes.SaveFailed);
            AssertThat(editor.TryOpenReference(original, out diagnostic)).IsTrue();
            AssertThat(editor.OpenedTarget).IsNull();
        }
        finally
        {
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        }
    }
}
