using SkiTelemetry_Core;

namespace SkiTelemetry_Analyst.Services;

public class CalibrationService
{
    private static readonly Lazy<CalibrationService> _instance = new(() => new CalibrationService());
    private const string LastCalibDateKey = "LastCalibrationDate";
    public static CalibrationService Instance => _instance.Value;

    public double OffsetL { get; private set; }
    public double OffsetR { get; private set; }

    private CalibrationService()
    {
        /*OffsetL = Preferences.Default.Get("CalibL", 0.0);
        OffsetR = Preferences.Default.Get("CalibR", 0.0);*/
        OffsetL = 0;
        OffsetR = 0;
    }

    public void Save(double l, double r)
    {
        OffsetL = l;
        OffsetR = r;
        /*Preferences.Default.Set("CalibL", l);
        Preferences.Default.Set("CalibR", r);
        Preferences.Default.Set(LastCalibDateKey, DateTime.Today.ToString("yyyy-MM-dd"));*/
        IsCalibratedToday = true;
    }

    public void Reset()
    {
        OffsetL = 0;
        OffsetR = 0;
        Preferences.Default.Remove("CalibL");
        Preferences.Default.Remove("CalibR");
        Preferences.Default.Remove(LastCalibDateKey);
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
    public CalibrationOffsets GetOffsets() => new CalibrationOffsets { RollL = OffsetL, RollR = OffsetR };
}