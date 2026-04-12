using System;
namespace SkiTelemetry_Core;
public class MadgwickAHRS
{
    // Il parametro magico: determina quanto ci fidiamo dell'accelerometro.
    // 0.1 è lo standard. Per lo sci, dove le forze G sono alte, potremmo 
    // scendere a 0.05 o 0.02.
    public double Beta { get; set; }
    public double SamplePeriod { get; set; }

    // Quaternione interno [w, x, y, z]
    public double q0 { get; private set; }
    public double q1 { get; private set; }
    public double q2 { get; private set; }
    public double q3 { get; private set; }

    public MadgwickAHRS(double samplePeriod, double beta = 0.05)
    {
        SamplePeriod = samplePeriod;
        Beta = beta;
        q0 = 1.0;
        q1 = 0.0;
        q2 = 0.0;
        q3 = 0.0;
    }

    // Passare Gyro in RADIANTI/SECONDO e Acc in qualsiasi unità (verrà normalizzato)
    public void Update(double gx, double gy, double gz, double ax, double ay, double az)
    {
        double recipNorm;
        double s0, s1, s2, s3;
        double qDot1, qDot2, qDot3, qDot4;
        double _2q0, _2q1, _2q2, _2q3, _4q0, _4q1, _4q2, _8q1, _8q2, q0q0, q1q1, q2q2, q3q3;

        // Rate of change of quaternion from gyroscope
        qDot1 = 0.5 * (-q1 * gx - q2 * gy - q3 * gz);
        qDot2 = 0.5 * (q0 * gx + q2 * gz - q3 * gy);
        qDot3 = 0.5 * (q0 * gy - q1 * gz + q3 * gx);
        qDot4 = 0.5 * (q0 * gz + q1 * gy - q2 * gx);

        // Compute feedback only if accelerometer measurement valid
        if (!((ax == 0.0) && (ay == 0.0) && (az == 0.0)))
        {
            // Normalise accelerometer measurement
            recipNorm = 1.0 / Math.Sqrt(ax * ax + ay * ay + az * az);
            ax *= recipNorm;
            ay *= recipNorm;
            az *= recipNorm;

            // Auxiliary variables to avoid repeated arithmetic
            _2q0 = 2.0 * q0;
            _2q1 = 2.0 * q1;
            _2q2 = 2.0 * q2;
            _2q3 = 2.0 * q3;
            _4q0 = 4.0 * q0;
            _4q1 = 4.0 * q1;
            _4q2 = 4.0 * q2;
            _8q1 = 8.0 * q1;
            _8q2 = 8.0 * q2;
            q0q0 = q0 * q0;
            q1q1 = q1 * q1;
            q2q2 = q2 * q2;
            q3q3 = q3 * q3;

            // Gradient decent algorithm corrective step
            s0 = _4q0 * q2q2 + _2q2 * ax + _4q0 * q1q1 - _2q1 * ay;
            s1 = _4q1 * q3q3 - _2q3 * ax + 4.0 * q0q0 * q1 - _2q0 * ay - _4q1 + _8q1 * q1q1 + _8q1 * q2q2 + _4q1 * az;
            s2 = 4.0 * q0q0 * q2 + _2q0 * ax + _4q2 * q3q3 - _2q3 * ay - _4q2 + _8q2 * q1q1 + _8q2 * q2q2 + _4q2 * az;
            s3 = 4.0 * q1q1 * q3 - _2q1 * ax + 4.0 * q2q2 * q3 - _2q2 * ay;

            recipNorm = 1.0 / Math.Sqrt(s0 * s0 + s1 * s1 + s2 * s2 + s3 * s3);
            s0 *= recipNorm;
            s1 *= recipNorm;
            s2 *= recipNorm;
            s3 *= recipNorm;

            // Apply feedback step
            qDot1 -= Beta * s0;
            qDot2 -= Beta * s1;
            qDot3 -= Beta * s2;
            qDot4 -= Beta * s3;
        }

        // Integrate rate of change and normalise
        q0 += qDot1 * SamplePeriod;
        q1 += qDot2 * SamplePeriod;
        q2 += qDot3 * SamplePeriod;
        q3 += qDot4 * SamplePeriod;

        recipNorm = 1.0 / Math.Sqrt(q0 * q0 + q1 * q1 + q2 * q2 + q3 * q3);
        q0 *= recipNorm;
        q1 *= recipNorm;
        q2 *= recipNorm;
        q3 *= recipNorm;
    }

    // Estrae gli angoli di Eulero (in Gradi)
    public double GetRoll()
    {
        return Math.Atan2(2.0 * (q0 * q1 + q2 * q3), 1.0 - 2.0 * (q1 * q1 + q2 * q2)) * (180.0 / Math.PI);
    }

    public double GetPitch()
    {
        return Math.Asin(Math.Clamp(2.0 * (q0 * q2 - q3 * q1), -1.0, 1.0)) * (180.0 / Math.PI);
    }

    public double GetYaw()
    {
        return Math.Atan2(2.0 * (q0 * q3 + q1 * q2), 1.0 - 2.0 * (q2 * q2 + q3 * q3)) * (180.0 / Math.PI);
    }
    public double GetDynamicG(double ax, double ay, double az)
    {
        // Passa gli stessi valori rimappati che dai all'Update
        // az qui è il tuo AccY originale (gravità)
        double gX = 2.0 * (q1 * q3 - q0 * q2);
        double gY = 2.0 * (q2 * q3 + q0 * q1);
        double gZ = q0 * q0 - q1 * q1 - q2 * q2 + q3 * q3;

        double dynX = ax - gX;
        double dynY = ay - gY;
        double dynZ = az - gZ;

        return Math.Sqrt(dynX * dynX + dynY * dynY + dynZ * dynZ);
    }
    public double GetM5StickRoll()
    {
        // Calcola l'angolo attorno all'asse Z (il tuo Roll) usando il quaternione
        return -GetPitch();
    }
}