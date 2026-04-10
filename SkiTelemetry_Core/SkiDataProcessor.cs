namespace SkiTelemetry_Core;

public class SkiDataProcessor
{
    public event EventHandler<SkiTurn>? OnNewTurn;
    private readonly TurnDetector _detector = new();

    private CalibrationOffsets _leftOffsets = new();
    private CalibrationOffsets _rightOffsets = new();

    // Bias relativo AccX stimato a runtime (Left rispetto a Right)
    

    public int _stableFramesCount = 0;

    private class FilterState
    {
        public double AccX, AccY, AccZ, GyroX, GyroY, GyroZ, Roll, AdjustedG, GAdjuster;
        public Queue<double> AccXBuf = new(5);
        public Queue<double> AccYBuf = new(5);
        public Queue<double> AccZBuf = new(5);
        public bool IsInitialized = false;
        public double InitialRollOffset { get; set; } = 0.0; // 🆕 Lo "Zero" del gambetto
        public bool IsOffsetLocked { get; set; } = false;
        public MadgwickAHRS? MadgwickFilter { get; set; }
    }
    private readonly FilterState _fL = new();
    private readonly FilterState _fR = new();

    // Coefficiente di smoothing (0.0 a 1.0). Più è basso, più è filtrato.
    private const double AccAlpha = 0.05;
    private const double GyroAlpha = 0.10;
    private const double AccRollWeight = 0.2;

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

        // 1. CALIBRAZIONE (Sottrazione Bias)
        p.AccX -= (float)currentOffset.AccX; p.AccY -= (float)currentOffset.AccY; p.AccZ -= (float)currentOffset.AccZ;
        p.GyroX -= (float)currentOffset.GyroX; p.GyroY -= (float)currentOffset.GyroY; p.GyroZ -= (float)currentOffset.GyroZ;
        //System.Diagnostics.Debug.WriteLine($"p.GyroZ: {p.GyroZ}");

        // 2. CLAMP DI SICUREZZA (Taglio spike da vibrazioni/ADC)
        p.AccX = Math.Clamp(p.AccX, -6.5f, 6.5f);
        p.AccY = Math.Clamp(p.AccY, -6.5f, 6.5f);
        p.AccZ = Math.Clamp(p.AccZ, -6.5f, 6.5f);
        p.GyroX = Math.Clamp(p.GyroX, -200f, 200f);
        p.GyroY = Math.Clamp(p.GyroY, -200f, 200f);
        p.GyroZ = Math.Clamp(p.GyroZ, -200f, 200f);


        // Calcolo Delta Time (protetto da divisione per zero o salti temporali)
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
        dt = Math.Clamp(dt, 0.01, 0.05);
        
        // 3. INIZIALIZZAZIONE STATO FILTRO
        if (!f.IsInitialized)
        {
            f.AccX = p.AccX; f.AccY = p.AccY; f.AccZ = p.AccZ;
            f.GyroX = p.GyroX; f.GyroY = p.GyroY; f.GyroZ = p.GyroZ;

            f.Roll = Math.Atan2(p.AccX, p.AccY) * (180.0 / Math.PI);

            for (int i = 0; i < 5; i++)
            {
                f.AccXBuf.Enqueue(p.AccX);
                f.AccYBuf.Enqueue(p.AccY);
                f.AccZBuf.Enqueue(p.AccZ);
            }
            f.IsInitialized = true;
            p.SmoothRoll = f.Roll;
            p.AdjustedG = Math.Sqrt(f.AccX * f.AccX + f.AccY * f.AccY + f.AccZ * f.AccZ);
            f.MadgwickFilter = new MadgwickAHRS(dt, AccRollWeight);
            return;
        }

        // 4. FILTRAGGIO ACCELEROMETRO (Mediana 5 campioni + EMA)
        double maX = PushAndMedian(f.AccXBuf, p.AccX);
        double maY = PushAndMedian(f.AccYBuf, p.AccY);
        double maZ = PushAndMedian(f.AccZBuf, p.AccZ);

        p.SmoothAccX = f.AccX = f.AccX + AccAlpha * (maX - f.AccX);
        p.SmoothAccY = f.AccY = f.AccY + AccAlpha * (maY - f.AccY);
        p.SmoothAccZ = f.AccZ = f.AccZ + AccAlpha * (maZ - f.AccZ);

        // 5. FILTRAGGIO GIROSCOPIO (EMA)
        p.SmoothGyroX = f.GyroX = f.GyroX + GyroAlpha * (p.GyroX - f.GyroX);
        p.SmoothGyroY = f.GyroY = f.GyroY + GyroAlpha * (p.GyroY - f.GyroY);
        p.SmoothGyroZ = f.GyroZ = f.GyroZ + GyroAlpha * (p.GyroZ - f.GyroZ);

        // 6. CALCOLO ROLL
        double degToRad = Math.PI / 180.0;
        double gxRad = p.GyroX * degToRad;
        double gyRad = p.GyroY * degToRad;
        double gzRad = p.GyroZ * degToRad;

        // Calcoliamo il valore centrato prima di passarlo al filtro

        f.MadgwickFilter!.Update(
            gxRad,           // Madgwick vede il tuo Z come Roll
            gyRad,           // Madgwick vede il tuo X come Pitch
            gzRad,           // Madgwick vede il tuo Y come Yaw
            p.SmoothAccX,    // Corrispondente Acc per Z
            p.SmoothAccY,    // Corrispondente Acc per X
            p.SmoothAccZ     // Corrispondente Acc per Y
        );
        f.Roll = f.MadgwickFilter.GetM5StickRoll();
        p.SmoothRoll = f.Roll;


        double gMagnitude = Math.Sqrt(p.SmoothAccX * p.SmoothAccX + p.SmoothAccY * p.SmoothAccY + p.SmoothAccZ * p.SmoothAccZ);
        double gDeviation = Math.Abs(gMagnitude - 1.0);
        // G totale calcolata
        p.AdjustedG = gMagnitude;
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