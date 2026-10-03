using Microsoft.Maui.Graphics;

namespace SkiTelemetry_Analyst.Controls;

/// <summary>
/// Half-circle (gauge) progress ring, drawn with MAUI Graphics.
/// Value is 0..100, rendered as a left-to-right arc over the top half.
/// </summary>
public class PerfRingView : GraphicsView
{
    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(double), typeof(PerfRingView), 0.0,
            propertyChanged: (b, _, _) => ((GraphicsView)b).Invalidate());

    public static readonly BindableProperty TrackColorProperty =
        BindableProperty.Create(nameof(TrackColor), typeof(Color), typeof(PerfRingView),
            Color.FromArgb("#33FFFFFF"),
            propertyChanged: (b, _, _) => ((GraphicsView)b).Invalidate());

    public static readonly BindableProperty ValueColorProperty =
        BindableProperty.Create(nameof(ValueColor), typeof(Color), typeof(PerfRingView),
            Colors.White,
            propertyChanged: (b, _, _) => ((GraphicsView)b).Invalidate());

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Color TrackColor
    {
        get => (Color)GetValue(TrackColorProperty)!;
        set => SetValue(TrackColorProperty, value);
    }

    public Color ValueColor
    {
        get => (Color)GetValue(ValueColorProperty)!;
        set => SetValue(ValueColorProperty, value);
    }

    public PerfRingView()
    {
        Drawable = new PerfRingDrawable(this);
    }

    private sealed class PerfRingDrawable(PerfRingView view) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            const float stroke = 9f;
            const float pad = stroke / 2f + 3f;

            float cx = dirtyRect.X + dirtyRect.Width / 2f;
            float cy = dirtyRect.Y + dirtyRect.Height - pad;
            float r = Math.Min(dirtyRect.Width / 2f - pad, dirtyRect.Height - pad - 4f);
            if (r <= 0) return;

            canvas.StrokeSize = stroke;
            canvas.StrokeLineCap = LineCap.Round;

            // Track: full top half (180° -> 360°, y-down so 270° is the top).
            canvas.StrokeColor = view.TrackColor;
            StrokeArc(canvas, cx, cy, r, 180, 360);

            // Value arc, proportional sweep from the left.
            double pct = Math.Clamp(view.Value, 0, 100) / 100.0;
            if (pct > 0)
            {
                canvas.StrokeColor = view.ValueColor;
                StrokeArc(canvas, cx, cy, r, 180, 180 + 180 * (float)pct);
            }
        }

        private static void StrokeArc(ICanvas canvas, float cx, float cy, float r, float fromDeg, float toDeg)
        {
            const float step = 2f;
            float prevX = cx + r * (float)Math.Cos(fromDeg * Math.PI / 180.0);
            float prevY = cy + r * (float)Math.Sin(fromDeg * Math.PI / 180.0);
            for (float a = fromDeg + step; a <= toDeg + 0.01f; a += step)
            {
                float arc = Math.Min(a, toDeg);
                double rad = arc * Math.PI / 180.0;
                float x = cx + r * (float)Math.Cos(rad);
                float y = cy + r * (float)Math.Sin(rad);
                canvas.DrawLine(prevX, prevY, x, y);
                prevX = x;
                prevY = y;
            }
        }
    }
}
