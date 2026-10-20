using SkiTelemetry_Core;

namespace SkiTelemetry_Analyst.Views.Runs;

public sealed class TurnDetailItem
{
    public List<SkiFrame> Frames { get; }
    public SkiTurn Turn { get; }

    public string Title => $"CURVA {Turn.Index:00}";
    public string FootText => Turn.Foot == SensorSide.Right
        ? "Piede esterno: destro"
        : "Piede esterno: sinistro";

    public string EdgeAngleText => $"{Turn.EdgeAngle.GetValueOrDefault():F0}°";
    public string GForceText => $"{Turn.GForce.GetValueOrDefault():F1}";
    public string EarlyLoadingText => $"{Turn.EarlyLoading.GetValueOrDefault():F0}%";
    public string EdgeBuildText => $"{Turn.ProgressiveEdgeBuild.GetValueOrDefault():F0}%";
    public string SimilarityText => $"{Turn.EdgeSimilarity.GetValueOrDefault():F0}%";
    public double PerformanceValue => Turn.PerformanceIndex.GetValueOrDefault();
    public string PerformanceTextShort => $"{Turn.PerformanceIndex.GetValueOrDefault():F0}%";
    public string DurationText => $"{Turn.Duration:F2}s";

    public TurnDetailItem(List<SkiFrame> frames, SkiTurn turn)
    {
        Frames = frames;
        Turn = turn;
    }
}
