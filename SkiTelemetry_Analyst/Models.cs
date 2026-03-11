using Microsoft.Maui.Controls.Shapes;

namespace SkiTelemetry_Analyst;

public enum MonitorMode
{
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
}

public enum SensorSide { Left, Right }