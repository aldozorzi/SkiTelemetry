using SkiTelemetry_Analyst.Helpers;
using SkiTelemetry_Core;

namespace SkiTelemetry_Analyst.Services;

public class CalibrationService
{
    private static readonly Lazy<CalibrationService> _instance = new(() => new CalibrationService());
    private const string LastCalibDateKey = "LastCalibrationDate";

    RollingBuffer leftAccXList = new(150);
    RollingBuffer leftAccYList = new(150);
    RollingBuffer leftAccZList = new(150);
    RollingBuffer leftGyroXList = new(150);
    RollingBuffer leftGyroYList = new(150);
    RollingBuffer leftGyroZList = new(150);

    RollingBuffer rightAccXList = new(150);
    RollingBuffer rightAccYList = new(150);
    RollingBuffer rightAccZList = new(150);
    RollingBuffer rightGyroXList = new(150);
    RollingBuffer rightGyroYList = new(150);
    RollingBuffer rightGyroZList = new(150);

    private readonly Queue<SkiFrame> _recentFrames = new(150);

    private int _consecutiveDeadChecks = 0;
    private const int DEAD_CHECK_THRESHOLD = 3;

    public static CalibrationService Instance => _instance.Value;

    public CalibrationOffsets OffsetL { get; private set; }
    public CalibrationOffsets OffsetR { get; private set; }

    private readonly TelemetryLogger _logger = new();

    private DateTime _lastCalibTime = DateTime.MinValue;
    private const int MINUTES_BETWEEN_AUTOCALIBS = 5;

    private CalibrationService()
    {
        OffsetL = new CalibrationOffsets();
        OffsetR = new CalibrationOffsets();
        /*OffsetL = Preferences.Default.Get("CalibL", 0.0);
        OffsetR = Preferences.Default.Get("CalibR", 0.0);*/
    }

    public void AddFrame(SkiFrame frame,bool auto = true)
    {
                
        leftAccXList.Add(frame.Left.AccX);
        leftAccYList.Add(frame.Left.AccY);
        leftAccZList.Add(frame.Left.AccZ);
        leftGyroXList.Add(frame.Left.GyroX);
        leftGyroYList.Add(frame.Left.GyroY);
        leftGyroZList.Add(frame.Left.GyroZ);

        rightAccXList.Add(frame.Right.AccX);
        rightAccYList.Add(frame.Right.AccY);
        rightAccZList.Add(frame.Right.AccZ);
        rightGyroXList.Add(frame.Right.GyroX);
        rightGyroYList.Add(frame.Right.GyroY);
        rightGyroZList.Add(frame.Right.GyroZ);

        _recentFrames.Enqueue(frame);
        if (_recentFrames.Count > 150) _recentFrames.Dequeue();

        if(auto)
            CheckAndAutoCalibrate(frame);
    }

    private void CheckAndAutoCalibrate(SkiFrame frame)
    {
        if ((DateTime.Now - _lastCalibTime) < TimeSpan.FromMinutes(MINUTES_BETWEEN_AUTOCALIBS))
        {
            return;
        }
        // Calcola la magnitude dell'accelerometro
        double leftGMag = Math.Sqrt(
            Math.Pow(frame.Left.AccX, 2) +
            Math.Pow(frame.Left.AccY, 2) +
            Math.Pow(frame.Left.AccZ, 2));

        double rightGMag = Math.Sqrt(
            Math.Pow(frame.Right.AccX, 2) +
            Math.Pow(frame.Right.AccY, 2) +
            Math.Pow(frame.Right.AccZ, 2));

        if (!AreSensorsAlive())
        {
            _consecutiveDeadChecks++;
            if (_consecutiveDeadChecks >= DEAD_CHECK_THRESHOLD)
            {
                // Sensori probabilmente morti: skip calibrazione, logga warning
                System.Diagnostics.Trace.WriteLine("[CALIB] Sensors appear frozen, skipping auto-cal");
                return;
            }
        }
        else
        {
            // Reset counter se i sensori tornano vivi
            _consecutiveDeadChecks = 0;
        }

        // SOGLIE per considerare "fermo":
        bool isStationary =
            leftAccXList.Count >= 150 &&  // Buffer pieno
            rightAccXList.Count >= 150 &&

            // 1. Gyro molto stabili (std dev < 1.5 deg/s)
            leftGyroXList.StdDev < 1.5 && leftGyroYList.StdDev < 1.5 && leftGyroZList.StdDev < 1.5 &&
            rightGyroXList.StdDev < 1.5 && rightGyroYList.StdDev < 1.5 && rightGyroZList.StdDev < 1.5 &&

            // 2. Accelerometro stabile (std dev < 0.05G)
            leftAccXList.StdDev < 0.05 && leftAccYList.StdDev < 0.05 && leftAccZList.StdDev < 0.05 &&
            rightAccXList.StdDev < 0.05 && rightAccYList.StdDev < 0.05 && rightAccZList.StdDev < 0.05 &&

            // 3. G-force vicina a 1G (±0.1G)
            Math.Abs(leftGMag - 1.0) < 0.1 &&
            Math.Abs(rightGMag - 1.0) < 0.1;

        if (isStationary)
        {
            // Salva SOLO la calibrazione dei giroscopi
            OffsetL.GyroX = (float)leftGyroXList.Average;
            OffsetL.GyroY = (float)leftGyroYList.Average;
            OffsetL.GyroZ = (float)leftGyroZList.Average;

            OffsetR.GyroX = (float)rightGyroXList.Average;
            OffsetR.GyroY = (float)rightGyroYList.Average;
            OffsetR.GyroZ = (float)rightGyroZList.Average;

            _ = SpeechHelper.SpeakAsync($"Autocalibrazione effettuata.");

            //bool isSettingsLoggingEnabled = Preferences.Default.Get("IsLoggingEnabled", false);
            bool isSettingsLoggingEnabled = true; // forcing logging 

            if (!_logger.IsLogging && isSettingsLoggingEnabled)
            {
                _logger.StartLogging("Calibrations", $"{DateTime.Now.ToString("yyyy_MM_dd")}_{DateTime.Now.ToString("HH_mm")}_auto.bin");
            }
            else if (_logger != null && _logger.IsLogging)
            {
                foreach (var cachedFrame in _recentFrames)
                {
                    _logger.LogData(cachedFrame);
                }
            }
            _recentFrames.Clear();
            ResetBuffers();
            _lastCalibTime = DateTime.Now;
        }
    }

    public void Save()
    {
        
        OffsetL = new CalibrationOffsets
        {
            // GIROSCOPI: Se sei fermo, dovrebbero segnare 0. 
            // Quello che leggi è TUTTO errore (bias).
            GyroX = leftGyroXList.Average,
            GyroY = leftGyroYList.Average,
            GyroZ = leftGyroZList.Average,

            // ACCELEROMETRI: Qui decidi la "posa". 

            AccX = leftAccXList.Average - 0.0,
            AccY = leftAccYList.Average - 1.0,
            AccZ = leftAccZList.Average - 0.0
        };
        OffsetR = new CalibrationOffsets
        {
            // GIROSCOPI: Se sei fermo, dovrebbero segnare 0. 
            // Quello che leggi è TUTTO errore (bias).
            GyroX = rightGyroXList.Average,
            GyroY = rightGyroYList.Average,
            GyroZ = rightGyroZList.Average,

            // ACCELEROMETRI: Qui decidi la "posa". 
            AccX = rightAccXList.Average - 0.0,
            AccY = rightAccYList.Average - 1.0,
            AccZ = rightAccZList.Average - 0.0
        };

        /*Preferences.Default.Set("CalibL", l);
        Preferences.Default.Set("CalibR", r);
        Preferences.Default.Set(LastCalibDateKey, DateTime.Today.ToString("yyyy-MM-dd"));*/
        IsCalibratedToday = true;
        _lastCalibTime = DateTime.Now;
    }
    private bool AreSensorsAlive()
    {
        // Soglie basate sul rumore termico reale dell'M5 Stick Plus 2
        const double GYRO_NOISE_FLOOR = 0.05;  // deg/s: sotto è sospetto
        const double ACC_NOISE_FLOOR = 0.002;  // G: sotto è sospetto

        // Helper: un buffer è "alive" se mostra rumore o non è congelato
        bool IsBufferAlive(RollingBuffer buf, double noiseFloor)
        {
            // Caso 1: StdDev sopra la soglia = sicuramente vivo
            if (buf.StdDev > noiseFloor) return true;

            // Caso 2: Controlla valori bit-per-bit identici (congelato)
            // Se tutti gli ultimi N valori sono ESATTAMENTE uguali, il sensore è morto
            if (buf.Count >= 15 && buf.StdDev == 0.0)
            {
                // Aggiungere un metodo GetRecentValues() a RollingBuffer se serve
                // Oppure: se StdDev è zero per 15+ campioni, è congelato
                return false; // Frozen!
            }

            // Caso 3: StdDev bassa ma >0 = probabilmente vivo (rumore minimo)
            return buf.StdDev > 0.0;
        }

        // Basta che UNO degli assi mostri segni di vita
        return
            IsBufferAlive(leftGyroXList, GYRO_NOISE_FLOOR) ||
            IsBufferAlive(leftGyroYList, GYRO_NOISE_FLOOR) ||
            IsBufferAlive(leftGyroZList, GYRO_NOISE_FLOOR) ||
            IsBufferAlive(rightGyroXList, GYRO_NOISE_FLOOR) ||
            IsBufferAlive(rightGyroYList, GYRO_NOISE_FLOOR) ||
            IsBufferAlive(rightGyroZList, GYRO_NOISE_FLOOR) ||
            IsBufferAlive(leftAccXList, ACC_NOISE_FLOOR) ||
            IsBufferAlive(leftAccYList, ACC_NOISE_FLOOR) ||
            IsBufferAlive(leftAccZList, ACC_NOISE_FLOOR) ||
            IsBufferAlive(rightAccXList, ACC_NOISE_FLOOR) ||
            IsBufferAlive(rightAccYList, ACC_NOISE_FLOOR) ||
            IsBufferAlive(rightAccZList, ACC_NOISE_FLOOR);
    }
    private void ResetBuffers()
    {
        leftAccXList.Clear(); leftAccYList.Clear(); leftAccZList.Clear();
        leftGyroXList.Clear(); leftGyroYList.Clear(); leftGyroZList.Clear();

        rightAccXList.Clear(); rightAccYList.Clear(); rightAccZList.Clear();
        rightGyroXList.Clear(); rightGyroYList.Clear(); rightGyroZList.Clear();
    }
    public void Reset()
    {
        OffsetL.GyroX = OffsetL.GyroY = OffsetL.GyroZ = OffsetR.GyroX = OffsetR.GyroY = OffsetR.GyroZ = 0;
        ResetBuffers();
        Preferences.Default.Remove("CalibL");
        Preferences.Default.Remove("CalibR");
        Preferences.Default.Remove(LastCalibDateKey);
        CalibrationService.Instance.IsCalibratedToday = false;
    }
    public bool IsCalibratedToday = false;
    /*{
        get
        {
            var lastCalibStr = Preferences.Default.Get(LastCalibDateKey, string.Empty);
            if (string.IsNullOrEmpty(lastCalibStr)) return false;

            if (DateTime.TryParse(lastCalibStr, out DateTime lastDate))
            {
                // Restituisce true solo se la data salvata è uguale a oggi
                return lastDate.Date == DateTime.Today;
            }
            return false;
        }
    }*/


    // Ritorna l'oggetto per il Core
    public CalibrationOffsets? GetOffsets(SensorSide side)
    {
        return side == SensorSide.Left ? OffsetL : OffsetR;
    }
        

}