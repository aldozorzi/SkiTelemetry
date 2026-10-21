using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiTelemetry_Core;

namespace SkiTelemetry_Analyst.Views.Runs;

public partial class TurnChartView : ContentView
{
    private List<SkiFrame> _frames = new();
    private TurnMarkers _markers;
    private SensorSide _outerSide = SensorSide.Right;
    private bool _hasData;

    public static readonly BindableProperty ItemProperty = BindableProperty.Create(
        nameof(Item),
        typeof(TurnDetailItem),
        typeof(TurnChartView),
        null,
        propertyChanged: OnItemChanged);

    public TurnDetailItem? Item
    {
        get => (TurnDetailItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public TurnChartView()
    {
        InitializeComponent();
    }

    private static void OnItemChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is TurnChartView view && newValue is TurnDetailItem item)
            view.LoadTurn(item.Frames, item.Turn);
    }

    public void LoadTurn(List<SkiFrame> frames, SkiTurn turn)
    {
        _frames = frames ?? new();
        _hasData = _frames.Count >= 2;
        _markers = _hasData ? SkiTurnAnalyzer.GetMarkers(_frames) : default;
        _outerSide = turn.Foot ?? SkiTurnAnalyzer.DetermineOuterFoot(_frames);
        ChartCanvas.InvalidateSurface();
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e)
    {
        var canvas = e!.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(SKColors.Transparent);

        if (!_hasData) return;

        int n = _frames.Count;
        var outerRoll = new double[n];
        var innerRoll = new double[n];
        var g = new double[n];

        for (int i = 0; i < n; i++)
        {
            SkiFrame frame = _frames[i];
            outerRoll[i] = Math.Abs(_outerSide == SensorSide.Right ? frame.Right.SmoothRoll : frame.Left.SmoothRoll);
            innerRoll[i] = Math.Abs(_outerSide == SensorSide.Right ? frame.Left.SmoothRoll : frame.Right.SmoothRoll);
            g[i] = _outerSide == SensorSide.Right ? frame.Right.AdjustedG : frame.Left.AdjustedG;
        }

        double rollMin = 0;
        double rollMax = Math.Max(Math.Max(outerRoll.Max(), innerRoll.Max()), 10);
        rollMax += Math.Max(rollMax * 0.12, 5);

        double gMin = Math.Min(g.Min(), 1.0);
        double gMax = Math.Max(g.Max(), 1.0);
        double gPad = Math.Max((gMax - gMin) * 0.15, 0.15);
        gMin -= gPad;
        gMax += gPad;

        const float ml = 44f, mr = 44f, mt = 12f, mb = 26f;
        float pw = info.Width - ml - mr;
        float ph = info.Height - mt - mb;
        if (pw <= 0 || ph <= 0) return;

        float scaleX = pw / (n - 1);

        int engagement = Math.Clamp(_markers.EngagementIndex, 0, n - 1);
        int rollPeak = Math.Clamp(_markers.RollPeakIndex, 0, n - 1);
        int gPeak = Math.Clamp(_markers.GPeakIndex, 0, n - 1);

        DrawPhase(canvas, ml, mt, scaleX, ph, 0, engagement, new SKColor(0xFF, 0xFF, 0xFF, 0x18));
        DrawPhase(canvas, ml, mt, scaleX, ph, engagement, rollPeak, new SKColor(0x00, 0xBF, 0xFF, 0x22));
        DrawPhase(canvas, ml, mt, scaleX, ph, rollPeak, gPeak, new SKColor(0xFF, 0xA5, 0x00, 0x22));
        DrawPhase(canvas, ml, mt, scaleX, ph, gPeak, n - 1, new SKColor(0xFF, 0xFF, 0xFF, 0x18));

        using var gridPaint = new SKPaint { Color = SKColor.Parse("#33FFFFFF"), StrokeWidth = 1, IsAntialias = false };
        using var labelPaint = new SKPaint { Color = SKColor.Parse("#B9A3D6"), IsAntialias = true };
        using var rollLabelPaint = new SKPaint { Color = SKColor.Parse("#00BFFF"), IsAntialias = true };
        using var gLabelPaint = new SKPaint { Color = SKColor.Parse("#FFA500"), IsAntialias = true };
        var font = new SKFont();

        const int hLines = 4;
        for (int i = 0; i <= hLines; i++)
        {
            float y = mt + ph * i / hLines;
            canvas.DrawLine(ml, y, ml + pw, y, gridPaint);

            double rollValue = rollMax - (rollMax - rollMin) * i / hLines;
            canvas.DrawText(rollValue.ToString("F0"), 4, y + 4, SKTextAlign.Left, font, rollLabelPaint);

            double gValue = gMax - (gMax - gMin) * i / hLines;
            canvas.DrawText(gValue.ToString("F1"), info.Width - 4, y + 4, SKTextAlign.Right, font, gLabelPaint);
        }

        double startTs = _frames[0].Left.Timestamp;

        const int xLabels = 4;
        for (int i = 0; i <= xLabels; i++)
        {
            int index = (int)Math.Round((n - 1) * i / (double)xLabels);
            index = Math.Clamp(index, 0, n - 1);
            float x = ml + index * scaleX;
            canvas.DrawLine(x, mt, x, mt + ph, gridPaint);

            double seconds = (_frames[index].Left.Timestamp - startTs) / 1000.0;
            canvas.DrawText(seconds.ToString("F1") + "s", x, mt + ph + 18, SKTextAlign.Center, font, labelPaint);
        }

        using var outerPaint = new SKPaint
        {
            Color = SKColor.Parse("#00BFFF"),
            StrokeWidth = 2.2f,
            IsStroke = true,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        using var innerPaint = new SKPaint
        {
            Color = SKColor.Parse("#668FB8D8"),
            StrokeWidth = 1.6f,
            IsStroke = true,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        using var gPaint = new SKPaint
        {
            Color = SKColor.Parse("#FFA500"),
            StrokeWidth = 2.2f,
            IsStroke = true,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        using var dash = SKPathEffect.CreateDash(new float[] { 5, 4 }, 0);
        innerPaint.PathEffect = dash;

        DrawSeries(canvas, innerRoll, rollMin, rollMax, ml, mt, pw, ph, innerPaint);
        DrawSeries(canvas, outerRoll, rollMin, rollMax, ml, mt, pw, ph, outerPaint);
        DrawSeries(canvas, g, gMin, gMax, ml, mt, pw, ph, gPaint);

        DrawMarker(canvas, ml, mt, scaleX, ph, engagement, SKColor.Parse("#4DA6FF"), true);
        DrawMarker(canvas, ml, mt, scaleX, ph, rollPeak, SKColors.White, false);
        DrawMarker(canvas, ml, mt, scaleX, ph, gPeak, SKColor.Parse("#FFB347"), true);
    }

    private static void DrawPhase(
        SKCanvas canvas, float ml, float mt, float scaleX, float ph,
        int startIndex, int endIndex, SKColor color)
    {
        if (endIndex <= startIndex) return;

        float x0 = ml + startIndex * scaleX;
        float x1 = ml + endIndex * scaleX;

        using var paint = new SKPaint { Color = color, IsAntialias = false };
        canvas.DrawRect(x0, mt, x1 - x0, ph, paint);
    }

    private static void DrawSeries(
        SKCanvas canvas, double[] values, double min, double max,
        float ml, float mt, float pw, float ph, SKPaint paint)
    {
        if (values.Length < 2 || max <= min) return;

        float scaleX = pw / (values.Length - 1);
        var path = new SKPath();
        bool started = false;

        for (int i = 0; i < values.Length; i++)
        {
            double value = values[i];
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                started = false;
                continue;
            }

            float x = ml + i * scaleX;
            float y = mt + (float)((max - value) / (max - min) * ph);
            y = Math.Clamp(y, mt, mt + ph);

            if (!started)
            {
                path.MoveTo(x, y);
                started = true;
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        canvas.DrawPath(path, paint);
        path.Dispose();
    }

    private static void DrawMarker(
        SKCanvas canvas, float ml, float mt, float scaleX, float ph,
        int index, SKColor color, bool dashed)
    {
        float x = ml + index * scaleX;

        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = 1.4f,
            IsAntialias = true
        };

        if (dashed)
        {
            using var effect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0);
            paint.PathEffect = effect;
            canvas.DrawLine(x, mt, x, mt + ph, paint);
        }
        else
        {
            canvas.DrawLine(x, mt, x, mt + ph, paint);
        }
    }
}
