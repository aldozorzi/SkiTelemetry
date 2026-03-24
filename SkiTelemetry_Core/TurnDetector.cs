using SkiTelemetry_Core;

public class TurnDetector
{
    public event EventHandler<SkiTurn>? OnTurnCompleted;
    // Metriche che vogliamo calcolare (settate da fuori)
    public HashSet<MonitorMode> RequestedMetrics { get; } = new();

    private enum State { Searching, PendingExit }
    private State _currentState = State.Searching;

    // Buffer per il passato (Pre-Trigger): contiene circa 400ms di storia
    private List<SkiFrame> _historyBuffer = new List<SkiFrame>();
    private List<SkiFrame> _currentTurnSamples = new List<SkiFrame>();

    private double _lastValidSign = 0;
    private double _pendingPeakValue = 0;
    private const int HistorySize = 60; // 400ms a 50Hz per catturare l'inizio curva

    public void ProcessFrame(SkiDataPoint left, SkiDataPoint right)
    {
        var frame = new SkiFrame(left, right);
        double val = frame.CombinedGyroZ ?? 0;
        double absVal = Math.Abs(val);

        _historyBuffer.Add(frame);
        if (_historyBuffer.Count > HistorySize) _historyBuffer.RemoveAt(0);

        switch (_currentState)
        {
            case State.Searching:
                // 1. Cerchiamo un picco locale nel buffer di storia
                if (IsLocalPeak(out double peakVal))
                {
                    double currentSign = Math.Sign(peakVal);
                    // 2. Validazione: segno alternato e magnitudo minima
                    if (currentSign != _lastValidSign && Math.Abs(peakVal) > 30)
                    {
                        _currentState = State.PendingExit;
                        _pendingPeakValue = peakVal;
                        _currentTurnSamples.Clear();

                        // 3. RECUPERO INIZIO: Prendiamo tutta la storia precedente al picco
                        // che appartiene alla stessa "collina" (stesso segno)
                        _currentTurnSamples.AddRange(_historyBuffer.Where(f => Math.Sign(f.CombinedGyroZ ?? 0) == currentSign || Math.Abs(f.CombinedGyroZ ?? 0) < 5));
                    }
                }
                break;

            case State.PendingExit:
                _currentTurnSamples.Add(frame);

                // 4. RECUPERO FINE: Usciamo quando il segnale attraversa lo zero o torna piatto
                if (absVal < 8.0 || Math.Sign(val) != Math.Sign(_pendingPeakValue))
                {
                    FinalizeTurn();
                    _currentState = State.Searching;
                }
                break;
        }
    }

    private bool IsLocalPeak(out double peakVal)
    {
        peakVal = 0;
        if (_historyBuffer.Count < 11) return false;

        int mid = _historyBuffer.Count / 2;
        double midVal = Math.Abs(_historyBuffer[mid].CombinedGyroZ ?? 0);

        // Verifica che il centro sia il massimo assoluto della finestra attuale
        for (int i = 0; i < _historyBuffer.Count; i++)
        {
            if (i == mid) continue;
            if (Math.Abs(_historyBuffer[i].CombinedGyroZ ?? 0) >= midVal) return false;
        }

        peakVal = _historyBuffer[mid].CombinedGyroZ ?? 0;
        return true;
    }

    private void FinalizeTurn()
    {
        TrimSamples(); // Pulizia bordi < 10 deg/s

        if (_currentTurnSamples.Count < 20) return;

        double peakVal = Math.Abs(_pendingPeakValue);
        double energy = _currentTurnSamples.Sum(f => Math.Abs(f.CombinedGyroZ ?? 0));
        double averageVal = _currentTurnSamples.Average(f => Math.Abs(f.CombinedGyroZ ?? 0));
        double shapeRatio = peakVal / averageVal;

        // LOGICA DINAMICA:
        // Se non abbiamo ancora trovato la prima curva, siamo severissimi (Anti-Ghost).
        // Una volta partiti, abbassiamo la guardia per non perdere le curve blande.
        bool isFirstTurn = _lastValidSign == 0;

        double minEnergy = isFirstTurn ? 1500 : 800; // Recupera le curve "leggere" medie
        double minShape = isFirstTurn ? 1.8 : 1.4;   // Accetta forme meno perfette una volta in movimento
        double minPeak = isFirstTurn ? 45 : 25;      // Accetta picchi più bassi nella run blanda

        if (peakVal > minPeak && energy > minEnergy && shapeRatio > minShape)
        {
            _lastValidSign = Math.Sign(_pendingPeakValue);
            AnalyzeTurn(new List<SkiFrame>(_currentTurnSamples));
            System.Diagnostics.Debug.WriteLine($"[OK] Validata: E={energy:F0}, S={shapeRatio:F1}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[REJECT] E={energy:F0}, S={shapeRatio:F1}");
        }
    }

    private void TrimSamples()
    {
        // Rimuove il "silenzio" ai bordi per centrare il rettangolo sulla parte attiva
        while (_currentTurnSamples.Count > 0 && Math.Abs(_currentTurnSamples.First().CombinedGyroZ ?? 0) < 10)
            _currentTurnSamples.RemoveAt(0);

        while (_currentTurnSamples.Count > 0 && Math.Abs(_currentTurnSamples.Last().CombinedGyroZ ?? 0) < 10)
            _currentTurnSamples.RemoveAt(_currentTurnSamples.Count - 1);
    }

    private void AnalyzeTurn(List<SkiFrame> turnData)
    {
        // ESEMPIO DI COSA PUOI FARE ORA:
        var t0Frame = turnData.FirstOrDefault(f => Math.Abs(f.CombinedGyroZ ?? 0) > 5.0);
        uint startTime = t0Frame?.Left?.Timestamp ?? turnData[0].Left.Timestamp;
        uint endTime = turnData.Last().Left.Timestamp;


        var turn = new SkiTurn();
        turn.EdgeAngle = SkiTurnAnalyzer.CalculateMaxEdgeAngle(turnData);
        turn.EdgeSimilarity = SkiTurnAnalyzer.CalculateSimilarity(turnData);
        turn.EarlyEdging = 0;
        turn.GForce = SkiTurnAnalyzer.CalculateMaxGForce(turnData); ;
        turn.Radius = SkiTurnAnalyzer.CalculateEstimatedRadius(turnData);
        turn.Shape = 0;
        turn.ProgressiveEdgeBuild = 0;
        turn.CarvingIndex = SkiTurnAnalyzer.CalculateCarvingIndex(turnData);
        turn.Foot = SkiTurnAnalyzer.DetermineOuterFoot(turnData);
        turn.StartTimestamp = startTime;
        turn.EndTimestamp = endTime;
        OnTurnCompleted?.Invoke(this, turn);

        System.Diagnostics.Debug.WriteLine($"[DETECTOR] Curva analizzata: {startTime}-{endTime}");
    }

}