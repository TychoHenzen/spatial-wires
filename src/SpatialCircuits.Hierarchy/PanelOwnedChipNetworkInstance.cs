using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;

namespace SpatialCircuits.Hierarchy;

public sealed record ChipInstanceTickResult(
    ComponentId InstanceId,
    DefinitionId DefinitionId,
    string ContentHash,
    string Hash,
    ImmutableSortedDictionary<string, string> Outputs,
    ImmutableArray<ChipInstanceTickResult> Children);

public sealed record ChipNetworkTickResult(
    long Tick,
    string Hash,
    PanelTickResult OwnerPanel,
    ImmutableArray<ChipInstanceTickResult> Chips);

/// <summary>Runs one panel and its panel-owned child chips on independent hidden source grids.</summary>
public sealed class PanelOwnedChipNetworkInstance
{
    private readonly PanelDefinition _ownerPanel;
    private readonly PanelRuntimeInstance _ownerRuntime;
    private readonly Dictionary<string, ChipInstance> _instances;
    private PanelOwnedChipNetworkDefinition _definition;
    private bool _isStepping;

    public PanelOwnedChipNetworkInstance(
        PanelDefinition ownerPanel,
        PanelOwnedChipNetworkDefinition definition,
        ChipDefinitionCatalog catalog,
        CustomCellRuleRegistry? customCellRules = null)
        : this(ownerPanel, new PanelRuntimeInstance(ownerPanel, customCellRules), definition, catalog, customCellRules, isRoot: true)
    {
    }

    internal PanelOwnedChipNetworkInstance(
        PanelDefinition ownerPanel,
        PanelRuntimeInstance ownerRuntime,
        PanelOwnedChipNetworkDefinition definition,
        ChipDefinitionCatalog catalog,
        CustomCellRuleRegistry? customCellRules,
        bool isRoot)
    {
        ArgumentNullException.ThrowIfNull(ownerPanel);
        ArgumentNullException.ThrowIfNull(ownerRuntime);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(catalog);
        _ownerPanel = ownerPanel;
        _ownerRuntime = ownerRuntime;
        _definition = definition;
        IsRoot = isRoot;
        var diagnostics = definition.Validate(ownerPanel, catalog);
        if (diagnostics.Length > 0)
        {
            throw new ChipDefinitionException(diagnostics);
        }

        _instances = new Dictionary<string, ChipInstance>(StringComparer.Ordinal);
        foreach (var child in definition.Instances)
        {
            var chipDefinition = catalog.Resolve(child.DefinitionId, child.ContentHash);
            _instances.Add(
                child.InstanceId.Value,
                new ChipInstance(child.InstanceId, chipDefinition, this, catalog, customCellRules));
        }
    }

    public PanelOwnedChipNetworkDefinition Definition => _definition;

    public CircuitId OwnerPanelId => _ownerPanel.Id;

    internal bool IsRoot { get; }

    internal bool IsStepping => _isStepping;

    public IReadOnlyCollection<ChipInstance> Instances => _instances.Values
        .OrderBy(instance => instance.InstanceId.Value, StringComparer.Ordinal)
        .ToArray();

    public ChipInstance GetInstance(ComponentId instanceId) =>
        _instances.TryGetValue(instanceId.Value, out var instance)
            ? instance
            : throw new KeyNotFoundException($"Chip instance '{instanceId}' is not defined in this panel network.");

    public void SetInput(PortId portId, LogicValue value)
    {
        EnsureNotStepping();
        if (_definition.Connections.Any(connection =>
                connection.Target.IsParentPanel &&
                string.Equals(connection.Target.PortName, portId.Value, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Panel input '{portId}' is driven by a child-network connection.");
        }

        _ownerRuntime.SetInput(portId, value);
    }

    /// <summary>Propagates named connections from the prior tick, then steps every hidden source grid once.</summary>
    public ChipNetworkTickResult Step()
    {
        if (!IsRoot)
        {
            throw new InvalidOperationException("Only the root chip network can advance simulation time.");
        }

        EnsureNotStepping();
        SetTreeStepping(true);
        try
        {
            ApplyConnectionsToTree();
            var ownerResult = _ownerRuntime.Step();
            var chips = StepChildrenAfterInputs();
            return new ChipNetworkTickResult(
                ownerResult.Tick,
                ChipData.HashRuntime(
                    "spatial-circuits-panel-child-network-v1",
                    ownerResult.Hash,
                    chips.Select(chip => (chip.InstanceId.Value, chip.Hash))),
                ownerResult,
                chips);
        }
        finally
        {
            SetTreeStepping(false);
        }
    }

    internal bool HasIncomingConnection(ChipPortEndpoint endpoint) =>
        _definition.Connections.Any(connection => connection.Target == endpoint);

    internal void ReplaceDefinitionPin(
        ComponentId instanceId,
        ChipDefinition oldDefinition,
        ChipDefinition newDefinition,
        ChipDefinitionCatalog catalog)
    {
        EnsureNotStepping();
        var updatedInstances = _definition.Instances
            .Select(instance => instance.InstanceId == instanceId
                ? ChipInstanceDefinition.Create(instanceId, newDefinition.Id, newDefinition.ContentHash)
                : instance)
            .ToArray();
        var updated = PanelOwnedChipNetworkDefinition.Create(
            _definition.OwnerPanelId,
            updatedInstances,
            _definition.Connections);
        var diagnostics = updated.Validate(_ownerPanel, catalog);
        if (diagnostics.Length > 0)
        {
            throw new ChipDefinitionException(diagnostics);
        }

        if (!_instances.TryGetValue(instanceId.Value, out var current) ||
            current.Definition.ContentHash != oldDefinition.ContentHash)
        {
            throw new InvalidOperationException("Chip instance changed while its upgrade was being prepared.");
        }

        _definition = updated;
    }

    internal bool TryRestoreChildrenFrom(
        PanelOwnedChipNetworkInstance source,
        out Diagnostic? diagnostic)
    {
        var sourceIds = source._instances.Keys.Order(StringComparer.Ordinal).ToArray();
        var candidateIds = _instances.Keys.Order(StringComparer.Ordinal).ToArray();
        if (!sourceIds.SequenceEqual(candidateIds, StringComparer.Ordinal))
        {
            diagnostic = ChipData.Error(
                ChipDiagnosticCodes.UpgradeIncompatible,
                "chip.childNetwork.instances",
                "Upgrade changes the child instance set, so runtime state cannot be preserved.");
            return false;
        }

        foreach (var id in candidateIds)
        {
            if (!_instances[id].TryRestoreStateFrom(source._instances[id], out diagnostic))
            {
                return false;
            }
        }

        diagnostic = null;
        return true;
    }

    private void ApplyConnectionsToTree()
    {
        foreach (var connection in _definition.Connections)
        {
            var value = ReadOutput(connection.Source);
            DriveInput(connection.Target, value);
        }

        foreach (var child in OrderedInstances())
        {
            child.Children.ApplyConnectionsToTree();
        }
    }

    private ImmutableArray<ChipInstanceTickResult> StepChildrenAfterInputs() =>
        OrderedInstances().Select(instance => instance.StepAfterInputs()).ToImmutableArray();

    private ImmutableArray<ChipInstanceTickResult> StepNestedChildrenAfterInputs() => StepChildrenAfterInputs();

    private LogicValue ReadOutput(ChipPortEndpoint endpoint) => endpoint.InstanceId is { } instanceId
        ? GetInstance(instanceId).GetOutput(endpoint.PortName)
        : _ownerRuntime.GetOutput(new PortId(endpoint.PortName));

    private void DriveInput(ChipPortEndpoint endpoint, LogicValue value)
    {
        if (endpoint.InstanceId is { } instanceId)
        {
            GetInstance(instanceId).DriveConnectedInput(endpoint.PortName, value);
        }
        else
        {
            _ownerRuntime.SetInput(new PortId(endpoint.PortName), value);
        }
    }

    private IEnumerable<ChipInstance> OrderedInstances() =>
        _instances.Values.OrderBy(instance => instance.InstanceId.Value, StringComparer.Ordinal);

    private void SetTreeStepping(bool isStepping)
    {
        _isStepping = isStepping;
        foreach (var instance in _instances.Values)
        {
            instance.Children.SetTreeStepping(isStepping);
        }
    }

    private void EnsureNotStepping()
    {
        if (_isStepping)
        {
            throw new InvalidOperationException("A panel-owned chip network cannot be changed while it is stepping.");
        }
    }

    public sealed class ChipInstance
    {
        private readonly PanelOwnedChipNetworkInstance _parentNetwork;
        private readonly CustomCellRuleRegistry? _customCellRules;
        private ChipDefinition _definition;
        private PanelRuntimeInstance _runtime;
        private PanelOwnedChipNetworkInstance _children;
        private ChipPresentationMode _presentation = ChipPresentationMode.ExpandedGrid;

        internal ChipInstance(
            ComponentId instanceId,
            ChipDefinition definition,
            PanelOwnedChipNetworkInstance parentNetwork,
            ChipDefinitionCatalog catalog,
            CustomCellRuleRegistry? customCellRules)
        {
            InstanceId = instanceId;
            _definition = definition;
            _parentNetwork = parentNetwork;
            _customCellRules = customCellRules;
            _runtime = new PanelRuntimeInstance(definition.SourcePanel, customCellRules);
            _children = new PanelOwnedChipNetworkInstance(
                definition.SourcePanel,
                _runtime,
                definition.ChildNetwork,
                catalog,
                customCellRules,
                isRoot: false);
        }

        public ComponentId InstanceId { get; }

        public ChipDefinition Definition => _definition;

        public PanelOwnedChipNetworkInstance Children => _children;

        public ChipPresentationMode Presentation => _presentation;

        public void Expand() => _presentation = ChipPresentationMode.ExpandedGrid;

        public void Collapse() => _presentation = ChipPresentationMode.Collapsed;

        /// <summary>Disabling authoring always exposes the source grid as the readable fallback.</summary>
        public ChipPresentationMode ResolvePresentation(bool authoringEnabled) =>
            authoringEnabled ? _presentation : ChipPresentationMode.ExpandedGrid;

        public void SetInput(string portName, LogicValue value)
        {
            _parentNetwork.EnsureNotStepping();
            if (_parentNetwork.HasIncomingConnection(ChipPortEndpoint.ChildChip(InstanceId, portName)))
            {
                throw new InvalidOperationException($"Chip input '{portName}' is driven by a child-network connection.");
            }

            DriveConnectedInput(portName, value);
        }

        public LogicValue GetOutput(string portName)
        {
            var port = FindPort(portName, ChipPortDirection.Output);
            return _runtime.GetOutput(port.PanelPortId);
        }

        public ImmutableSortedDictionary<string, string> CaptureOutputs()
        {
            var outputs = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (var port in _definition.Ports.Where(port => port.Direction == ChipPortDirection.Output))
            {
                outputs.Add(port.Name, _runtime.GetOutput(port.PanelPortId).ToString());
            }

            return outputs.ToImmutable();
        }

        /// <summary>Admits a compatible pinned definition only after a candidate runtime restores all state.</summary>
        public bool TryUpgrade(
            ChipDefinition candidate,
            ChipDefinitionCatalog catalog,
            out Diagnostic? diagnostic)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            ArgumentNullException.ThrowIfNull(catalog);
            if (!_parentNetwork.IsRoot)
            {
                diagnostic = ChipData.Error(
                    ChipDiagnosticCodes.UpgradeCandidateInvalid,
                    $"network.instances/{InstanceId.Value}",
                    "Upgrade a nested chip by upgrading its owning chip definition.");
                return false;
            }

            if (_parentNetwork.IsStepping)
            {
                diagnostic = ChipData.Error(
                    ChipDiagnosticCodes.UpgradeCandidateInvalid,
                    $"network.instances/{InstanceId.Value}",
                    "A chip cannot be upgraded while its parent network is stepping.");
                return false;
            }

            if (candidate.Id != _definition.Id ||
                !catalog.TryResolve(candidate.Id, candidate.ContentHash, out var admitted) ||
                admitted is null)
            {
                diagnostic = ChipData.Error(
                    ChipDiagnosticCodes.UpgradeCandidateInvalid,
                    $"network.instances/{InstanceId.Value}",
                    "Upgrade candidate is not the validated definition pinned by its catalog.");
                return false;
            }

            ChipInstance replacement;
            try
            {
                replacement = new ChipInstance(
                    InstanceId,
                    admitted,
                    _parentNetwork,
                    catalog,
                    _customCellRules);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                diagnostic = ChipData.Error(
                    ChipDiagnosticCodes.UpgradeCandidateInvalid,
                    $"network.instances/{InstanceId.Value}",
                    $"Upgrade candidate could not be constructed: {exception.Message}");
                return false;
            }

            if (!replacement.TryRestoreStateFrom(this, out diagnostic))
            {
                return false;
            }

            try
            {
                _parentNetwork.ReplaceDefinitionPin(InstanceId, _definition, admitted, catalog);
            }
            catch (ChipDefinitionException exception)
            {
                diagnostic = exception.Diagnostics[0];
                return false;
            }

            _definition = replacement._definition;
            _runtime = replacement._runtime;
            _children = replacement._children;
            _presentation = replacement._presentation;
            diagnostic = null;
            return true;
        }

        internal LogicValue GetConnectedOutput(string portName) => GetOutput(portName);

        internal void DriveConnectedInput(string portName, LogicValue value)
        {
            var port = FindPort(portName, ChipPortDirection.Input);
            _runtime.SetInput(port.PanelPortId, value);
        }

        internal ChipInstanceTickResult StepAfterInputs()
        {
            var panelResult = _runtime.Step();
            var children = _children.StepNestedChildrenAfterInputs();
            var outputs = CaptureOutputs();
            return new ChipInstanceTickResult(
                InstanceId,
                _definition.Id,
                _definition.ContentHash,
                ChipData.HashRuntime(
                    "spatial-circuits-chip-instance-v1",
                    $"{_definition.ContentHash}:{panelResult.Hash}",
                    children.Select(child => (child.InstanceId.Value, child.Hash))),
                outputs,
                children);
        }

        internal bool TryRestoreStateFrom(ChipInstance source, out Diagnostic? diagnostic)
        {
            try
            {
                _runtime.RestoreSnapshot(source._runtime.CaptureSnapshot());
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                diagnostic = ChipData.Error(
                    ChipDiagnosticCodes.UpgradeIncompatible,
                    $"network.instances/{InstanceId.Value}/sourcePanel",
                    $"Candidate source panel rejected runtime state: {exception.Message}");
                return false;
            }

            if (!_children.TryRestoreChildrenFrom(source._children, out diagnostic))
            {
                return false;
            }

            _presentation = source._presentation;
            diagnostic = null;
            return true;
        }

        private ChipPortDefinition FindPort(string portName, ChipPortDirection direction)
        {
            if (!ChipData.IsPortName(portName))
            {
                throw new ArgumentException("Chip port name is invalid.", nameof(portName));
            }

            return _definition.Ports.FirstOrDefault(port =>
                    string.Equals(port.Name, portName, StringComparison.Ordinal) && port.Direction == direction)
                ?? throw new KeyNotFoundException($"Chip {direction.ToString().ToLowerInvariant()} port '{portName}' is not defined.");
        }
    }
}
