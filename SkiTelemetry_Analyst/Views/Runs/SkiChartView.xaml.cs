using Microsoft.Maui.Controls.Shapes;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using SkiTelemetry_Core;

namespace SkiTelemetry_Analyst.Views.Runs;

public partial class SkiChartView : ContentView
{
    // ── Definizione serie ────────────────────────────────────────
    private record SeriesInfo(
        string Key, string Label, string Color,
        bool DefaultVisible,
        Func<SkiDataPoint, double> Selector,
        bool IsLeft = true);

    private static readonly List<SeriesInfo> AllSeries = new()
    {
        new("SmoothRoll_L",  "Roll L",    "#00BFFF", true,  p => p.SmoothRoll),
        new("SmoothRoll_R",  "Roll R",    "#FFA500", true,  p => p.SmoothRoll,    false),
        new("AdjustedG_L",   "G L",       "#1E90FF", true,  p => p.AdjustedG),
        new("AdjustedG_R",   "G R",       "#FF6347", true,  p => p.AdjustedG,     false),
        new("AccX_L",        "AccX L",    "#FF69B4", false, p => p.AccX),
        new("AccX_R",        "AccX R",    "#ADFF2F", false, p => p.AccX,          false),
        new("AccY_L",        "AccY L",    "#FF1493", false, p => p.AccY),
        new("AccY_R",        "AccY R",    "#7FFF00", false, p => p.AccY,          false),
        new("AccZ_L",        "AccZ L",    "#DA70D6", false, p => p.AccZ),
        new("AccZ_R",        "AccZ R",    "#F08080", false, p => p.AccZ,          false),
        new("SmoothAccX_L",  "SAccX L",  "#BA55D3", false, p => p.SmoothAccX),
        new("SmoothAccX_R",  "SAccX R",  "#CD5C5C", false, p => p.SmoothAccX,    false),
        new("SmoothAccY_L",  "SAccY L",  "#90EE90", false, p => p.SmoothAccY),
        new("SmoothAccY_R",  "SAccY R",  "#DDA0DD", false, p => p.SmoothAccY,    false),
        new("SmoothAccZ_L",  "SAccZ L",  "#3CB371", false, p => p.SmoothAccZ),
        new("SmoothAccZ_R",  "SAccZ R",  "#9370DB", false, p => p.SmoothAccZ,    false),
        new("SmoothGyroX_L", "SGyX L",   "#FFD700", false, p => p.SmoothGyroX),
        new("SmoothGyroX_R", "SGyX R",   "#87CEEB", false, p => p.SmoothGyroX,   false),
        new("SmoothGyroY_L", "SGyY L",   "#DAA520", false, p => p.SmoothGyroY),
        new("SmoothGyroY_R", "SGyY R",   "#4682B4", false, p => p.SmoothGyroY,   false),
        new("SmoothGyroZ_L", "SGyZ L",   "#98FB98", false, p => p.SmoothGyroZ),
        new("SmoothGyroZ_R", "SGyZ R",   "#DDA0DD", false, p => p.SmoothGyroZ,   false),
    };

    // ── Stato ────────────────────────────────────────────────────
    private readonly Dictionary<string, bool> _visible = new();
    private List<SkiFrame> _frames = new();
    private List<SkiFrame> _sampled = new();

    // Viewport: indici campionati visibili
    private float _viewStart = 0f;   // 0..1
    private float _viewEnd = 1f;   // 0..1

    // Pan gesture
    private float? _panStartX = null;
    private float _panStartViewStart;
    private float _panStartViewEnd;

    // Cache paint
    private readonly Dictionary<string, SKPaint> _paints = new();

    public SkiChartView()
    {
        InitializeComponent();

        foreach (var s in AllSeries)
        {
            _visible[s.Key] = s.DefaultVisible;
            _paints[s.Key] = MakePaint(s.Color);
        }

        BuildToggleButtons();
    }

    // ── API pubblica ─────────────────────────────────────────────
    public void LoadFrames(List<SkiFrame> frames)
    {
        _frames = frames;

        // Campionamento: max 1200 punti per fluidità
        int step = Math.Max(1, frames.Count / 1200);
        _sampled = frames.Where((_, i) => i % step == 0).ToList();

        _viewStart = 0f;
        _viewEnd = 1f;

        ChartCanvas.InvalidateSurface();
    }

    // ── Toggle buttons ───────────────────────────────────────────
    private void BuildToggleButtons()
    {
        ToggleButtons.Children.Clear();
        foreach (var s in AllSeries)
        {
            var active = _visible[s.Key];

            var btnContainer = new Border
            {
                // Mantiene CornerRadius (StrokeShape)
                StrokeShape = new RoundRectangle
                {
                    CornerRadius = new CornerRadius(8)
                },

                // Mantiene BorderColor e BorderWidth
                Stroke = ParseColor(s.Color),
                StrokeThickness = 1,

                // Mantiene BackgroundColor e Padding
                BackgroundColor = active ? ParseColor(s.Color) : Color.FromArgb("#332B0A4E"),
                Padding = new Thickness(10, 10),

                // Layout interno
                Content = new Label
                {
                    Text = s.Label,
                    FontSize = 10,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,

                    // Mantiene TextColor
                    TextColor = active ? Colors.Black : ParseColor(s.Color),
                }
            };

            var key = s.Key;
            var col = s.Color;

            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (sender, _) =>
            {
                // 1. Invertiamo lo stato di visibilità
                _visible[key] = !_visible[key];

                // 2. Il 'sender' del Tapped è il Border (btnContainer)
                if (sender is Border border)
                {
                    bool isVisible = _visible[key];

                    // Aggiorniamo il Background del Border
                    border.BackgroundColor = isVisible ? ParseColor(col) : Color.FromArgb("#332B0A4E");

                    // Cerchiamo la Label nel Content per cambiare il TextColor
                    if (border.Content is Label label)
                    {
                        label.TextColor = isVisible ? Colors.Black : ParseColor(col);
                    }
                }

                // 3. Refresh del grafico
                ChartCanvas.InvalidateSurface();
            };
            btnContainer.GestureRecognizers.Add(tapGesture);

            ToggleButtons.Children.Add(btnContainer);
        }
    }

    // ── Touch / Pan ──────────────────────────────────────────────
    private void OnTouch(object? sender, SKTouchEventArgs? e)
    {
        e!.Handled = true;
        float x = e.Location.X;
        float w = (float)ChartCanvas.Width;
        if (w <= 0) return;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                _panStartX = x;
                _panStartViewStart = _viewStart;
                _panStartViewEnd = _viewEnd;
                break;

            case SKTouchAction.Moved when _panStartX.HasValue:
                float dx = (x - _panStartX.Value) / w;
                float range = _panStartViewEnd - _panStartViewStart;
                float ns = _panStartViewStart - dx * range;
                float ne = _panStartViewEnd - dx * range;

                if (ns < 0) { ne -= ns; ns = 0; }
                if (ne > 1) { ns -= (ne - 1); ne = 1; }

                _viewStart = Math.Clamp(ns, 0, 1 - range);
                _viewEnd = _viewStart + range;
                ChartCanvas.InvalidateSurface();
                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                _panStartX = null;
                break;
        }
    }

    // ── Zoom buttons ─────────────────────────────────────────────
    private void OnZoomIn(object sender, EventArgs e) => ApplyZoom(0.6f);
    private void OnZoomOut(object sender, EventArgs e) => ApplyZoom(1.6f);
    private void OnResetZoom(object sender, EventArgs e)
    {
        _viewStart = 0f; _viewEnd = 1f;
        ChartCanvas.InvalidateSurface();
    }

    private void ApplyZoom(float factor)
    {
        float center = (_viewStart + _viewEnd) / 2f;
        float half = (_viewEnd - _viewStart) / 2f * factor;
        _viewStart = Math.Clamp(center - half, 0, 1);
        _viewEnd = Math.Clamp(center + half, 0, 1);
        if (_viewEnd - _viewStart < 0.01f) _viewEnd = _viewStart + 0.01f;
        if (_viewEnd > 1) { _viewStart = 1 - (_viewEnd - _viewStart); _viewEnd = 1; }
        ChartCanvas.InvalidateSurface();
    }

    // ── Rendering ────────────────────────────────────────────────
    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs? e)
    {
        var canvas = e!.Surface.Canvas;
        var info = e!.Info;
        canvas.Clear(SKColors.Transparent);

        if (!_sampled.Any()) return;

        // Margini
        const float ml = 48f, mr = 12f, mt = 12f, mb = 28f;
        float pw = info.Width - ml - mr;
        float ph = info.Height - mt - mb;
        if (pw <= 0 || ph <= 0) return;

        // Finestra visibile
        int total = _sampled.Count;
        int iStart = (int)(_viewStart * total);
        int iEnd = (int)(_viewEnd * total);
        iStart = Math.Clamp(iStart, 0, total - 2);
        iEnd = Math.Clamp(iEnd, iStart + 1, total);
        var visible = _sampled.Skip(iStart).Take(iEnd - iStart).ToList();
        if (visible.Count < 2) return;

        // Min/Max globale Y tra serie visibili
        double yMin = double.MaxValue, yMax = double.MinValue;
        foreach (var s in AllSeries.Where(s => _visible[s.Key]))
        {
            var src = s.IsLeft ? visible.Select(f => s.Selector(f.Left))
                               : visible.Select(f => s.Selector(f.Right));
            foreach (var v in src)
            {
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                if (v < yMin) yMin = v;
                if (v > yMax) yMax = v;
            }
        }
        if (yMin >= yMax) { yMin -= 1; yMax += 1; }
        double yRange = yMax - yMin;

        // Grid Y
        using var gridPaint = new SKPaint
        { Color = SKColor.Parse("#4DFFFFFF"), StrokeWidth = 1, IsAntialias = false };
        using var labelPaint = new SKPaint
        { Color = SKColor.Parse("#B9A3D6"), IsAntialias = true };

        int gridLines = 5;
        SKTextAlign align = new SKTextAlign();
        SKFont font = new SKFont();
        for (int g = 0; g <= gridLines; g++)
        {
            float gy = mt + ph * g / gridLines;
            canvas.DrawLine(ml, gy, ml + pw, gy, gridPaint);
            double val = yMax - yRange * g / gridLines;
            canvas.DrawText(val.ToString("F1"), 2, gy + 6, align, font, labelPaint);
        }

        // Asse X labels (timestamp)
        int xLabels = 6;

        double startTs = _sampled[0].Left.Timestamp;

        for (int g = 0; g <= xLabels; g++)
        {
            int idx = (int)(visible.Count * g / (float)xLabels);
            idx = Math.Clamp(idx, 0, visible.Count - 1);
            float gx = ml + pw * g / xLabels;
            canvas.DrawLine(gx, mt, gx, mt + ph, gridPaint);
            double ts = (visible[idx].Left.Timestamp - startTs) / 1000.0;
            canvas.DrawText(ts.ToString("F1") + "s", gx - 12, mt + ph + 20, align, font, labelPaint);
        }

        // Linee serie
        float scaleX = pw / (visible.Count - 1);

        foreach (var s in AllSeries.Where(s => _visible[s.Key]))
        {
            var src = s.IsLeft ? visible.Select(f => s.Selector(f.Left)).ToList()
                               : visible.Select(f => s.Selector(f.Right)).ToList();

            var path = new SKPath();
            bool started = false;

            for (int i = 0; i < src.Count; i++)
            {
                double v = src[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) { started = false; continue; }

                float px = ml + i * scaleX;
                float py = mt + (float)((yMax - v) / yRange * ph);
                py = Math.Clamp(py, mt, mt + ph);

                if (!started) { path.MoveTo(px, py); started = true; }
                else path.LineTo(px, py);
            }

            canvas.DrawPath(path, _paints[s.Key]);
            path.Dispose();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────
    private static SKPaint MakePaint(string hex) => new()
    {
        Color = SKColor.Parse(hex),
        StrokeWidth = 1.8f,
        IsStroke = true,
        IsAntialias = true,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    };

    private static Color ParseColor(string hex) => Color.FromArgb(hex);
}