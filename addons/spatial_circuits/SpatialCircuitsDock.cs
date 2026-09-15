using Godot;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Hierarchy;
using SpatialCircuits.Runner;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.GodotAdapter;

[Tool]
public partial class SpatialCircuitsDock : EditorDock
{
    private enum Tool
    {
        Select,
        Paint,
        Erase,
        Rotate
    }

    private static readonly CellKind[] PaintableKinds =
    [
        CellKind.Wire,
        CellKind.Junction,
        CellKind.Crossing,
        CellKind.Constant,
        CellKind.InputPort,
        CellKind.OutputPort,
        CellKind.Probe,
        CellKind.Nand,
        CellKind.Clock,
        CellKind.DFlipFlop,
        CellKind.StabilityFilter
    ];

    private static readonly LogicValue[] LogicValues = Enum.GetValues<LogicValue>();

    private PanelWorkbenchSession _session;
    private readonly SpatialCircuitsWorkbenchCanvas _canvas;
    private readonly OptionButton _toolPicker = new();
    private readonly OptionButton _cellPicker = new();
    private readonly OptionButton _directionPicker = new();
    private readonly OptionButton _valuePicker = new();
    private readonly SpinBox _cycleTicks = new();
    private readonly SpinBox _cableLatency = new();
    private readonly Label _status = new();
    private readonly Label _topology = new();
    private readonly Label _deviceStatus = new();
    private readonly OptionButton _probePicker = new();
    private readonly PanelWorkbenchWaveform _waveform = new();
    private readonly Label _waveformValues = new();
    private readonly Label _practiceStatus = new();
    private readonly RichTextLabel _commandLog = new();
    private readonly Button _runButton = new();
    private readonly Godot.Timer _runTimer = new();
    private string? _activeProbeId;
    private ProbeSample[]? _practiceRawWaveform;
    private bool _refreshingProbePicker;
    private int _deviceOrdinal;
    private int _laneOrdinal;
    private int _chipOrdinal;

    public SpatialCircuitsDock() : this(CreateEmptySession())
    {
    }

    public SpatialCircuitsDock(PanelWorkbenchSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _canvas = new SpatialCircuitsWorkbenchCanvas { Name = "Canvas" };
        _canvas.Bind(_session);
        _canvas.CellPressed += OnCellPressed;
        _session.Changed += Refresh;

        Name = SpatialCircuitsEditorPlugin.DockName;
        Title = SpatialCircuitsEditorPlugin.DockTitle;
        LayoutKey = SpatialCircuitsEditorPlugin.PluginId;
        DefaultSlot = DockSlot.LeftUr;
        Global = true;

        BuildUi();
        _runTimer.WaitTime = 0.12;
        _runTimer.Timeout += OnRunTimeout;
        AddChild(_runTimer);
        Refresh();
    }

    public PanelWorkbenchSession WorkbenchSession => _session;

    public TamperDetectionPracticeResult? LastTamperPracticeResult { get; private set; }

    public SpatialCircuitsWorkbenchCanvas WorkbenchCanvas => _canvas;

    public override void _ExitTree()
    {
        _runTimer.Stop();
        _session.Changed -= Refresh;
        _canvas.CellPressed -= OnCellPressed;
    }

    private static PanelWorkbenchSession CreateEmptySession() => new(PanelDefinition.Create(
        new CircuitId("panel/workbench"),
        12,
        8,
        []));

    private void BuildUi()
    {
        var root = new VBoxContainer { Name = "Workbench" };
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);

        var tools = new HBoxContainer { Name = "Tools" };
        root.AddChild(tools);
        AddToolOptions(tools);
        AddButton(tools, "DriveInput", "Drive", DriveSelectedInput);
        AddButton(tools, "Commit", "Commit", CommitStaged);
        _runButton.Name = "RunPause";
        _runButton.Pressed += ToggleRun;
        tools.AddChild(_runButton);
        AddButton(tools, "Step", "Step", StepMicrotick);

        var timing = new HBoxContainer { Name = "Timing" };
        root.AddChild(timing);
        timing.AddChild(new Label { Text = "Cycle ticks" });
        _cycleTicks.Name = "CycleTicks";
        _cycleTicks.MinValue = 1;
        _cycleTicks.MaxValue = 64;
        _cycleTicks.Step = 1;
        _cycleTicks.Value = _session.CycleTicks;
        _cycleTicks.ValueChanged += value => _session.CycleTicks = (int)value;
        timing.AddChild(_cycleTicks);
        AddButton(timing, "StepCycle", "Step cycle", StepCycle);
        timing.AddChild(new Label { Text = "Cable ticks" });
        _cableLatency.Name = "CableLatency";
        _cableLatency.MinValue = 1;
        _cableLatency.MaxValue = 64;
        _cableLatency.Step = 1;
        _cableLatency.Value = 2;
        timing.AddChild(_cableLatency);

        var assets = new HBoxContainer { Name = "Assets" };
        root.AddChild(assets);
        AddButton(assets, "PackageChip", "Package panel", PackagePanel);
        AddButton(assets, "PlaceChip", "Place chip", PlaceChip);
        AddButton(assets, "AddPanelDevice", "Add panel device", AddPanelDevice);
        AddButton(assets, "AddTimedDevice", "Add timed device", AddTimedDevice);
        AddButton(assets, "ConnectDevices", "Connect devices", ConnectDevices);
        AddButton(assets, "XorPractice", "XOR practice", RunXorPractice);
        AddButton(assets, "TamperBypassPractice", "Tamper bypass", RunTamperBypassPractice);

        var scroll = new ScrollContainer { Name = "PanelScroll" };
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        root.AddChild(scroll);
        scroll.AddChild(_canvas);

        _status.Name = "Status";
        root.AddChild(_status);
        _topology.Name = "Topology";
        root.AddChild(_topology);
        _deviceStatus.Name = "DeviceGraphStatus";
        root.AddChild(_deviceStatus);
        var probes = new HBoxContainer { Name = "Probes" };
        probes.AddChild(new Label { Text = "Probe" });
        _probePicker.Name = "ProbePicker";
        _probePicker.ItemSelected += OnProbeSelected;
        probes.AddChild(_probePicker);
        root.AddChild(probes);
        _waveform.Name = "Waveform";
        root.AddChild(_waveform);
        _waveformValues.Name = "WaveformValues";
        root.AddChild(_waveformValues);
        _practiceStatus.Name = "PracticeStatus";
        root.AddChild(_practiceStatus);
        root.AddChild(new Label { Text = "Accepted command log" });
        _commandLog.Name = "CommandLog";
        _commandLog.CustomMinimumSize = new Vector2(0, 72);
        _commandLog.ScrollActive = false;
        root.AddChild(_commandLog);
    }

    private void AddToolOptions(HBoxContainer parent)
    {
        _toolPicker.Name = "ToolPicker";
        AddOption(_toolPicker, Tool.Select);
        AddOption(_toolPicker, Tool.Paint);
        AddOption(_toolPicker, Tool.Erase);
        AddOption(_toolPicker, Tool.Rotate);
        _toolPicker.Select(0);
        parent.AddChild(_toolPicker);

        _cellPicker.Name = "CellPicker";
        foreach (var kind in PaintableKinds)
        {
            _cellPicker.AddItem(kind.ToString(), (int)kind);
        }

        _cellPicker.Select(0);
        parent.AddChild(_cellPicker);

        _directionPicker.Name = "DirectionPicker";
        foreach (var direction in Enum.GetValues<CardinalDirection>())
        {
            _directionPicker.AddItem(direction.ToString(), (int)direction);
        }

        _directionPicker.Select((int)CardinalDirection.East);
        parent.AddChild(_directionPicker);

        _valuePicker.Name = "InputValuePicker";
        for (var index = 0; index < LogicValues.Length; index++)
        {
            _valuePicker.AddItem(LogicValues[index].ToString(), index);
        }

        _valuePicker.Select(Array.IndexOf(LogicValues, LogicValue.Low));
        parent.AddChild(_valuePicker);
    }

    private static void AddOption(OptionButton picker, Tool tool) =>
        picker.AddItem(tool.ToString(), (int)tool);

    private static void AddButton(HBoxContainer parent, string name, string text, Action action)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += action;
        parent.AddChild(button);
    }

    private void OnCellPressed(GridCoordinate location)
    {
        WorkbenchDiagnostic? diagnostic = null;
        var success = (Tool)_toolPicker.GetSelectedId() switch
        {
            Tool.Select => _session.TrySelect(location, out diagnostic),
            Tool.Paint => Paint(location, out diagnostic),
            Tool.Erase => _session.TryErase(location, out diagnostic),
            Tool.Rotate => _session.TryRotate(location, out diagnostic),
            _ => false
        };

        if (!success && diagnostic is null)
        {
            _status.Text = "Select, paint, erase, or rotate a panel cell.";
        }
        else
        {
            ShowDiagnostic(diagnostic);
        }
    }

    private bool Paint(GridCoordinate location, out WorkbenchDiagnostic? diagnostic) => _session.TryPaint(
        location,
        (CellKind)_cellPicker.GetSelectedId(),
        (CardinalDirection)_directionPicker.GetSelectedId(),
        out diagnostic);

    private void DriveSelectedInput()
    {
        if (_session.Selection is not { } location || _session.GetCell(location) is not
            { Kind: CellKind.InputPort, PortId: { } portId })
        {
            _status.Text = "Select an input port before driving it.";
            return;
        }

        var valueIndex = (int)_valuePicker.GetSelectedId();
        var ok = _session.TryDriveInput(portId, LogicValues[valueIndex], out var diagnostic);
        ShowDiagnostic(diagnostic);
        if (!ok)
        {
            return;
        }

        Refresh();
    }

    private void ToggleRun()
    {
        if (_session.IsPaused)
        {
            _session.SetPaused(false);
            _runTimer.Start();
        }
        else
        {
            Pause();
        }

        Refresh();
    }

    private void CommitStaged()
    {
        _session.CommitStaged(out var diagnostic);
        ShowDiagnostic(diagnostic);
        Refresh();
    }

    private void StepMicrotick()
    {
        Pause();
        _session.StepMicrotick();
        Refresh();
    }

    private void StepCycle()
    {
        Pause();
        _session.StepConfiguredCycles();
        Refresh();
    }

    private void Pause()
    {
        _runTimer.Stop();
        _session.SetPaused(true);
    }

    private void OnRunTimeout()
    {
        if (_session.IsPaused)
        {
            _runTimer.Stop();
            return;
        }

        _session.StepMicrotick(commitStaged: false);
    }

    private void PackagePanel()
    {
        var id = new DefinitionId("chip/workbench-panel");
        _session.TryPackagePanelAsChip(id, "panel", out _, out var diagnostic);
        ShowDiagnostic(diagnostic);
        Refresh();
    }

    private void PlaceChip()
    {
        if (_session.Selection is not { } location)
        {
            _status.Text = "Select an empty cell for the chip.";
            return;
        }

        var chip = _session.Definition.ChipCatalog.Definitions.LastOrDefault();
        if (chip is null)
        {
            _status.Text = "Package the panel before placing a chip.";
            return;
        }

        var id = new ComponentId($"chip/workbench-instance-{++_chipOrdinal}");
        var ok = _session.TryPlaceChip(chip, id, location, out var diagnostic);
        ShowDiagnostic(diagnostic);
        if (ok)
        {
            Refresh();
        }
    }

    private void AddPanelDevice()
    {
        if (_session.Selection is not { } location)
        {
            _status.Text = "Select a cell for the panel device.";
            return;
        }

        try
        {
            var device = DeviceDefinition.Create(
                new ComponentId($"device/panel-{++_deviceOrdinal}"),
                PanelDeviceBackendDefinition.Create(_session.Definition.Panel));
            var ok = _session.TryPlaceDevice(device, location, out var diagnostic);
            ShowDiagnostic(diagnostic);
            if (ok)
            {
                Refresh();
            }
        }
        catch (ArgumentException exception)
        {
            _status.Text = exception.Message;
        }
    }

    private void AddTimedDevice()
    {
        if (_session.Selection is not { } location)
        {
            _status.Text = "Select a cell for the timed device.";
            return;
        }

        var backend = TimedDeviceBackendDefinition.Create(
            [
                DevicePortDefinition.Create("in", DevicePortDirection.Input),
                DevicePortDefinition.Create("out", DevicePortDirection.Output)
            ],
            [new TimedOutputChange(1, "out", DeviceSignal.Scalar(LogicValue.Low))]);
        var device = DeviceDefinition.Create(
            new ComponentId($"device/timed-{++_deviceOrdinal}"),
            backend);
        var ok = _session.TryPlaceDevice(device, location, out var diagnostic);
        ShowDiagnostic(diagnostic);
        if (ok)
        {
            Refresh();
        }
    }

    private void ConnectDevices()
    {
        var devices = _session.Definition.DeviceGraph?.Devices ?? [];
        var source = devices.FirstOrDefault(device =>
            device.Ports.Any(port => port.Direction == DevicePortDirection.Output));
        var target = devices.FirstOrDefault(device => device.Id != source?.Id &&
            device.Ports.Any(port => port.Direction == DevicePortDirection.Input));
        if (source is null || target is null)
        {
            _status.Text = "Add a device with an output and a different device with an input first.";
            return;
        }

        var sourcePort = source.Ports.First(port => port.Direction == DevicePortDirection.Output);
        var targetPort = target.Ports.First(port => port.Direction == DevicePortDirection.Input);
        var laneId = new ComponentId($"lane/workbench-{++_laneOrdinal}");
        var lane = CableLaneDefinition.Create(
            laneId,
            DevicePortEndpoint.Create(source.Id, sourcePort.Name),
            DevicePortEndpoint.Create(target.Id, targetPort.Name),
            (int)_cableLatency.Value);
        var bundle = CableBundleDefinition.Create(
            new ComponentId($"bundle/workbench-{_laneOrdinal}"),
            [laneId]);
        var ok = _session.TryAddCableBundle(bundle, [lane], out var diagnostic);
        ShowDiagnostic(diagnostic);
        if (ok)
        {
            Refresh();
        }
    }

    private void RunXorPractice()
    {
        Pause();
        try
        {
            var result = WorkbenchPractice.Run(
                ReadXorPracticeFixture());
            _session.Changed -= Refresh;
            _session = result.Session;
            _session.Changed += Refresh;
            _canvas.Bind(_session);
            _practiceRawWaveform = result.XorWaveform.ToArray();
            _activeProbeId = "xor-raw-probe";
            _practiceStatus.Text = result.Succeeded && !result.CableHistory.IsDefaultOrEmpty
                ? $"XOR output: t34={result.XorOutputTrace[34]} | cable delivered " +
                  $"{result.CableHistory[^1].Signal} at t{result.CableHistory[^1].DeliveredTick}"
                : "workbench.practice.trace-mismatch: Practice trace did not match its fixtures.";
            Refresh();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            _practiceStatus.Text = $"workbench.practice.failed: {exception.Message}";
        }
    }

    private void RunTamperBypassPractice()
    {
        Pause();
        try
        {
            var result = TamperDetectionPractice.Run();
            LastTamperPracticeResult = result;
            _practiceStatus.Text = result.Succeeded
                ? $"tamper-bypass: disconnected original, inserted responder, " +
                  $"accepted response at t{result.Trace.First(item => item.ResponseAccepted).Tick}, no alarm"
                : "tamper-bypass.failed: protocol or replay trace did not match.";
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            _practiceStatus.Text = $"tamper-bypass.failed: {exception.Message}";
        }
    }

    private static byte[] ReadXorPracticeFixture()
    {
        const string resourcePath = "res://examples/stage-04/xor-glitch.fixture.json";
        if (Godot.FileAccess.FileExists(resourcePath))
        {
            return Godot.FileAccess.GetFileAsBytes(resourcePath);
        }

        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "xor-glitch.fixture.json");
        if (File.Exists(fixturePath))
        {
            return File.ReadAllBytes(fixturePath);
        }

        throw new InvalidOperationException("workbench.practice.fixture-missing: XOR fixture is unavailable.");
    }

    private void OnProbeSelected(long index)
    {
        if (_refreshingProbePicker || index < 0 || index >= _probePicker.ItemCount)
        {
            return;
        }

        _activeProbeId = _probePicker.GetItemText((int)index);
        Refresh();
    }

    private void Refresh()
    {
        _runButton.Text = _session.IsPaused ? "Run" : "Pause";
        _status.Text = _session.LastDiagnostic is { } diagnostic
            ? $"{diagnostic.Code}: {diagnostic.Message}"
            : $"Tick {_session.CurrentTick} | {_session.PendingCommandCount} pending | " +
              $"{_session.StagedEditCount} staged";
        var definition = _session.Definition;
        var graph = definition.DeviceGraph;
        _topology.Text = graph is null
            ? $"Chips: {definition.ChipPlacements.Length} | Devices: 0 | Cable bundles: 0"
            : $"Chips: {definition.ChipPlacements.Length} | Devices: {graph.Devices.Length} | " +
              $"Cable bundles: {graph.Bundles.Length}";
        var committed = _session.CommittedDefinition;
        var chipOutputs = committed.ChipPlacements.SelectMany(placement =>
        {
            var chip = committed.ChipCatalog.Resolve(placement.DefinitionId, placement.ContentHash);
            return chip.Ports
                .Where(port => port.Direction == ChipPortDirection.Output)
                .Select(port => $"{placement.InstanceId}.{port.Name}={_session.GetChipOutput(placement.InstanceId, port.Name)}");
        });
        var deviceOutputs = committed.DeviceGraph?.Devices.SelectMany(device => device.Ports
                    .Where(port => port.Direction == DevicePortDirection.Output)
                    .Select(port =>
                        $"{device.Id}.{port.Name}={_session.GetDeviceOutput(device.Id, port.Name)}")) ?? [];
        var lanes = committed.DeviceGraph?.Lanes.Select(lane =>
        {
            var last = _session.GetCableLaneHistory(lane.Id).LastOrDefault();
            return last is null
                ? $"{lane.Id}=no transition"
                : $"{lane.Id}={last.Signal}@{last.ScheduledTick} ({last.Status})";
        }) ?? [];
        var runtimeState = chipOutputs.Concat(deviceOutputs).Concat(lanes).ToArray();
        _deviceStatus.Text = runtimeState.Length == 0
            ? string.Empty
            : $"Runtime tick {_session.CurrentTick} | {string.Join(" | ", runtimeState)}";

        var probes = definition.Panel.Cells.OfType<PanelCellDefinition>()
            .Where(cell => cell.Kind == CellKind.Probe)
            .Select(cell => cell.Id.Value)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (_practiceRawWaveform is not null && !probes.Contains("xor-raw-probe", StringComparer.Ordinal))
        {
            probes = probes.Append("xor-raw-probe").OrderBy(id => id, StringComparer.Ordinal).ToArray();
        }

        _refreshingProbePicker = true;
        _probePicker.Clear();
        foreach (var probe in probes)
        {
            _probePicker.AddItem(probe);
        }

        var activeIndex = Array.IndexOf(probes, _activeProbeId);
        if (activeIndex < 0 && probes.Length > 0)
        {
            activeIndex = 0;
            _activeProbeId = probes[0];
        }

        if (activeIndex >= 0)
        {
            _probePicker.Select(activeIndex);
        }

        _refreshingProbePicker = false;
        IEnumerable<ProbeSample> samples = [];
        if (_activeProbeId == "xor-raw-probe" && _practiceRawWaveform is not null)
        {
            samples = _practiceRawWaveform;
        }
        else if (_activeProbeId is not null)
        {
            samples = _session.Waveform(new ComponentId(_activeProbeId));
        }

        _waveform.Present(samples);
        _waveformValues.Text = string.Join("   ", samples.TakeLast(8)
            .Select(sample => $"{sample.Tick}:{sample.Value}"));
        _commandLog.Text = string.Join("\n", _session.CommandLog.TakeLast(8).Select(command =>
            $"#{command.AcceptedOrdinal} @ {command.ApplyAtTick}: {command.Action} ({command.Status})"));
    }

    private void ShowDiagnostic(WorkbenchDiagnostic? diagnostic)
    {
        _status.Text = diagnostic is null
            ? $"Tick {_session.CurrentTick} | {_session.PendingCommandCount} pending | " +
              $"{_session.StagedEditCount} staged"
            : $"{diagnostic.Code}: {diagnostic.Message}";
    }
}
