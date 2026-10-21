using SkiTelemetry_Core;

public class TurnDetector
{
    public event EventHandler<SkiTurn>? OnTurnCompleted;
    public HashSet<MonitorMode> RequestedMetrics { get; } = new();

    private int _lowSignalCounter = 0;
    private const int MIN_SILENCE_SAMPLES = 5;
    private double _minThreshold = 8;

    private List<SkiFrame> _currentAccumulator = new List<SkiFrame>();
    private double _prevTurnSign = 0;

    private uint _turnCounter = 0;


    public void ProcessFrame(SkiFrame frame)
    {
        double val = frame.CombinedGyro;
        double absVal = Math.Abs(val);
        //double startSign = Math.Sign(_currentAccumulator[0].CombinedGyro);

        if (absVal < _minThreshold)
        {
            if (_currentAccumulator.Count > 0)
            {
                _lowSignalCounter++;

                if (_lowSignalCounter >= MIN_SILENCE_SAMPLES)
                {
                    FinalizeTurn();
                }
            }
            return;
        }

        // Segnale forte: resettiamo il counter del silenzio
        _lowSignalCounter = 0;

        if (_currentAccumulator.Count > 0 && Math.Sign(val) != _prevTurnSign)
        {
            //System.Diagnostics.Debug.WriteLine($"[DETECTOR] {frame.Left.Timestamp} Sign has changed, now {Math.Sign(val)}, before {_prevTurnSign}");
            FinalizeTurn();
        }

        _prevTurnSign = Math.Sign(val);
        //System.Diagnostics.Debug.WriteLine($"[DETECTOR] {frame.Left.Timestamp} Building Turn: {absVal}");
        _currentAccumulator.Add(frame);
    }

    public void Reset()
    {
        _currentAccumulator.Clear();
        _lowSignalCounter = 0;
        _prevTurnSign = 0;
        _turnCounter = 0;
    }

    private void FinalizeTurn()
    {
        if (_currentAccumulator.Count > 0)
        {
            // Calcoliamo il picco massimo della pancia prima di decidere se è una curva
            //System.Diagnostics.Debug.WriteLine($"[DETECTOR] CalculateMaxEdgeAngle (on {_currentAccumulator.Count} frames)");
            double maxEdge = SkiTurnAnalyzer.CalculateMaxEdgeAngle(_currentAccumulator);
            double duration = (_currentAccumulator.Last().Left.Timestamp - _currentAccumulator.First().Left.Timestamp) / 1000;

            // Prendo un frame all'interno della curva per avere un segno pulito
            int mid = _currentAccumulator.Count / 2;
            _prevTurnSign = Math.Sign(_currentAccumulator[mid].CombinedGyro);

            // SOGLIA DI DIGNITÀ: Se la pancia non arriva almeno a 15 deg/s, 
            // è rumore, non è una curva.
            //System.Diagnostics.Debug.WriteLine($"[DETECTOR] Checking ({maxAbs} > 15 && {_currentAccumulator.Count} > 15");
            if (maxEdge >= 10 && duration > 0.8 &&  _currentAccumulator.Count > 15)
            {
                //System.Diagnostics.Debug.WriteLine($"[DETECTOR] Checking CombinedGyro: ({maxAbs} )");
                AnalyzeAndEmitTurn(new List<SkiFrame>(_currentAccumulator));
            }
        }
        _currentAccumulator.Clear();
        _lowSignalCounter = 0;
    }

    private void AnalyzeAndEmitTurn(List<SkiFrame> turnData)
    {
        _turnCounter++;
        //System.Diagnostics.Debug.WriteLine($"[DETECTOR ]CURVA [{_turnCounter}] Frames:[{turnData.Count}]--------------------------------------------------");
        // 2. Crea la curva usando i timestamp dei DataPoint originali
        // (Assumo che SkiFrame abbia i riferimenti Left/Right come mostrato all'inizio)
        var turn = new SkiTurn
        {
            StartTimestamp = turnData.First().Left.Timestamp,
            EndTimestamp = turnData.Last().Left.Timestamp,
        };
        turn.Index = _turnCounter;
        turn.Shape = 0;
        SkiTurnAnalyzer.Populate(turnData, turn, RequestedMetrics);
        // 3. Calcolo basilare del piede (se positivo è un lato, se negativo è l'altro)
        // turn.Foot = turn.MaxGyro > 0 ? Foot.Right : Foot.Left;
        // SkiTurnAnalyzer.DiagnosticTurnAnalysis(turnData);
        //System.Diagnostics.Debug.WriteLine($"[DETECTOR] Curva: {turn.StartTimestamp} -> {turn.EndTimestamp} - CarvingIndex: {turn.PerformanceIndex} - EdgeAngle:{turn.EdgeAngle} - GForce:{turn.GForce}");
        OnTurnCompleted?.Invoke(this, turn);
        //System.Diagnostics.Debug.WriteLine($"-----------------------------------------------------------");
    }

}