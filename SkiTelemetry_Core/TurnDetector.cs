using SkiTelemetry_Core;

public class TurnDetector
{
    public event EventHandler<SkiTurn>? OnTurnCompleted;
    // Metriche che vogliamo calcolare (settate da fuori)
    public HashSet<MonitorMode> RequestedMetrics { get; } = new();

    private enum State { Searching, PendingExit }
    private int _lowSignalCounter = 0;
    private const int MIN_SILENCE_SAMPLES = 10;
    private double _minThreshold = 8;

    // Buffer per il passato (Pre-Trigger): contiene circa 400ms di storia

    private List<SkiFrame> _currentAccumulator = new List<SkiFrame>();


    public void ProcessFrame(SkiFrame frame)
    {
        double val = frame.CombinedGyro ?? 0;
        double absVal = Math.Abs(val);

        if (absVal < _minThreshold)
        {
            if (_currentAccumulator.Count > 0)
            {
                _lowSignalCounter++;
                // Aggiungiamo comunque il frame per non avere buchi nel grafico della curva
                _currentAccumulator.Add(frame);

                if (_lowSignalCounter >= MIN_SILENCE_SAMPLES)
                {
                    FinalizeTurn();
                }
            }
            return;
        }

        // Segnale forte: resettiamo il counter del silenzio
        _lowSignalCounter = 0;

        if (_currentAccumulator.Count > 0)
        {
            // Rimuovere rumore iniziale su Combette
            // Su finale 1 Indagare perché apra l'ultima curva visto che il segno non è cambiato rispetto alla curva precedente
            double startSign = Math.Sign(_currentAccumulator[0].CombinedGyro ?? 0);
            if (Math.Sign(val) != startSign)
            {
                FinalizeTurn();
            }
        }

        _currentAccumulator.Add(frame);
    }

    private void FinalizeTurn()
    {
        if (_currentAccumulator.Count > 0)
        {
            // Calcoliamo il picco massimo della pancia prima di decidere se è una curva
            double maxAbs = _currentAccumulator.Max(f => Math.Abs(f.CombinedGyro ?? 0));

            // SOGLIA DI DIGNITÀ: Se la pancia non arriva almeno a 25 deg/s, 
            // è rumore, non è una curva.
            if (maxAbs > 15 && _currentAccumulator.Count > 15)
            {
                AnalyzeAndEmitTurn(new List<SkiFrame>(_currentAccumulator));
            }
        }
        _currentAccumulator.Clear();
        _lowSignalCounter = 0;
    }

    private void AnalyzeAndEmitTurn(List<SkiFrame> turnData)
    {
        // turnData è la lista di frame tra due passaggi per lo zero
        if (turnData.Count < 10) return;

        // 1. Trova il frame con il valore massimo (assoluto)
        // Uso il nome che hai usato tu nei log: CombinedGyro
        var peakFrame = turnData[0];
        double maxAbs = 0;

        foreach (var f in turnData)
        {
            double currentAbs = Math.Abs(f.CombinedGyro ?? 0);
            if (currentAbs > maxAbs)
            {
                maxAbs = currentAbs;
                peakFrame = f;
            }
        }

        // 2. Crea il turno usando i timestamp dei DataPoint originali
        // (Assumo che SkiFrame abbia i riferimenti Left/Right come mostrato all'inizio)
        var turn = new SkiTurn
        {
            StartTimestamp = turnData.First().Left.Timestamp,
            EndTimestamp = turnData.Last().Left.Timestamp,
        };
        turn.EdgeAngle = SkiTurnAnalyzer.CalculateMaxEdgeAngle(turnData);
        turn.EdgeSimilarity = SkiTurnAnalyzer.CalculateSimilarity(turnData);
        turn.EarlyEdging = 0;
        turn.GForce = SkiTurnAnalyzer.CalculateMaxGForce(turnData); ;
        turn.Radius = SkiTurnAnalyzer.CalculateEstimatedRadius(turnData);
        turn.Shape = 0;
        turn.ProgressiveEdgeBuild = 0;
        turn.CarvingIndex = SkiTurnAnalyzer.CalculateCarvingIndex(turnData);
        turn.Foot = SkiTurnAnalyzer.DetermineOuterFoot(turnData);
        // 3. Calcolo basilare del piede (se positivo è un lato, se negativo è l'altro)
        // turn.Foot = turn.MaxGyro > 0 ? Foot.Right : Foot.Left;

        System.Diagnostics.Debug.WriteLine($"[OK] Curva: {turn.StartTimestamp} -> {turn.EndTimestamp}");

        OnTurnCompleted?.Invoke(this, turn);
    }

}