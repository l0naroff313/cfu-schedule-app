using Microsoft.Maui.Graphics;

namespace UniversitySchedule.Mobile.Controls;

public sealed class CompletionRingDrawable : IDrawable
{
    private const float StrokeWidth = 4f;

    public CompletionRingDrawable(double progress, Color trackColor, Color progressColor)
    {
        Progress = Math.Clamp(progress, 0d, 1d);
        TrackColor = trackColor;
        ProgressColor = progressColor;
    }

    public double Progress { get; }

    public Color TrackColor { get; }

    public Color ProgressColor { get; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float inset = StrokeWidth / 2f;
        var ringBounds = new RectF(
            dirtyRect.Left + inset,
            dirtyRect.Top + inset,
            Math.Max(0, dirtyRect.Width - StrokeWidth),
            Math.Max(0, dirtyRect.Height - StrokeWidth));

        canvas.StrokeSize = StrokeWidth;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeColor = TrackColor;
        canvas.DrawEllipse(ringBounds);

        if (Progress <= 0d)
        {
            return;
        }

        canvas.StrokeColor = ProgressColor;
        canvas.DrawArc(
            ringBounds.Left,
            ringBounds.Top,
            ringBounds.Right,
            ringBounds.Bottom,
            90f,
            90f - (float)(Progress * 360d),
            false,
            false);
    }
}
