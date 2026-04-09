using SkiTelemetry_Core;
using System.Runtime.CompilerServices;

public class SkiDataProcessor
{
    public event EventHandler<SkiTurn>? OnNewTurn;
    private readonly TurnDetector _detector = new();

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
    private const double GyroAlpha = 0.70;
    private const double AccRollWeight = 0.07;

    private double _lastTimestampL = 0;
    private double _lastTimestampR = 0;

    public SkiDataProcessor()
    {
        _detector.OnTurnCompleted += OnTurnCompleted;
    }

    /// <summary>
    /// Punto di ingresso unico per i dati grezzi che arrivano dal sensore
    /// </summary>
    public void Process(SkiFrame rawFrame)
    {

        ApplyFilters(rawFrame.Left);
        ApplyFilters(rawFrame.Right);

        _detector.ProcessFrame(rawFrame);
    }

    

    private void ApplyFilters(SkiDataPoint p)
    {
        FilterState f = (p.Side == SensorSide.Left) ? _fL : _fR;
        CalibrationOffsets currentOffset = (p.Side == SensorSide.Left) ? _leftOffsets : _rightOffsets;
        p.AccX -= (float)currentOffset.AccX; p.AccY -= (float)currentOffset.AccY; p.AccZ -= (float)currentOffset.AccZ;
        p.GyroX -= (float)currentOffset.GyroX; p.GyroY -= (float)currentOffset.GyroY; p.GyroZ -= (float)currentOffset.GyroZ;

        p.AccX = Math.Clamp(p.AccX, -4.5f, 4.5f);
        p.AccY = Math.Clamp(p.AccY, -4.5f, 4.5f);
        p.AccZ = Math.Clamp(p.AccZ, -4.5f, 4.5f);
        p.GyroX = Math.Clamp(p.GyroX, -200f, 200f);
        p.GyroY = Math.Clamp(p.GyroY, -200f, 200f);
        p.GyroZ = Math.Clamp(p.GyroZ, -200f, 200f);

        //double dt = 0.02;

        double dt;
        if (p.Side == SensorSide.Left)
        {
            dt = _lastTimestampL == 0 ? 0.02 : (p.Timestamp - _lastTimestampL) / 1000.0;
            _lastTimestampL = p.Timestamp;
        }
        else
        {
            dt = _lastTimestampR == 0 ? 0.02 : (p.Timestamp - _lastTimestampR) / 1000.0;
            _lastTimestampR = p.Timestamp;
        }
        dt = Math.Clamp(dt, 0.01, 0.05); // Safety clamp tra 10ms e 50ms


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

        // 5. Filtro Complementare ADATTIVO (peso all'accelerometro ridotto durante le curve)
        double gMagnitude = Math.Sqrt(p.SmoothAccX * p.SmoothAccX + p.SmoothAccY * p.SmoothAccY + p.SmoothAccZ * p.SmoothAccZ);
        double gDeviation = Math.Abs(gMagnitude - 1.0);

        // Se le G si discostano di >0.4G, riduci drasticamente il peso dell'accelerometro
        double accWeight = (gDeviation > 0.4 || Math.Abs(p.AccX) >= 0.95) ? 0.00 : AccRollWeight;


        double gyroWeight = 1.0 - accWeight;

        double safeAccRoll = Math.Clamp(p.Roll, -75.0, 75.0);

        f.Roll = gyroWeight * (f.Roll + p.GyroZ * dt) + accWeight * safeAccRoll;
        p.SmoothRoll = f.Roll;

        double rawG = Math.Sqrt(p.SmoothAccX * p.SmoothAccX + p.SmoothAccY * p.SmoothAccY + p.SmoothAccZ * p.SmoothAccZ);
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