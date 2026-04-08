using SkiTelemetry_Core;

public class SkiDataProcessor
{
    public event EventHandler<SkiTurn>? OnNewTurn;
    private readonly TurnDetector _detector = new();

    // Dizionario per sincronizzare i dati dei due sci tramite Timestamp
    private readonly Dictionary<uint, SkiDataPoint> _pendingLeft = new Dictionary<uint, SkiDataPoint>();
    private readonly Dictionary<uint, SkiDataPoint> _pendingRight = new Dictionary<uint, SkiDataPoint>();

    private CalibrationOffsets _leftOffsets = new();
    private CalibrationOffsets _rightOffsets = new();

    private class FilterState
    {
        public double AccX, AccY, AccZ, GyroX, GyroY, GyroZ, Roll, AdjustedG, GAdjuster;
        public Queue<double> AccXBuf = new(5);
        public Queue<double> AccYBuf = new(5);
        public Queue<double> AccZBuf = new(5);
        public bool IsInitialized = false;
    }
    private readonly FilterState _fL = new();
    private readonly FilterState _fR = new();

    // Coefficiente di smoothing (0.0 a 1.0). Più è basso, più è filtrato.
    private const double AccAlpha = 0.05;
    private const double GyroAlpha = 0.5;

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
        CalibrationOffsets currentOffset = (p.Side == SensorSide.Left) ? _leftOffsets : _rightOffsets;

        //System.Diagnostics.Trace.WriteLine($"[PROCESSOR] currentOffset {p.Side} {currentOffset} ");

        double dt = 0.02;

        p.AccX -= (float)currentOffset.AccX; p.AccY -= (float)currentOffset.AccY; p.AccZ -= (float)currentOffset.AccZ;
        p.GyroX -= (float)currentOffset.GyroX; p.GyroY -= (float)currentOffset.GyroY; p.GyroZ -= (float)currentOffset.GyroZ;
        if (!f.IsInitialized)
        {
            f.AccX = p.AccX; f.AccY = p.AccY; f.AccZ = p.AccZ ;
            f.GyroX = p.GyroX ; f.GyroY = p.GyroY ; f.GyroZ = p.GyroZ;
            f.Roll = p.Roll;
            for (int i = 0; i < 5; i++)
            {
                f.AccXBuf.Enqueue(p.AccX);
                f.AccYBuf.Enqueue(p.AccY);
                f.AccZBuf.Enqueue(p.AccZ);
            }

            double initialG = Math.Sqrt(Math.Pow(f.AccX, 2) + Math.Pow(f.AccY, 2) + Math.Pow(f.AccZ, 2));
            f.GAdjuster = (initialG < 1) ? (1 - initialG) : 0;
            f.AdjustedG = Math.Max(1.0, initialG+ f.GAdjuster);
            f.IsInitialized = true;
            p.SmoothRoll = f.Roll;
            p.AdjustedG = f.AdjustedG;
            return;
        }

        // Filtriamo e popoliamo le proprietà Smooth
        double maX = PushAndMedian(f.AccXBuf, p.AccX);
        double maY = PushAndMedian(f.AccYBuf, p.AccY);
        double maZ = PushAndMedian(f.AccZBuf, p.AccZ);
        p.SmoothAccX = f.AccX = f.AccX + AccAlpha * (maX - f.AccX);
        p.SmoothAccY = f.AccY = f.AccY + AccAlpha * (maY - f.AccY);
        p.SmoothAccZ = f.AccZ = f.AccZ + AccAlpha * (maZ - f.AccZ);

        p.SmoothGyroX = f.GyroX = f.GyroX + GyroAlpha * (p.GyroX - f.GyroX);
        p.SmoothGyroY = f.GyroY = f.GyroY + GyroAlpha * (p.GyroY - f.GyroY);
        p.SmoothGyroZ = f.GyroZ = f.GyroZ + GyroAlpha * (p.GyroZ - f.GyroZ);

       // System.Diagnostics.Trace.WriteLine($"[PROCESSOR] f.Roll {f.Roll} - p.GyroZ: {p.GyroZ} ");
        f.Roll = 0.90 * (f.Roll + p.GyroZ * dt) + 0.10 * p.Roll; // 90% gyro, 10% roll statico
        //f.Roll = 0.0 * (f.Roll + p.GyroZ * dt) + 1.0 * p.Roll;
        p.SmoothRoll = f.Roll;

        double rawG = Math.Sqrt(
        Math.Pow(p.SmoothAccX, 2) +
        Math.Pow(p.SmoothAccY, 2) +
        Math.Pow(p.SmoothAccZ, 2));

        p.AdjustedG = rawG;
        //p.AdjustedG = f.AdjustedG = f.AdjustedG + 0.15 * (rawG - f.AdjustedG);



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

    public void SetCalibration(CalibrationOffsets? leftOffsets, CalibrationOffsets? rightOffsets)
    {
        if(leftOffsets != null && rightOffsets != null)
        {
            _leftOffsets = leftOffsets;
            _rightOffsets = rightOffsets;
        }
        
    }
    private static double PushAndMedian(Queue<double> buf, double newVal, int size = 5)
    {
        buf.Enqueue(newVal);
        if (buf.Count > size) buf.Dequeue();
        var sorted = buf.OrderBy(x => x).ToList();
        return sorted[sorted.Count / 2];
    }
    private static double PushAndAverage(Queue<double> buf, double newVal, int size = 5)
    {
        buf.Enqueue(newVal);
        if (buf.Count > size) buf.Dequeue();
        return buf.Average();
    }
    public void Reset()
    {
        _fL.IsInitialized = false;
        _fR.IsInitialized = false;
        _fL.AccXBuf.Clear(); _fL.AccYBuf.Clear(); _fL.AccZBuf.Clear();
        _fR.AccXBuf.Clear(); _fR.AccYBuf.Clear(); _fR.AccZBuf.Clear();
    }

}