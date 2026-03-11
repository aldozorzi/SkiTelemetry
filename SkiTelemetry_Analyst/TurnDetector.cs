namespace SkiTelemetry_Analyst
{
    public class TurnDetector
    {
        // Evento per notificare la fine di una curva
        public event EventHandler<SkiTurn>? OnTurnCompleted;

        // Metriche che vogliamo calcolare (settate da fuori)
        public HashSet<MonitorMode> RequestedMetrics { get; } = new();

        // VARIABILI PER L'OUTPUT
        private double? _turnEdgeAngle { get; set; }
        private double? _turnEdgeSimilarity { get; set; }
        private double? _turnEarlyEdging { get; set; }
        private double? _turnGForce { get; set; }
        private double? _turnRadius { get; set; }
        private double? _turnShape { get; set; }
        private double? _turnProgressiveEdgeBuild { get; set; }
        private double? _turnCarvingIndex { get; set; }

        // EDGE ANGLE PEAK
        private double _maxRollInCurrentTurn = 0;
        private bool _isCurrentlyTurning = false;
        private bool _wasTurningBefore = false;
        private bool _isCoolingDown = false;


        // EDGE SIMILARITY
        private double _accumulatedDiff = 0;
        private int _sampleCount = 0;
        private const double _maxAcceptableDiff = 15.0;

        // G-FORCE
        private double _maxGForceInCurrentTurn = 0;

        // TURN RADIUS
        private double _accumulatedAccY = 0;
        private double _accumulatedGyroZ = 0;
        private int _radiusSampleCount = 0;

        // EARLY EDGING
        private uint _turnStartTime = 0;
        private bool _thresholdReached = false;
        private const double _earlyEdgingExcellentTime = 500; // Sotto i 250ms è un 100 pieno (Fulmineo)
        private const double _earlyEdgingMaxAcceptableTime = 1500; // Oltre 1 secondo è uno 0 (Ritardato)

        // PROGRESSIVE BUILD (Versione conteggio campioni)
        private int _positiveSamples = 0;
        private int _totalSamples = 0;
        private double _prevRollInTurn = 0;
        private Queue<double> _rollHistory = new Queue<double>();
        private const int WindowSize = 5;


        // TURN SHAPE
        // Struttura per memorizzare l'evoluzione della curva
        private List<double> _turnRadiusHistory = new List<double>();

        // CARVING INDEX
        private double _accumulatedCarvingDeviation = 0;
        private int _carvingSampleCount = 0;
        private double _targetCarvingRatio = 1.35; // Inizializziamo con il valore teorico basato sulla fisica

        // Soglie personalizzabili
        private const double StartThreshold = 10; // Gradi minimi per considerare l'inizio di un turn
        private const double GapToConfirmPeak = 3.0; // Quanti gradi devi "risalire" per confermare il picco 
        private const double ExitThreshold = 6; // Valore massimo per l'uscita forzata da una cruva, qualora venisse "saltato" il calcolo StartThreshold-GapToConfirmPeak
        private const double EarlyEdgingStartThresholdDelta = 5; // Gradi minimi per considerare l'attivazione dell'earlyedging rispetto a StartThreshold (che definisce l'inizio curva)
        private const double ProgressiveBuildPerfectionTolerance = 0.90; // Un risultato di ProgressiveBuildPerfectionTolerance*100 è considerato 100 (es. 0.90->90% = 100%)

        public void AddPoints(SkiDataPoint left, SkiDataPoint right)
        {
            double leftRoll = left.Roll;
            double rightRoll = right.Roll;
            double absRoll = Math.Max(Math.Abs(leftRoll), Math.Abs(rightRoll));
            _wasTurningBefore = _isCurrentlyTurning;
            UpdateTurnState(absRoll, left.Timestamp);

            if (RequestedMetrics.Contains(MonitorMode.EdgeAngle))
            {
                double? result = GetTurnAngle(left, right);
                if (result.HasValue)
                {
                    _turnEdgeAngle = result;
                }
            }
            if (RequestedMetrics.Contains(MonitorMode.EdgeSimilarity))
            {
                double? result = GetTurnSimilarity(left, right);
                if (result.HasValue)
                {
                     _turnEdgeSimilarity = result;
                }
            }
            if (RequestedMetrics.Contains(MonitorMode.EarlyEdging))
            {
                double? result = GetTurnEarlyEdging(left, right);
                if (result.HasValue){
                    _turnEarlyEdging = result;
                }  
            }
            if (RequestedMetrics.Contains(MonitorMode.GForce))
            {
                double? result = GetTurnGForce(left, right);
                if (result.HasValue)
                {
                    _turnGForce = result;
                }
            }
            if (RequestedMetrics.Contains(MonitorMode.TurnRadius))
            {
                double? result = GetTurnRadius(left, right);
                if (result.HasValue)
                {
                    _turnRadius = result;
                }
            }
            if (RequestedMetrics.Contains(MonitorMode.TurnShape))
            {
                double? result = GetTurnShape(left, right);
                if (result.HasValue)
                {
                    _turnShape = result;
                }                
            }
            if (RequestedMetrics.Contains(MonitorMode.ProgressiveEdgeBuild))
            {
                double? result = GetTurnProgressiveEdgeBuild(left, right);
                if (result.HasValue)
                {
                     _turnProgressiveEdgeBuild = result;
                }               
            }
            if (RequestedMetrics.Contains(MonitorMode.CarvingIndex))
            {
                double? result = GetTurnCarvingIndex(left, right);
                if (result.HasValue)
                {
                    _turnCarvingIndex = result;
                }
            }




            if (_isCurrentlyTurning && !_wasTurningBefore)
            {
                // inizio curva
            }
            
            
            if(_isCurrentlyTurning)
            {
                // prosecuzione curva
            }
            else if (!_isCurrentlyTurning && _wasTurningBefore)
            {
                // fine curva
                
                var turn = new SkiTurn();
                turn.EdgeAngle = _turnEdgeAngle;
                turn.EdgeSimilarity = _turnEdgeSimilarity;
                turn.EarlyEdging = _turnEarlyEdging;
                turn.GForce = _turnGForce;
                turn.Radius = _turnRadius;
                turn.Shape = _turnShape;
                turn.ProgressiveEdgeBuild = _turnProgressiveEdgeBuild;
                turn.CarvingIndex = _turnCarvingIndex;
                System.Diagnostics.Debug.WriteLine($"[DETECTOR] Sending Turn: {turn}");
                ResetMetrics(); // Pulizia immediata dopo il calcolo finale
                OnTurnCompleted?.Invoke(this, turn);
            }
        }

        public double? GetTurnAngle(SkiDataPoint left, SkiDataPoint right)
        {
           if (!_isCurrentlyTurning &&  _wasTurningBefore)
            {    
                double result = _maxRollInCurrentTurn;
                return result;
            }

            return null;
        }
        public double? GetTurnSimilarity(SkiDataPoint left, SkiDataPoint right)
        {
            double leftRoll = left.Roll;
            double rightRoll = right.Roll;


            if (_isCurrentlyTurning)
            {
                // Accumulo continuo durante la curva
                _accumulatedDiff += Math.Abs(leftRoll - rightRoll);
                _sampleCount++;
            }
            else if (_wasTurningBefore)
            {
                // Siamo appena usciti: calcoliamo e restituiamo la media
                double averageDiff = _accumulatedDiff / _sampleCount;
                // 2. Trasformiamo in scala 0-100
                // Formula: 100 * (1 - (diff / max))
                double score = 100 * (1 - (averageDiff / _maxAcceptableDiff));

                // 3. Limitiamo il range tra 0 e 100 (non vogliamo punteggi negativi)
                double finalScore = Math.Clamp(Math.Round(score), 0, 100);

                System.Diagnostics.Debug.WriteLine($"[DETECTOR] DIFF MEDIA: {averageDiff:F1}° | SCORE SIMMETRIA: {finalScore}");

                return finalScore;
            }

            return null;
        }
        public double? GetTurnGForce(SkiDataPoint left, SkiDataPoint right)
        {
            double leftRoll = left.Roll;
            double rightRoll = right.Roll;

            double gLeft = Math.Sqrt(Math.Pow(left.AccX, 2) + Math.Pow(left.AccY, 2) + Math.Pow(left.AccZ, 2));

            // Calcoliamo il modulo per il sensore destro
            double gRight = Math.Sqrt(Math.Pow(right.AccX, 2) + Math.Pow(right.AccY, 2) + Math.Pow(right.AccZ, 2));

            // Prendiamo il massimo tra i due sci in questo istante
            double currentG = Math.Max(gLeft, gRight);

            // Usiamo lo stato della curva già gestito internamente
            if (_isCurrentlyTurning)
            {
                //System.Diagnostics.Debug.WriteLine($"_isCurrentlyTurning(): currentG:{currentG}, _maxGForceInCurrentTurn:{_maxGForceInCurrentTurn}");

                if (currentG > _maxGForceInCurrentTurn)
                {
                    _maxGForceInCurrentTurn = currentG;
                }
            }
            else if (_wasTurningBefore)
            {
                // Fine curva: restituiamo il picco e resettiamo
                
                double finalG = _maxGForceInCurrentTurn;
                _maxGForceInCurrentTurn = 0;
                System.Diagnostics.Debug.WriteLine($"[DETECTOR][{left.Timestamp}ms] G-FORCE: {finalG}");
                return finalG;
            }

            return null;
        }
        public double? GetTurnRadius(SkiDataPoint left, SkiDataPoint right)
        {
            
            if (_isCurrentlyTurning)
            {
                // Prendiamo il valore dallo sci esterno (quello con più roll)
                double currentAccY = Math.Abs(left.Roll) > Math.Abs(right.Roll) ? Math.Abs(left.AccY) : Math.Abs(right.AccY);
                double currentGyroZ = Math.Abs(left.Roll) > Math.Abs(right.Roll) ? Math.Abs(left.GyroZ) : Math.Abs(right.GyroZ);

                _accumulatedAccY += currentAccY;
                _accumulatedGyroZ += currentGyroZ;
                _radiusSampleCount++;
            }
            else if (_wasTurningBefore)
            {
                double? finalRadius = null;

                if (_radiusSampleCount > 0)
                {
                    double avgAccY = (_accumulatedAccY / _radiusSampleCount) * 9.81; // Convertiamo G in m/s^2
                    // double avgGyroZ = (_accumulatedGyroZ / _radiusSampleCount) * (Math.PI / 180.0); // Convertiamo Deg/s in Rad/s
                    double avgGyroZ = (_accumulatedGyroZ / _radiusSampleCount); // dato già in radianti
                    System.Diagnostics.Debug.WriteLine($"[DETECTOR][{left.Timestamp}ms] avgAccY: {avgAccY} - avgGyroZ: {avgGyroZ}");
                    // Evitiamo divisioni per zero se lo sciatore è andato quasi dritto
                    if (avgGyroZ > 0.05)
                    {
                        double radius = avgAccY / Math.Pow(avgGyroZ, 2);
                        finalRadius = Math.Round(Math.Min(radius, 99.9), 1);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[DETECTOR] TURN RADIUS: {finalRadius} m");
                return finalRadius;
            }

            return null;
        }
        public double? GetTurnEarlyEdging(SkiDataPoint left, SkiDataPoint right)
        {
            double EarlyEdgingStartThreshold = StartThreshold - EarlyEdgingStartThresholdDelta;
            double absRoll = Math.Max(Math.Abs(left.Roll), Math.Abs(right.Roll));

            if (_isCurrentlyTurning)
            {
                // Se è l'inizio della curva, salviamo il timestamp
                if (!_wasTurningBefore)
                {
                    _turnStartTime = left.Timestamp; // O il timestamp dell'evento
                    _thresholdReached = false;
                }

                // Se non abbiamo ancora segnato il tempo e superiamo i 20°
                if (!_thresholdReached && absRoll >= EarlyEdgingStartThreshold)
                {
                    double timeToEdge = left.Timestamp - _turnStartTime;
                    _thresholdReached = true;

                    // Calcolo dello Score 0-100
                    double score;
                    if (timeToEdge <= _earlyEdgingExcellentTime)
                    {
                        score = 100;
                    }
                    else if (timeToEdge >= _earlyEdgingMaxAcceptableTime)
                    {
                        score = 0;
                    }
                    else
                    {
                        // Interpolazione lineare tra ExcellentTime (100) e MaxAcceptableTime (0)
                        score = 100 * (1 - (timeToEdge - _earlyEdgingExcellentTime) / (_earlyEdgingMaxAcceptableTime - _earlyEdgingExcellentTime));
                    }

                    double finalScore = Math.Round(score);
                    System.Diagnostics.Debug.WriteLine($"[DETECTOR] EARLY EDGING: {timeToEdge}ms | SCORE: {finalScore}");

                    return finalScore;
                }
            }
            

            return null;
        }
        public double? GetTurnProgressiveEdgeBuild(SkiDataPoint left, SkiDataPoint right)
        {
            double absRoll = Math.Max(Math.Abs(left.Roll), Math.Abs(right.Roll));

            if (_isCurrentlyTurning)
            {
                if (!_wasTurningBefore) // Inizio curva
                {
                    _positiveSamples = 0;
                    _totalSamples = 0;
                    _rollHistory.Clear();
                    _prevRollInTurn = absRoll;
                }
                else
                {
                    
                    // Confronto con la media mobile per smussare il rumore
                    if (_rollHistory.Count > 0)
                    {
                        double average = _rollHistory.Average();
                        if (absRoll > average)
                        {
                            _totalSamples++;
                            
                            if(absRoll > _prevRollInTurn)
                            {
                                _positiveSamples++;
                               // System.Diagnostics.Debug.WriteLine($"[DETECTOR] Positive {absRoll} > {_prevRollInTurn}");
                                _prevRollInTurn = absRoll;
                            }
                            else
                            {
                                //System.Diagnostics.Debug.WriteLine($"[DETECTOR] Negative {absRoll} <= {_prevRollInTurn}");
                            }

                        }

                    }
                    // Aggiornamento coda per la media
                    _rollHistory.Enqueue(absRoll);
                    if (_rollHistory.Count > WindowSize) _rollHistory.Dequeue();
                }
            }
            else if (_wasTurningBefore) // Fine curva
            {
                if (_totalSamples == 0) { ResetMetrics(); return null; }

                // Calcolo grezzo
                double rawScore = ((double)_positiveSamples / _totalSamples);

                // Rebasement: se rawScore è >= 0.9, diventa 1.0. Altrimenti scala linearmente.
                double normalizedScore = Math.Min(1.0, rawScore / ProgressiveBuildPerfectionTolerance);
                double finalScore = normalizedScore * 100.0;

                System.Diagnostics.Debug.WriteLine($"[DETECTOR] PROG BUILD: {finalScore:F1}% (Raw: {rawScore * 100:F1}%, Campioni: {_positiveSamples}/{_totalSamples})");
                return Math.Round(finalScore, 1);
            }
            return null;
        }
        public double? GetTurnShape(SkiDataPoint left, SkiDataPoint right)
        {

            return null;
        }
        public double? GetTurnCarvingIndex(SkiDataPoint left, SkiDataPoint right)
        {
           
            if (_isCurrentlyTurning)
            {
                // Identifichiamo lo sci esterno (quello con più roll)
                var outsideSki = Math.Abs(left.Roll) > Math.Abs(right.Roll) ? left : right;

                double accY = Math.Abs(outsideSki.AccY);
                double gyroZ = Math.Abs(outsideSki.GyroZ);
                if (accY > 0)
                {
                    double carvingRatio = gyroZ / accY;
                    //System.Diagnostics.Debug.WriteLine($"[DETECTOR] gyroZ: {gyroZ} - accY: {accY} - carvingRatio:{carvingRatio}");
                    _accumulatedCarvingDeviation += Math.Abs(carvingRatio - _targetCarvingRatio);
                    _carvingSampleCount++;
                }


            }
            else if (_wasTurningBefore)
            {
                if (_carvingSampleCount == 0) { return null; }

                // Calcoliamo la media della deviazione
                double avgDeviation = _accumulatedCarvingDeviation / _carvingSampleCount;
                // System.Diagnostics.Debug.WriteLine($"[DETECTOR] avgDeviation: {avgDeviation}");
                // Normalizziamo: 0 deviazione = 100%, deviazione alta = 0%
                // Un valore di 5.0 di deviazione lo consideriamo già "derapata totale" (0%)
                double finalCarvingIndex = Math.Max(0, 100 - (avgDeviation / 0.2 * 100));

                System.Diagnostics.Debug.WriteLine($"[DETECTOR] CARVING INDEX: {finalCarvingIndex:F1}%");

                return Math.Round(finalCarvingIndex, 1);
            }
            return null;
        }
        private void UpdateTurnState(double absRoll, uint ts)
        {
            if (!_isCurrentlyTurning && !_isCoolingDown)
            {
                // Posso iniziare solo se sono sopra la soglia E non sono in fase di rientro
                if (absRoll > StartThreshold)
                {
                    _isCurrentlyTurning = true;
                    _maxRollInCurrentTurn = absRoll;
                    _turnStartTime = ts;
                    // System.Diagnostics.Debug.WriteLine($"[DEBUG][{ts}ms] CURVA INIZIATA");
                }
            }
            else if (_isCurrentlyTurning && !_isCoolingDown)
            {
                // Aggiorna picco
                if (absRoll > _maxRollInCurrentTurn) _maxRollInCurrentTurn = absRoll;

                // Condizioni di chiusura
                bool dropFromPeak = absRoll < (_maxRollInCurrentTurn - GapToConfirmPeak);
                bool dropBelowExit = absRoll < ExitThreshold;

                if (dropFromPeak || dropBelowExit)
                {
                    _isCurrentlyTurning = false;
                    _isCoolingDown = dropFromPeak; // Entriamo in protezione se stiamo scendendo dal picco, o usciamo dalla protezione se siamo soto soglia.
                    //System.Diagnostics.Debug.WriteLine($"[DEBUG][{ts}ms] CURVA TERMINATA, Inizio cool-down");
                }
            }
            else if (_isCoolingDown)
            {
                // Restiamo in cool-down finché non torniamo sotto la soglia di uscita o di sicurezza
                // Questo impedisce di riaprire una curva a 17° mentre si sta ancora uscendo
                if (absRoll < ExitThreshold)
                {
                    _isCoolingDown = false;
                    //System.Diagnostics.Debug.WriteLine($"[DEBUG][{ts}ms] Cool-down terminato, sistema pronto");
                }
            }
        }

        private void ResetMetrics()
        {
            // EDGE ANGLE
            _maxRollInCurrentTurn = 0;

            // EDGE SIMILARITY
            _accumulatedDiff = 0;
            _sampleCount = 0;

            //G-FORCE
            _maxGForceInCurrentTurn = 0;

            // RADIUS
            _accumulatedAccY = 0;
            _accumulatedGyroZ = 0;
            _radiusSampleCount = 0;

             // EARLY EDGING
            _turnStartTime = 0;
            _thresholdReached = false;

            // PROGRESSIVE BUILD
            _positiveSamples = 0;
            _totalSamples = 0;
            _prevRollInTurn = 0;
            _rollHistory.Clear();

            // CARVING INDEX
            _accumulatedCarvingDeviation = 0;
            _carvingSampleCount = 0;

            // proprietà della curva
            _turnEdgeAngle = 0;
            _turnEdgeSimilarity  = 0;
            _turnEarlyEdging  = 0;
            _turnGForce  = 0;
            _turnRadius  = 0;
            _turnShape  = 0;
            _turnProgressiveEdgeBuild  = 0;
            _turnCarvingIndex  = 0;
    }
    }
}
