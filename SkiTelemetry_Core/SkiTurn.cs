namespace SkiTelemetry_Core
{
    public class SkiTurn
    {
        public uint StartTimestamp { get; set; }
        public uint EndTimestamp { get; set; }

        // Metriche opzionali (nulle se non richieste)
        public double? EdgeAngle { get; set; }
        public double? EdgeSimilarity { get; set; }
        public double? EarlyEdging { get; set; }
        public double? GForce { get; set; }
        public double? Radius { get; set; }
        public double? Shape { get; set; }
        public double? ProgressiveEdgeBuild { get; set; }
        public double? CarvingIndex { get; set; }

        public SensorSide? Foot { get; set; }
        public override string ToString()
        {
            return $"[DATA] EdgeAngle: {EdgeAngle}, EdgeSimilarity: {EdgeSimilarity}, EarlyEdging:{EarlyEdging}, GForce:{GForce}, Radius{Radius}, Shape: {Shape}, ProgressiveEdgeBuild: {ProgressiveEdgeBuild}, CarvingIndex:{CarvingIndex}, Foot:{Foot})";
        }
    }

    
}

/*
    EdgeAngle,
    EdgeSimilarity,
    EarlyEdging,
    GForce,
    TurnRadius,
    TurnShape,
    ProgressiveEdgeBuild,
    CarvingIndex,
    AngleVsGForce,
    SmoothnesIndex,
    EdgeEngagement,
    LineChoice,
    LoadDistribution,
    RaceEfficiencyIndex,
    DebugData
*/