using Microsoft.EntityFrameworkCore;
using SkiTelemetry_Core;
using SkiTelemetry_Web.Data;

namespace SkiTelemetry_Web.Services;

public class RunService
{
    private readonly SkiDbContext _db;
    private readonly string _storagePath = Path.Combine(Directory.GetCurrentDirectory(), "DataStorage", "Runs");

    public RunService(SkiDbContext db)
    {
        _db = db;
        if (!Directory.Exists(_storagePath)) Directory.CreateDirectory(_storagePath);
    }

    public async Task<List<SkiRun>> GetAllRunsAsync()
    {
        // Recuperiamo l'elenco ordinato per data più recente
        return await _db.Runs
            .Include(r => r.Athlete)
            .OrderByDescending(r => r.UploadDate)
            .ToListAsync();
    }

    public async Task<(SkiRun? Run, List<SkiDataPoint> Telemetry)> GetRunDetailsBinaryAsync(int id)
    {
        var run = await _db.Runs.Include(r => r.Athlete).FirstOrDefaultAsync(r => r.Id == id);
        if (run == null) return (null, new List<SkiDataPoint>());

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "DataStorage", "Runs", run.FileName);
        if (!File.Exists(filePath)) return (run, new List<SkiDataPoint>());

        var telemetry = new List<SkiDataPoint>();

        // Leggiamo il file binario
        using (var stream = File.OpenRead(filePath))
        using (var reader = new BinaryReader(stream))
        {
            while (stream.Position < stream.Length)
            {
                // Esempio di mappatura: deve combaciare ESATTAMENTE con come l'app Android scrive il file
                uint ts = reader.ReadUInt32();
                byte side = reader.ReadByte();
                float ax = reader.ReadSingle();
                float ay = reader.ReadSingle();
                float az = reader.ReadSingle();
                float gx = reader.ReadSingle();
                float gy = reader.ReadSingle();
                float gz = reader.ReadSingle();
                //float roll = reader.ReadSingle();
                var data = new SkiDataPoint
                {
                    Timestamp = ts,
                    Side = (SensorSide)side,
                    AccX = ax, // ReadSingle legge un float (4 byte)
                    AccY = ay,
                    AccZ = az,
                    GyroX = gx,
                    GyroY = gy,
                    GyroZ = gz,
                };
                telemetry.Add(data);
            }
        }

        return (run, telemetry);
    }
}