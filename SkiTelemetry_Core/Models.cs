using System.Security.AccessControl;

namespace SkiTelemetry_Core;

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
    DebugData,
    Foot
}



public enum SensorSide { Left, Right }

public class SkiTurn
{
    public uint StartTimestamp { get; set; }
    public uint EndTimestamp { get; set; }

    public double Duration => EndTimestamp >= StartTimestamp
    ? (double)(EndTimestamp - StartTimestamp) / 1000
    : 0; // Gestione errore se End è prima di Start

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

public class SkiRun
{
    public int Id { get; set; }
    public required string FileName { get; set; } // es: "run_20240520_1015.csv"
    public int AthleteId { get; set; }
    public DateTime UploadDate { get; set; }
    public User? Athlete { get; set; }
}
public class CoachAthleteRelation
{
    public int CoachId { get; set; }
    public int AthleteId { get; set; }
}
public enum UserRole
{
    Admin,
    Coach,
    Athlete,
    
}
public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; } // Mai salvare password in chiaro!
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<SkiRun> Runs { get; set; } = new();

}

public class SkiDataPoint
{
    public SensorSide Side { get; set; }
    public uint Timestamp { get; set; } // Millisecondi dall'avvio
    public float AccX { get; set; }
    public float AccY { get; set; }
    public float AccZ { get; set; }
    public float GyroX { get; set; }
    public float GyroY { get; set; }
    public float GyroZ { get; set; }

    // Calcoliamo l'inclinazione (Roll) semplificata per ora
    // public double rawRoll => Math.Atan2(AccX, AccY) * (180 / Math.PI);
    public double Roll => Math.Atan2(AccX, AccY) * (180 / Math.PI);
    public double SmoothRoll { get; set; } // Dato filtrato
    public double SmoothAccX { get; set; } // Dato filtrato
    public double SmoothAccY { get; set; } // Dato filtrato
    public double SmoothAccZ { get; set; } // Dato filtrato
    public double SmoothGyroX { get; set; } // Dato filtrato
    public double SmoothGyroY { get; set; } // Dato filtrato
    public double SmoothGyroZ { get; set; } // Dato filtrato
    public double AdjustedG { get; set; } // Dato filtrato

    public override string ToString()
    {
        return $"[DATA] Ts: {Timestamp} - Side: {Side} - Roll: {Roll:0.0} - SmoothRoll: {SmoothRoll} - AccX:{AccX:0.0} - SmoothAccX:{SmoothAccX} - AccY:{AccY:0.0} - SmoothAccY:{SmoothAccY} - AccZ:{AccZ:0.0} - SmoothAccZ:{SmoothAccZ} - GyroX:{GyroX:0.0} - GyroY:{GyroY:0.0} - GyroZ:{GyroZ:0.0}";    }
}

public class SkiFrame
{

    public SkiDataPoint Left { get; set; }
    public SkiDataPoint Right { get; set; }
    public double CombinedGyro => (Left.SmoothGyroZ + Right.SmoothGyroZ) / 2.0;

    public SkiFrame(SkiDataPoint left, SkiDataPoint right)
    {
        Left = left;
        Right = right;
    }
}

public class SensorBias
{
    public float AccX, AccY, AccZ;
    public float GyroX, GyroY, GyroZ;
}

public class TurnMetrics
{
    private double? _turnEdgeAngle { get; set; }
    private double? _turnEdgeSimilarity { get; set; }
    private double? _turnEarlyEdging { get; set; }
    private double? _turnGForce { get; set; }
    private double? _turnRadius { get; set; }
    private double? _turnShape { get; set; }
    private double? _turnProgressiveEdgeBuild { get; set; }
    private double? _turnCarvingIndex { get; set; }
    private SensorSide? _foot { get; set; }
}