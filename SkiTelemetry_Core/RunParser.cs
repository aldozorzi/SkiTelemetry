namespace SkiTelemetry_Core;

public static class RunParser
{
    public static List<SkiFrame> ParseBinFile(string filePath)
    {
        var dataPoints = new List<SkiFrame>();

        if (!File.Exists(filePath)) return dataPoints;
        const int bytesPerRecord = 52;
        using (var stream = File.OpenRead(filePath))
        using (var reader = new BinaryReader(stream))
        {
            while (stream.Position + bytesPerRecord < stream.Length)
            {
                var ts = reader.ReadUInt32();
                var left = new SkiDataPoint
                {
                    Timestamp = ts,
                    Side = SensorSide.Left,
                    AccX = reader.ReadSingle(),
                    AccY = reader.ReadSingle(),
                    AccZ = reader.ReadSingle(),
                    GyroX = reader.ReadSingle(),
                    GyroY = reader.ReadSingle(),
                    GyroZ = reader.ReadSingle()
                };
                var right = new SkiDataPoint
                {
                    Timestamp = ts,
                    Side = SensorSide.Right,
                    AccX = reader.ReadSingle(),
                    AccY = reader.ReadSingle(),
                    AccZ = reader.ReadSingle(),
                    GyroX = reader.ReadSingle(),
                    GyroY = reader.ReadSingle(),
                    GyroZ = reader.ReadSingle()
                };

                dataPoints.Add(new SkiFrame(left,right));
            }
        }

        return dataPoints;
    }
}