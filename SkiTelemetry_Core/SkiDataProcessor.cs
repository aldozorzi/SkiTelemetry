namespace SkiTelemetry_Core
{
    [System.Runtime.Versioning.SupportedOSPlatform("ios15.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("maccatalyst15.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("android21.0")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763.0")]
    public class SkiDataProcessor
    {
        public event EventHandler<SkiTurn>? OnNewTurn;

        /*private double _lastLeftRoll = 0;
        private double _lastRightRoll = 0;
        private const double Alpha = 0.2;*/

        private readonly TurnDetector _turnDetector = new();
        private SkiDataPoint? _rightDataPoint;
        private SkiDataPoint? _leftDataPoint;

        

        public SkiDataProcessor()
        {
            _turnDetector.OnTurnCompleted += OnTurnCompleted;
        }

        public void Process(SkiDataPoint point)
        {
            ApplySmoothing(point);
            
            // System.Diagnostics.Debug.WriteLine(point.ToString());

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

            

            // Aggiungiamo la metrica attualmente selezionata nella UI
           
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

        

        private void ApplySmoothing(SkiDataPoint point)
        {
            /*double currentRaw = point.rawRoll;
            if (point.Side == SensorSide.Left)
            {
                _lastLeftRoll = (Alpha * currentRaw) + ((1 - Alpha) * _lastLeftRoll);
                point.Roll = _lastLeftRoll;
            }
            else
            {
                _lastRightRoll = (Alpha * currentRaw) + ((1 - Alpha) * _lastRightRoll);
                point.Roll = _lastRightRoll;
            }*/
        }


       
    }
}
