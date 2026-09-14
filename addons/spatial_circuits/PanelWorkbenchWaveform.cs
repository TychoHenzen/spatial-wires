using System.Collections.Immutable;
using Godot;
using SpatialCircuits.Cells;
using SpatialCircuits.Core;

namespace SpatialCircuits.GodotAdapter;

[Tool]
public partial class PanelWorkbenchWaveform : Control
{
    private ImmutableArray<ProbeSample> _samples = [];

    public PanelWorkbenchWaveform()
    {
        CustomMinimumSize = new Vector2(180, 96);
    }

    public ImmutableArray<ProbeSample> Samples => _samples;

    public void Present(IEnumerable<ProbeSample> samples)
    {
        _samples = samples.OrderBy(sample => sample.Tick).ToImmutableArray();
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.075f, 0.082f, 0.1f));
        var chart = new Rect2(8, 8, Mathf.Max(0, Size.X - 16), Mathf.Max(0, Size.Y - 16));
        for (var level = 0; level < 4; level++)
        {
            var y = chart.Position.Y + chart.Size.Y * level / 3;
            DrawLine(new Vector2(chart.Position.X, y), new Vector2(chart.End.X, y),
                new Color(0.23f, 0.25f, 0.29f));
        }

        if (_samples.IsDefaultOrEmpty)
        {
            return;
        }

        var firstTick = _samples[0].Tick;
        var tickSpan = Math.Max(1, _samples[^1].Tick - firstTick);
        Vector2 Point(ProbeSample sample) => new(
            chart.Position.X + (sample.Tick - firstTick) * chart.Size.X / tickSpan,
            chart.Position.Y + Level(sample.Value) * chart.Size.Y);

        for (var index = 0; index < _samples.Length; index++)
        {
            var current = Point(_samples[index]);
            DrawCircle(current, 2.5f, ValueColor(_samples[index].Value));
            if (index == 0)
            {
                continue;
            }

            var previous = Point(_samples[index - 1]);
            DrawLine(previous, new Vector2(current.X, previous.Y), ValueColor(_samples[index - 1].Value), 2);
            DrawLine(new Vector2(current.X, previous.Y), current, ValueColor(_samples[index].Value), 2);
        }
    }

    private static float Level(LogicValue value) => value switch
    {
        LogicValue.High => 0.08f,
        LogicValue.Unknown => 0.38f,
        LogicValue.Low => 0.66f,
        _ => 0.92f
    };

    private static Color ValueColor(LogicValue value) => value switch
    {
        LogicValue.High => new Color(0.3f, 0.88f, 0.45f),
        LogicValue.Low => new Color(0.38f, 0.72f, 1),
        LogicValue.Unknown => new Color(1, 0.55f, 0.38f),
        _ => new Color(0.68f, 0.7f, 0.74f)
    };
}
