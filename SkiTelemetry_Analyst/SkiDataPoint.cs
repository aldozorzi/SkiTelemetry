using System;
using System.Collections.Generic;
using System.Text;

namespace SkiTelemetry_Analyst
{
  

    public class SkiDataPoint
    {
        public SensorSide Side { get; set; }
        public uint Timestamp { get; set; } // Millisecondi dall'avvio
        public float AccX { get; set; }
        public float AccY { get; set; }
        public float AccZ { get; set; }
        public float GyroX { get; set; }
        public float GyroY { get; set; }
        public float GyroZ { get; set; }

        // Calcoliamo l'inclinazione (Roll) semplificata per ora
        public double rawRoll => Math.Atan2(AccY, AccZ) * (180 / Math.PI);
        public double Roll { get; set; } // Dato filtrato

        public override string ToString()
        {
            return $"[DATA] Side: {Side}, Roll: {Roll:0.0}, Acc:({AccX:0.0},{AccY:0.0},{AccZ:0.0})";
        }
    }
}
