using SkiTelemetry_Core;

public class SkiDataProcessor
{
    public event EventHandler<SkiTurn>? OnNewTurn;
    private readonly TurnDetector _detector = new();

    // Dizionario per sincronizzare i dati dei due sci tramite Timestamp
    private readonly Dictionary<uint, SkiDataPoint> _pendingLeft = new Dictionary<uint, SkiDataPoint>();
    private readonly Dictionary<uint, SkiDataPoint> _pendingRight = new Dictionary<uint, SkiDataPoint>();

    private double _gAdjuster = 0;

    private class FilterState
    {
        public double AccX, AccY, AccZ, GyroX, GyroY, GyroZ, Roll, AdjustedG;
        public bool IsInitialized = false;
    }
    private readonly FilterState _fL = new();
    private readonly FilterState _fR = new();

    // Coefficiente di smoothing (0.0 a 1.0). Più è basso, più è filtrato.
    private const double AccAlpha = 0.05;
    private const double GyroAlpha = 0.05;

    public SkiDataProcessor()
    {
        _detector.OnTurnCompleted += OnTurnCompleted;
    }

    /// <summary>
    /// Punto di ingresso unico per i dati grezzi che arrivano dal sensore
    /// </summary>
    public void Process(SkiDataPoint rawPoint)
    {
        // 1. Normalizzazione e Smoothing
        ApplyFilters(rawPoint);

        // 2. Sincronizzazione dei due piedi
        if (rawPoint.Side == SensorSide.Left)
            _pendingLeft[rawPoint.Timestamp] = rawPoint;
        else
            _pendingRight[rawPoint.Timestamp] = rawPoint;

        // 3. Se abbiamo entrambi i piedi per questo timestamp, processiamo il "Frame"
        if (_pendingLeft.TryGetValue(rawPoint.Timestamp, out var left) &&
            _pendingRight.TryGetValue(rawPoint.Timestamp, out var right))
        {
            _detector.ProcessFrame(new SkiFrame(left, right));
            _pendingLeft.Remove(rawPoint.Timestamp);
            _pendingRight.Remove(rawPoint.Timestamp);
        }

        // Opzionale: pulizia buffer per timestamp persi (timeout)
        if (_pendingLeft.Count > 100) _pendingLeft.Clear();
        if (_pendingRight.Count > 100) _pendingRight.Clear();
    }

    private void ApplyFilters(SkiDataPoint p)
    {
        FilterState f = (p.Side == SensorSide.Left) ? _fL : _fR;
        
        double dt = 0.02;

        if (!f.IsInitialized)
        {
            f.AccX = p.AccX; f.AccY = p.AccY; f.AccZ = p.AccZ;
            f.GyroX = p.GyroX; f.GyroY = p.GyroY; f.GyroZ = p.GyroZ;
            f.Roll = p.Roll;
            f.IsInitialized = true;
        }

        // Filtriamo e popoliamo le proprietà Smooth
        p.SmoothAccX = f.AccX = f.AccX + AccAlpha * (p.AccX - f.AccX);
        p.SmoothAccY = f.AccY = f.AccY + AccAlpha * (p.AccY - f.AccY);
        p.SmoothAccZ = f.AccZ = f.AccZ + AccAlpha * (p.AccZ - f.AccZ);

        p.SmoothGyroX = f.GyroX = f.GyroX + GyroAlpha * (p.GyroX - f.GyroX);
        p.SmoothGyroY = f.GyroY = f.GyroY + GyroAlpha * (p.GyroY - f.GyroY);
        p.SmoothGyroZ = f.GyroZ = f.GyroZ + GyroAlpha * (p.GyroZ - f.GyroZ);
        f.Roll = 0.98 * (f.Roll + p.GyroZ * dt) + 0.02 * p.Roll; // 98% gyro, 2% roll statico
        p.SmoothRoll = f.Roll;

        double rawG = Math.Sqrt(Math.Pow(p.SmoothAccX, 2) + Math.Pow(p.SmoothAccY, 2) + Math.Pow(p.SmoothAccZ, 2));

        if(rawG<1)
        {
            rawG = 1;
            _gAdjuster = Math.Max(_gAdjuster, (1 - rawG));
        }
        else
        {
            rawG += _gAdjuster;
        }
        p.AdjustedG = rawG;
        f.AdjustedG = p.AdjustedG = f.AdjustedG + GyroAlpha * (p.AdjustedG - f.AdjustedG);
        


    }
    private void OnTurnCompleted(object? sender, SkiTurn turn)
    {
        OnNewTurn?.Invoke(this, turn);
    }
    public void setMetrics(HashSet<MonitorMode> metrics)
    {

        _detector.RequestedMetrics.Clear();
        foreach (MonitorMode metric in metrics)
        {

            _detector.RequestedMetrics.Add(metric);
        }
    }
}