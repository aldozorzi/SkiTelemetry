using SkiTelemetry_Analyst;
using System.Text;

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
        //string folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string folder = Path.Combine(FileSystem.Current.AppDataDirectory, "Logs");
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
        _writer.Write((uint)p.Timestamp); // 4 byte
        _writer.Write((byte)p.Side);      // 1 byte
        _writer.Write((float)p.AccX);     // 4 byte
        _writer.Write((float)p.AccY);     // 4 byte
        _writer.Write((float)p.AccZ);     // 4 byte
        _writer.Write((float)p.GyroX);    // 4 byte
        _writer.Write((float)p.GyroY);    // 4 byte
        _writer.Write((float)p.GyroZ);    // 4 byte
        _writer.Write((float)p.rawRoll);  // 4 byte
    }

    public void StopLogging()
    {
        isLogging = false;
        _writer?.Flush();
        _writer?.Close();
        _fileStream?.Close();
        _writer = null;
        _fileStream = null;
    }

    private string GenerateProgressiveFileName(string folder)
    {
        string datePart = DateTime.Now.ToString("yyyy_MM_dd");
        int runNumber = 1;

        // Recupera i file esistenti per trovare l'ultimo numero
        var files = Directory.GetFiles(folder, $"{datePart}_RUN_*.bin");
        if (files.Length > 0)
        {
            runNumber = files
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Select(name => name.Split('_').Last())
                .Select(num => int.TryParse(num, out int n) ? n : 0)
                .Max() + 1;
        }
        return Path.Combine(folder, $"{datePart}_RUN_{runNumber}.bin");
    }
    public void Dispose() => StopLogging();
}