using SkiTelemetry_Core;

namespace SkiTelemetry_Analyst.Services;

public class CalibrationService
{
    private static readonly Lazy<CalibrationService> _instance = new(() => new CalibrationService());
    private const string LastCalibDateKey = "LastCalibrationDate";

    RollingBuffer leftAccXList = new(200);
    RollingBuffer leftAccYList = new(200);
    RollingBuffer leftAccZList = new(200);
    RollingBuffer leftGyroXList = new(200);
    RollingBuffer leftGyroYList = new(200);
    RollingBuffer leftGyroZList = new(200);

    RollingBuffer rightAccXList = new(200);
    RollingBuffer rightAccYList = new(200);
    RollingBuffer rightAccZList = new(200);
    RollingBuffer rightGyroXList = new(200);
    RollingBuffer rightGyroYList = new(200);
    RollingBuffer rightGyroZList = new(200);

    public static CalibrationService Instance => _instance.Value;

    public CalibrationOffsets? OffsetL { get; private set; }
    public CalibrationOffsets? OffsetR { get; private set; }

    private readonly TelemetryLogger _logger = new();

    private CalibrationService()
    {

        /*OffsetL = Preferences.Default.Get("CalibL", 0.0);
        OffsetR = Preferences.Default.Get("CalibR", 0.0);*/
    }

    public void AddFrame(SkiFrame frame)
    {
        //bool isSettingsLoggingEnabled = Preferences.Default.Get("IsLoggingEnabled", false);
        bool isSettingsLoggingEnabled = true; // forcing logging 
        if (!_logger.isLogging && isSettingsLoggingEnabled)
        {
            _logger.StartLogging("Calibrations");
        }else if(_logger.isLogging)
        {
            _logger.LogData(frame.Left);
            _logger.LogData(frame.Right);
        }
        
        
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
        // Gestisci logica per autoSave: se i valori dei giroscopi sono coerenti con un utente fermo (std deviation?) li salva da solo. Così può essere chiamato mentre è in corso la sessione, e se un utnte è fermo (quindi gli ultimi 50?100?200? valori sono coerenti) salviamo la calibrazione dei giroscopi. Per gli accelerometri meglio di no percHé non possiamo sapere se è in piano.
    }

    public void Save()
    {
        _logger.StopLogging();
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
    }

    public void Reset()
    {
        OffsetL = null;
        OffsetR = null;
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