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
public sealed class PanelWorkbenchCanvasTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void WaveformCanvasProjectsSamplesWithoutCreatingPerSampleNodes()
    {
        var sceneTree = Engine.GetMainLoop() as SceneTree;
        AssertThat(sceneTree).IsNotNull();
        var waveform = new PanelWorkbenchWaveform();
        sceneTree!.Root.AddChild(waveform);

        try
        {
            waveform.Present(
            [
                new ProbeSample(new ComponentId("probe"), 31, LogicValue.Low),
                new ProbeSample(new ComponentId("probe"), 32, LogicValue.High),
                new ProbeSample(new ComponentId("probe"), 33, LogicValue.Low)
            ]);
            AssertThat(waveform.GetChildCount()).IsEqual(0);
            AssertThat(waveform.Samples.Select(sample => sample.Tick).SequenceEqual([31L, 32L, 33L])).IsTrue();
        }
        finally
        {
            waveform.Free();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void PublicPracticeDrawsXorPackagesItAndMatchesDelayedBundleTrace()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var practice = WorkbenchPractice.Run(
            File.ReadAllBytes(Path.Combine(fixtures, "xor-glitch.fixture.json")));
        AssertThat(practice.Succeeded).IsTrue();
        AssertThat(practice.XorOutputTrace[33]).IsEqual(LogicValue.Low);
        AssertThat(practice.XorOutputTrace[34]).IsEqual(LogicValue.High);
        AssertThat(practice.XorOutputTrace[35]).IsEqual(LogicValue.Low);
        AssertThat(practice.PlacedChipOutputTrace).IsEqual(practice.XorOutputTrace);
        AssertThat(practice.XorWaveform.Single(sample => sample.Tick == 34).Value).IsEqual(LogicValue.High);
        AssertThat(practice.PackagedChip.Symbol).IsEqual("xor");
        AssertThat(practice.Session.CommittedDefinition.ChipPlacements.Length).IsEqual(1);
        AssertThat(practice.Session.CommittedDefinition.DeviceGraph!.Bundles.Length).IsEqual(1);
        AssertThat(practice.CableHistory.Length > 0).IsTrue();
        AssertThat(practice.CableHistory.All(item => item.Status == CableTransitionStatus.Delivered)).IsTrue();

        var sceneTree = Engine.GetMainLoop() as SceneTree;
        AssertThat(sceneTree).IsNotNull();
        var canvas = new SpatialCircuitsWorkbenchCanvas();
        var waveform = new PanelWorkbenchWaveform();
        canvas.Bind(practice.Session);
        sceneTree!.Root.AddChild(canvas);
        sceneTree.Root.AddChild(waveform);
        try
        {
            canvas.QueueRedraw();
            waveform.Present(practice.XorWaveform);
            AssertThat(canvas.GetChildCount()).IsEqual(0);
            AssertThat(waveform.Samples.Single(sample => sample.Tick == 34).Value).IsEqual(LogicValue.High);
        }
        finally
        {
            canvas.Free();
            waveform.Free();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void DockRunPauseStepAndTeardownUseSessionMicroticks()
    {
        var sceneTree = Engine.GetMainLoop() as SceneTree;
        AssertThat(sceneTree).IsNotNull();
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/dock-controls"),
            1,
            1,
            []));
        var dock = new SpatialCircuitsDock(session);
        sceneTree!.Root.AddChild(dock);

        try
        {
            var runButton = dock.FindChild("RunPause", recursive: true, owned: false) as Button;
            var stepButton = dock.FindChild("Step", recursive: true, owned: false) as Button;
            var timer = dock.GetChildren().OfType<global::Godot.Timer>().Single();
            AssertThat(runButton).IsNotNull();
            AssertThat(stepButton).IsNotNull();

            runButton!.EmitSignal(BaseButton.SignalName.Pressed);
            AssertThat(session.IsPaused).IsFalse();
            timer.EmitSignal(global::Godot.Timer.SignalName.Timeout);
            AssertThat(session.CurrentTick).IsEqual(1L);

            runButton.EmitSignal(BaseButton.SignalName.Pressed);
            AssertThat(session.IsPaused).IsTrue();
            AssertThat(timer.IsStopped()).IsTrue();
            stepButton!.EmitSignal(BaseButton.SignalName.Pressed);
            AssertThat(session.CurrentTick).IsEqual(2L);

            runButton.EmitSignal(BaseButton.SignalName.Pressed);
            AssertThat(session.IsPaused).IsFalse();
        }
        finally
        {
            if (GodotObject.IsInstanceValid(dock))
            {
                dock.Free();
            }
        }

        AssertThat(GodotObject.IsInstanceValid(dock)).IsFalse();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void DockPracticeDisplaysTheRawGlitchAtItsExactTicks()
    {
        var sceneTree = Engine.GetMainLoop() as SceneTree;
        AssertThat(sceneTree).IsNotNull();
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/dock-practice"),
            1,
            1,
            []));
        var dock = new SpatialCircuitsDock(session);
        sceneTree!.Root.AddChild(dock);

        try
        {
            var practiceButton = dock.FindChild("XorPractice", recursive: true, owned: false) as Button;
            AssertThat(practiceButton).IsNotNull();
            practiceButton!.EmitSignal(BaseButton.SignalName.Pressed);

            var waveform = dock.FindChild("Waveform", recursive: true, owned: false)
                as PanelWorkbenchWaveform;
            AssertThat(waveform).IsNotNull();
            AssertThat(waveform!.Samples.Single(sample => sample.Tick == 33).Value).IsEqual(LogicValue.Low);
            AssertThat(waveform.Samples.Single(sample => sample.Tick == 34).Value).IsEqual(LogicValue.High);
            AssertThat(waveform.Samples.Single(sample => sample.Tick == 35).Value).IsEqual(LogicValue.Low);
        }
        finally
        {
            dock.Free();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void DockTamperBypassPracticeDisconnectsAndAcceptsTheReplacementResponder()
    {
        var sceneTree = Engine.GetMainLoop() as SceneTree;
        AssertThat(sceneTree).IsNotNull();
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            new CircuitId("panel/dock-tamper-practice"),
            1,
            1,
            []));
        var dock = new SpatialCircuitsDock(session);
        sceneTree!.Root.AddChild(dock);

        try
        {
            var practiceButton = dock.FindChild("TamperBypassPractice", recursive: true, owned: false) as Button;
            var status = dock.FindChild("PracticeStatus", recursive: true, owned: false) as Label;
            AssertThat(practiceButton).IsNotNull();
            AssertThat(status).IsNotNull();
            practiceButton!.EmitSignal(BaseButton.SignalName.Pressed);
            AssertThat(status!.Text).Contains("accepted response");
            AssertThat(status.Text).Contains("no alarm");
            var result = dock.LastTamperPracticeResult;
            AssertThat(result).IsNotNull();
            AssertThat(result!.Succeeded).IsTrue();
            AssertThat(result.OriginalDisconnected).IsTrue();
            AssertThat(result.PlayerResponderInserted).IsTrue();
            AssertThat(result.ResponseAccepted).IsTrue();
            AssertThat(result.AlarmLatched).IsFalse();
            AssertThat(result.ResponderComputedExpected).IsTrue();
            AssertThat(result.TopologyCasesPassed).IsTrue();
            AssertThat(result.ReplacementCableHistory.Any(item =>
                item.Status == CableTransitionStatus.Delivered)).IsTrue();
        }
        finally
        {
            dock.Free();
        }
    }
}
