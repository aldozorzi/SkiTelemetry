using System.Drawing;

namespace SkiTelemetry_Core
{
    [System.Runtime.Versioning.SupportedOSPlatform("ios15.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("maccatalyst15.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("android21.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
    public class SkiDataProcessor
    {
        public event EventHandler<SkiTurn>? OnNewTurn;

        private readonly TurnDetector _turnDetector = new();
        private SkiDataPoint? _rightDataPoint;
        private SkiDataPoint? _leftDataPoint;

        // Stato della calibrazione
        private bool _isCalibrated = false;
        private int _sampleCounter = 0;
        private const int SamplesRequired = 60; // ~3 secondi a 20Hz
        private readonly SensorBias _leftBias = new();
        private readonly SensorBias _rightBias = new();
        private double _leftGyroStdDev = 0;
        private double _rightGyroStdDev = 0;
        private double _leftGyroGain = 0;
        private double _rightGyroGain = 0;
        private double _leftGain = 0;
        private double _rightGain = 0;
        private List<double> _leftSamples = new();
        private List<double> _rightSamples = new();


        public SkiDataProcessor()
        {
            _turnDetector.OnTurnCompleted += OnTurnCompleted;
        }

        private bool checkCalibration(SkiDataPoint left, SkiDataPoint right)
        {

            if (!_isCalibrated)
            {
                _leftBias.AccX += left.AccX;
                _leftBias.AccY += left.AccY;
                _leftBias.AccZ += left.AccZ;
                _leftBias.GyroY += left.GyroY;

                _rightBias.AccX += right.AccX;
                _rightBias.AccY += right.AccY;
                _rightBias.AccZ += right.AccZ;
                _rightBias.GyroY += right.GyroY;

                _leftSamples.Add(left.GyroY);
                _rightSamples.Add(right.GyroY);
                System.Diagnostics.Trace.WriteLine($"Adding Calibrating point: {left.ToString()} ");
                System.Diagnostics.Trace.WriteLine($"Adding Calibrating point: {right.ToString()} ");
                _sampleCounter++;

                if (_sampleCounter >= SamplesRequired) // Altri 3 secondi di campionamento statico
                {
                    _leftBias.AccX /= SamplesRequired;
                    _leftBias.AccY /= SamplesRequired;
                    _leftBias.AccZ /= SamplesRequired;
                    _leftBias.GyroY /= SamplesRequired;

                    _rightBias.AccX /= SamplesRequired;
                    _rightBias.AccY /= SamplesRequired;
                    _rightBias.AccZ /= SamplesRequired;
                    _rightBias.GyroY /= SamplesRequired;

                    double magL = Math.Sqrt(_leftBias.AccX * _leftBias.AccX + _leftBias.AccY * _leftBias.AccY + _leftBias.AccZ * _leftBias.AccZ);
                    double magR = Math.Sqrt(_rightBias.AccX * _rightBias.AccX + _rightBias.AccY * _rightBias.AccY + _rightBias.AccZ * _rightBias.AccZ);

                    _leftGain = 1.0 / magL;
                    _rightGain = 1.0 / magR;

                    double avgL = _leftSamples.Average();
                    double avgR = _rightSamples.Average();
                    double sumL = _leftSamples.Sum(d => Math.Pow(d - avgL, 2));
                    double sumR = _rightSamples.Sum(d => Math.Pow(d - avgR, 2));
                    _leftGyroStdDev = Math.Sqrt(sumL / SamplesRequired);
                    _rightGyroStdDev = Math.Sqrt(sumR / SamplesRequired);

                    _rightGyroGain = _leftGyroStdDev / _rightGyroStdDev;
                    _leftGyroGain = 1.0; // Usiamo il Left come riferimento


                    // System.Diagnostics.Trace.WriteLine($"[DataProcessor] _leftGain:{_leftGain},_rightGain:{_rightGain}");
                    _isCalibrated = true;
                    return true;
                }
                return false;
            }
            return true;
        }

        public void Process(SkiDataPoint point)
        {

            


            if (point.Side == SensorSide.Left)
            {
                _leftDataPoint = point; 
            }
            else if (point.Side == SensorSide.Right)
            {
                _rightDataPoint = point;
            }

            // 2. Controllo: se manca uno dei due, esci con null (SENZA RESETTARE)
            if (_leftDataPoint == null || _rightDataPoint == null) return;

            if (!checkCalibration(_leftDataPoint, _rightDataPoint))
            {
                return;
            }

            _leftDataPoint.AccX = (float)((_leftDataPoint.AccX - _leftBias.AccX) * _leftGain);
            _leftDataPoint.AccY = (float)(_leftDataPoint.AccY * _leftGain);
            _leftDataPoint.AccZ = (float)((_leftDataPoint.AccZ - _leftBias.AccZ) * _leftGain);
            _leftDataPoint.GyroY = (float)((_leftDataPoint.GyroY - _leftBias.GyroY) * _leftGyroGain);

            _rightDataPoint.AccX = (float)((_rightDataPoint.AccX - _rightBias.AccX) * _rightGain);
            _rightDataPoint.AccY = (float)(_rightDataPoint.AccY * _rightGain);
            _rightDataPoint.AccZ = (float)((_rightDataPoint.AccZ - _rightBias.AccZ) * _rightGain);
            _rightDataPoint.GyroY = (float)((_rightDataPoint.GyroY - _rightBias.GyroY) * _rightGyroGain);
            

            // Aggiungiamo la metrica attualmente selezionata nella UI
            //System.Diagnostics.Trace.WriteLine($"Adding point: {point.ToString()} ");
            _turnDetector.AddPoints(_leftDataPoint, _rightDataPoint);

            _leftDataPoint = null;
            _rightDataPoint = null;
        }

        public void setMetrics(HashSet<MonitorMode> metrics)
        {

            _turnDetector.RequestedMetrics.Clear();
            foreach (MonitorMode metric in metrics )
            {

                _turnDetector.RequestedMetrics.Add(metric);
            }
        }

        private void OnTurnCompleted(object? sender, SkiTurn turn)
        {
            OnNewTurn?.Invoke(this, turn);
        }



       
    }
}
