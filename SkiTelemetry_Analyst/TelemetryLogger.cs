using SkiTelemetry_Core;
[System.Runtime.Versioning.SupportedOSPlatform("ios15.0")]
[System.Runtime.Versioning.SupportedOSPlatform("maccatalyst15.0")]
[System.Runtime.Versioning.SupportedOSPlatform("android21.0")]
[System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]

public class TelemetryLogger : IDisposable
{
    private FileStream? _fileStream;
    private BinaryWriter? _writer;
    public bool isLogging = false;

    public void StartLogging()
    {
        string baseDir = FileSystem.Current.AppDataDirectory;
        if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
        string folder = Path.Combine(baseDir, "Logs");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        string filePath = GenerateProgressiveFileName(folder);
        _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        _writer = new BinaryWriter(_fileStream);
        isLogging = true;

        System.Diagnostics.Debug.WriteLine($"[LOGGER] File binario creato: {filePath}");
    }

    public void LogData(SkiDataPoint point)
    {
        if (!isLogging || _writer == null) return;

        // Scrittura binaria compatta (Esempio struttura: 104 byte per riga)
        // Timestamp (4) + Side (1) + Acc(4*3) + Gyro(4*3) + Roll(4) -> ripetuto per Left e Right
        WritePoint(point);
    }

    private void WritePoint(SkiDataPoint p)
    {
        if (_writer == null) return;

        //System.Diagnostics.Debug.WriteLine($"[LOGGER] Logging point: {p}");

        _writer.Write((uint)p.Timestamp); // 4 byte
        _writer.Write((byte)p.Side);      // 1 byte
        _writer.Write((float)p.AccX);     // 4 byte
        _writer.Write((float)p.AccY);     // 4 byte
        _writer.Write((float)p.AccZ);     // 4 byte
        _writer.Write((float)p.GyroX);    // 4 byte
        _writer.Write((float)p.GyroY);    // 4 byte
        _writer.Write((float)p.GyroZ);    // 4 byte
    }

    public void StopLogging()
    {
        if (!isLogging) return;
        isLogging = false;
        _writer?.Flush();
        _writer?.Close();
        _fileStream?.Close();
        _writer = null;
        _fileStream = null;
    }

    private string GenerateProgressiveFileName(string folder)
    {
        string dateToday = DateTime.Now.ToString("yyyy_MM_dd");

        // 1. Recupera la data dell'ultima RUN registrata
        string lastRunDate = Preferences.Default.Get("LastRunDate", "");
        int lastRunNumber = Preferences.Default.Get("LastRunNumber", 0);

        int currentRunNumber;

        // 2. Controllo: se la data salvata è diversa da oggi, resetta il contatore
        if (lastRunDate != dateToday)
        {
            currentRunNumber = 1;
        }
        else
        {
            // Se è lo stesso giorno, incrementa il numero precedente
            currentRunNumber = lastRunNumber + 1;
        }

        // 3. Aggiorna le Preferences per la prossima chiamata
        Preferences.Default.Set("LastRunDate", dateToday);
        Preferences.Default.Set("LastRunNumber", currentRunNumber);

        return Path.Combine(folder, $"{dateToday}_RUN_{currentRunNumber}.bin");
    }
    public void Dispose() => StopLogging();
}