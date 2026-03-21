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

    public override string ToString()
    {
        return $"[DATA] Ts: {Timestamp}, Side: {Side}, Roll: {Roll:0.0}, Acc:({AccX:0.0},{AccY:0.0},{AccZ:0.0}, Gyro:{GyroY:0.0})";
    }
}

public class SensorBias
{
    public float AccX, AccY, AccZ;
    public float GyroX, GyroY, GyroZ;
}