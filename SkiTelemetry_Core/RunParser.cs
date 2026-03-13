namespace SkiTelemetry_Core;

public static class RunParser
{
    public static List<SkiDataPoint> ParseBinFile(string filePath)
    {
        var dataPoints = new List<SkiDataPoint>();

        if (!File.Exists(filePath)) return dataPoints;

        using (var stream = File.OpenRead(filePath))
        using (var reader = new BinaryReader(stream))
        {
            while (stream.Position < stream.Length)
            {
                dataPoints.Add(new SkiDataPoint
                {
                    Timestamp = reader.ReadUInt32(),
                    Side = (SensorSide)reader.ReadByte(),
                    AccX = reader.ReadSingle(),
                    AccY = reader.ReadSingle(),
                    AccZ = reader.ReadSingle(),
                    GyroX = reader.ReadSingle(),
                    GyroY = reader.ReadSingle(),
                    GyroZ = reader.ReadSingle()
                });
            }
        }

        return dataPoints;
    }
}