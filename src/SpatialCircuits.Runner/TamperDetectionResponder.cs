using System.Collections.Immutable;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;
using SpatialCircuits.Workbench;

namespace SpatialCircuits.Runner;

public static class TamperDetectionResponder
{
    public static PanelDefinition CreatePanelDefinition(
        string panelId = "panel/tamper-responder")
    {
        var cells = new List<PanelCellDefinition>();
        for (var outputBit = 0; outputBit < 8; outputBit++)
        {
            var sourceBit = (outputBit + 7) % 8;
            var challengePort = new PortId($"challenge-{sourceBit}");
            var responsePort = new PortId($"response-{outputBit}");
            var y = outputBit * 3 + 1;
            if ((TamperDetectionProtocol.ResponseConstant & (1 << outputBit)) == 0)
            {
                cells.Add(Cell($"input-{outputBit}", 0, y, CellKind.InputPort, CardinalDirection.East, challengePort));
                cells.Add(Cell($"wire-{outputBit}", 1, y, CellKind.Wire, CardinalDirection.East));
            }
            else
            {
                cells.Add(Cell($"input-{outputBit}-a", 1, y - 1, CellKind.InputPort, CardinalDirection.South, challengePort));
                cells.Add(Cell($"input-{outputBit}-b", 1, y + 1, CellKind.InputPort, CardinalDirection.North, challengePort));
                cells.Add(Cell(
                    $"invert-{outputBit}",
                    1,
                    y,
                    CellKind.Nand,
                    CardinalDirection.East,
                    parameters: [new KeyValuePair<string, string>("delay", "1")]));
            }

            cells.Add(Cell($"output-{outputBit}", 2, y, CellKind.OutputPort, CardinalDirection.East, responsePort));
        }

        return PanelDefinition.Create(new CircuitId(panelId), 3, 24, cells);
    }

    public static byte ComputeResponse(byte challenge) =>
        ComputeResponse(CreatePanelDefinition(), challenge);

    public static byte ComputeResponse(PanelDefinition panel, byte challenge)
    {
        ArgumentNullException.ThrowIfNull(panel);
        var runtime = new PanelRuntimeInstance(panel);
        for (var bit = 0; bit < 8; bit++)
        {
            runtime.SetInput(
                new PortId($"challenge-{bit}"),
                (challenge & (1 << bit)) == 0 ? LogicValue.Low : LogicValue.High);
        }

        for (var tick = 0; tick < TamperDetectionProtocol.ResponderComputeDelay; tick++)
        {
            runtime.Step();
        }

        byte response = 0;
        for (var bit = 0; bit < 8; bit++)
        {
            if (runtime.GetOutput(new PortId($"response-{bit}")) == LogicValue.High)
            {
                response |= (byte)(1 << bit);
            }
        }

        return response;
    }

    public static PanelWorkbenchSession BuildThroughPublicEditing()
    {
        var panel = CreatePanelDefinition("panel/player-built-responder");
        var session = new PanelWorkbenchSession(PanelDefinition.Create(
            panel.Id,
            panel.Width,
            panel.Height,
            []));
        foreach (var cell in panel.Cells.OfType<PanelCellDefinition>())
        {
            if (!session.TryPaint(cell, out var diagnostic))
            {
                throw new InvalidOperationException(
                    diagnostic?.Message ?? "Player responder cell could not be painted.");
            }
        }

        if (!session.CommitStaged(out var commitDiagnostic))
        {
            throw new InvalidOperationException(
                commitDiagnostic?.Message ?? "Player responder could not be committed.");
        }

        return session;
    }

    private static PanelCellDefinition Cell(
        string id,
        int x,
        int y,
        CellKind kind,
        CardinalDirection orientation,
        PortId? portId = null,
        IEnumerable<KeyValuePair<string, string>>? parameters = null) =>
        PanelCellDefinition.Create(
            new ComponentId(id),
            new GridCoordinate(x, y),
            kind,
            orientation,
            portId,
            parameters);
}
