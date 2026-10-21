namespace SkiTelemetry_Core;

public readonly record struct TurnMarkers(
    int EngagementIndex,
    int RollPeakIndex,
    int GMinIndex,
    int GPeakIndex,
    double PeakRoll,
    double PeakG);

public static class SkiTurnAnalyzer
{
    public static void Populate(List<SkiFrame> turnData, SkiTurn turn, ISet<MonitorMode>? requestedMetrics = null)
    {
        if (turnData == null || turnData.Count == 0) return;

        bool computeAll = requestedMetrics == null
            || requestedMetrics.Count == 0
            || requestedMetrics.Contains(MonitorMode.DebugData);

        var context = new TurnAnalysisContext(turnData);

        if (computeAll || requestedMetrics!.Contains(MonitorMode.EdgeAngle))
            turn.EdgeAngle = ComputeEdgeAngle(context);

        if (computeAll || requestedMetrics!.Contains(MonitorMode.EdgeSimilarity))
            turn.EdgeSimilarity = ComputeSimilarity(context);

        if (computeAll || requestedMetrics!.Contains(MonitorMode.EarlyLoading))
            turn.EarlyLoading = ComputeEarlyLoading(context);

        if (computeAll || requestedMetrics!.Contains(MonitorMode.GForce))
            turn.GForce = ComputeMaxGForce(context);

        if (computeAll || requestedMetrics!.Contains(MonitorMode.ProgressiveEdgeBuild))
            turn.ProgressiveEdgeBuild = ComputeEdgeBuild(context);

        if (computeAll || requestedMetrics!.Contains(MonitorMode.PerformanceIndex))
            turn.PerformanceIndex = ComputePerformanceIndex(context);

        if (computeAll || requestedMetrics!.Contains(MonitorMode.Foot))
            turn.Foot = context.OuterSide;
    }

    public static double CalculateMaxEdgeAngle(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count == 0) return 0;
        return ComputeEdgeAngle(new TurnAnalysisContext(turnData));
    }

    public static double CalculateMaxGForce(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count == 0) return 0;
        return ComputeMaxGForce(new TurnAnalysisContext(turnData));
    }

    public static SensorSide DetermineOuterFoot(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count == 0) return SensorSide.Left;
        return new TurnAnalysisContext(turnData).OuterSide;
    }

    public static double CalculateSimilarity(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 3) return 0;
        return ComputeSimilarity(new TurnAnalysisContext(turnData));
    }

    public static double CalculateRadius(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 5) return 0;

        var radii = new List<double>();

        foreach (var frame in turnData)
        {
            double g = (frame.Left.AdjustedG + frame.Right.AdjustedG) / 2.0;
            double gyroX = (frame.Left.SmoothGyroX + frame.Right.SmoothGyroX) / 2.0;
            double gyroY = (frame.Left.SmoothGyroY + frame.Right.SmoothGyroY) / 2.0;
            double omegaRad = Math.Sqrt(gyroX * gyroX + gyroY * gyroY) * (Math.PI / 180.0);

            if (omegaRad < 0.05 || g <= 1.0) continue;

            double accCentripetal = 9.80665 * Math.Sqrt(Math.Max(g * g - 1.0, 0));
            double radius = accCentripetal / (omegaRad * omegaRad);

            if (double.IsFinite(radius) && radius > 0 && radius < 200)
                radii.Add(radius);
        }

        if (radii.Count == 0) return 0;

        radii.Sort();
        double median = radii[radii.Count / 2];
        return Math.Round(Math.Min(median, 50.0), 1);
    }

    public static double CalculatePerformanceIndex(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 5) return 0;
        return ComputePerformanceIndex(new TurnAnalysisContext(turnData));
    }

    public static double CalculateEdgeBuild(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 5) return 0;
        return ComputeEdgeBuild(new TurnAnalysisContext(turnData));
    }

    public static double CalculateEarlyLoading(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 10) return 0;
        return ComputeEarlyLoading(new TurnAnalysisContext(turnData));
    }

    public static TurnMarkers GetMarkers(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count == 0) return default;

        var context = new TurnAnalysisContext(turnData);
        return new TurnMarkers(
            context.EngagementIndex,
            context.RollPeakIndex,
            context.GMinIndex,
            context.GMaxIndex,
            context.RollPeak,
            context.GMax);
    }

    private static double ComputeEdgeAngle(TurnAnalysisContext context)
        => context.AverageOuterRollAround(context.RollPeakIndex);

    private static double ComputeMaxGForce(TurnAnalysisContext context)
    {
        return Math.Round(context.GMax, 1);
    }

    private static double ComputeSimilarity(TurnAnalysisContext context)
    {
        if (context.Data.Count < 3) return 0;

        double outerPeak = context.RollPeak;
        double innerPeak = context.InnerRoll.Length == 0
            ? 0
            : context.InnerRoll.Max(v => Math.Abs(v));

        if (outerPeak < 5 || innerPeak < 5) return 0;

        double peakScore = 100.0 * (1.0 - Math.Abs(outerPeak - innerPeak) / Math.Max(outerPeak, 35.0));
        peakScore = Clamp(peakScore, 0, 100);

        int comparable = 0;
        int mismatched = 0;

        for (int i = 0; i < context.Data.Count; i++)
        {
            double outer = context.OuterRoll[i];
            double inner = context.InnerRoll[i];

            if (Math.Abs(outer) <= 1 || Math.Abs(inner) <= 1) continue;

            comparable++;
            if (Math.Sign(outer) != Math.Sign(inner)) mismatched++;
        }

        if (comparable > 0 && mismatched * 2 > comparable) return 0;

        double correlation = Correlation(context.OuterRoll, context.InnerRoll);
        double shapeScore = double.IsNaN(correlation) ? peakScore : Clamp(correlation, 0, 1) * 100.0;
        double signScore = comparable == 0 ? 100.0 : 100.0 * (1.0 - (double)mismatched / comparable);

        double score = 0.4 * peakScore + 0.4 * shapeScore + 0.2 * signScore;
        return Math.Round(Clamp(score, 0, 100), 1);
    }

    private static double ComputeEarlyLoading(TurnAnalysisContext context)
    {
        int n = context.Data.Count;
        if (n < 10) return 0;
        if (context.RollPeak < 5) return 0;

        int gMin = context.GMinIndex;
        double gMinValue = context.OuterG[gMin];
        double gMax = gMinValue;

        for (int i = gMin; i < n; i++)
        {
            if (context.OuterG[i] > gMax)
                gMax = context.OuterG[i];
        }

        // Carico piatto: nessun picco da temporizzare.
        if (gMax - gMinValue < 0.05) return 0;

        // Primo istante in cui il carico raggiunge l'80% del picco.
        double threshold = 0.8 * gMax;
        int t80 = n - 1;

        for (int i = gMin; i < n; i++)
        {
            if (context.OuterG[i] >= threshold)
            {
                t80 = i;
                break;
            }
        }

        double lag = ((double)context.Data[t80].Left.Timestamp
                      - context.Data[context.RollPeakIndex].Left.Timestamp) / 1000.0;

        double score = 100.0 * Clamp(1.0 - Math.Abs(lag) / 0.6, 0.0, 1.0);
        return Math.Round(score, 1);
    }

    private static double ComputeEdgeBuild(TurnAnalysisContext context)
    {
        int peakIndex = context.RollPeakIndex;
        double peakAbs = context.RollPeak;

        if (peakAbs < 10 || peakIndex < 3) return 0.0;

        int start = context.EngagementIndex;
        if (start >= peakIndex) return 0.0;

        int buildCount = peakIndex - start + 1;
        if (buildCount < 3) return 0.0;

        var rolls = new double[buildCount];
        var gs = new double[buildCount];
        var timestamps = new double[buildCount];

        for (int i = 0; i < buildCount; i++)
        {
            int index = start + i;
            rolls[i] = Math.Abs(context.OuterRoll[index]);
            gs[i] = context.OuterG[index];
            timestamps[i] = context.Data[index].Left.Timestamp;
        }

        double buildDuration = timestamps[^1] - timestamps[0];
        if (buildDuration <= 0) return 0.0;

        double progressionScore = Clamp01(CalculateRSquared(timestamps.ToList(), rolls.ToList()));

        // Morbidezza: coefficiente di variazione della velocita' angolare su 3 campioni.
        // Piu' robusto del jerk (derivata seconda), che su dati a 20 ms e' dominato dal rumore.
        int step = Math.Min(3, buildCount - 1);
        var angularRates = new List<double>();
        for (int i = step; i < buildCount; i++)
        {
            double dt = timestamps[i] - timestamps[i - step];
            if (dt <= 0) continue;
            angularRates.Add((rolls[i] - rolls[i - step]) / dt);
        }

        double smoothnessScore = 0.5;
        if (angularRates.Count >= 2)
        {
            double meanRate = angularRates.Average();
            if (Math.Abs(meanRate) < 1e-9)
            {
                smoothnessScore = 0.0;
            }
            else
            {
                double variance = angularRates.Sum(r => (r - meanRate) * (r - meanRate)) / angularRates.Count;
                double cv = Math.Sqrt(variance) / Math.Abs(meanRate);
                smoothnessScore = 1.0 / (1.0 + cv);
            }
        }

        // Sostegno del carico: correlazione nella finestra che parte dopo il minimo di G,
        // per non contaminare con il carico residuo della curva precedente.
        int loadOffset = Math.Max(0, context.GMinIndex - start);
        if (loadOffset > buildCount - 5) loadOffset = 0;

        double loadCorrelation = Correlation(
            rolls.Skip(loadOffset).ToArray(),
            gs.Skip(loadOffset).ToArray());
        double loadSupportScore = double.IsNaN(loadCorrelation) ? 0.0 : Clamp01(loadCorrelation);

        const double wProgression = 0.35;
        const double wSmoothness = 0.35;
        const double wLoadSupport = 0.30;

        double finalScore =
            wProgression * progressionScore +
            wSmoothness * smoothnessScore +
            wLoadSupport * loadSupportScore;

        return Math.Round(Clamp01(finalScore) * 100);
    }

    // Carico di riferimento per il punteggio massimo: 100% quando il G sullo sci
    // esterno vale R100 volte il carico teorico di binario (1 / cos(Roll)).
    private const double R100 = 1.30;

    private static double ComputePerformanceIndex(TurnAnalysisContext context)
    {
        double maxRoll = ComputeEdgeAngle(context);
        double maxG = context.CarvingGPeakAverage;

        if (maxRoll < 10) return 0;

        // G teorico di una curva in binario puro: in curva l'accelerazione centripeta
        // vale g*tan(Roll), quindi il carico totale sullo sci e' 1/cos(Roll).
        double rollRad = maxRoll * (Math.PI / 180.0);
        double gBinario = 1.0 / Math.Cos(rollRad);

        // Quanto carico reale c'e' sull'esterno rispetto al binario:
        // ratio >= 1 -> curva condotta (o spinta), ratio < 1 -> derapata / peso sull'interno.
        double ratio = maxG / gBinario;

        double score;
        if (ratio <= 1.0)
        {
            // Sotto il binario: cresce linearmente fino a 60 quando lo sci "tiene".
            score = 60.0 * Clamp01(ratio);
        }
        else
        {
            // Sopra il binario: premio progressivo fino a 100 per carico "spinto".
            double progress = (ratio - 1.0) / (R100 - 1.0);
            score = 60.0 + Math.Min(progress, 1.0) * 40.0;
        }

        // Basso angolo con carico anomalo: probabile sterzata/derapata.
        // Penalita' continua e limitata, per non azzerare curve legittime.
        if (maxRoll < 20.0 && ratio > 1.0)
            score -= Math.Min(20.0, (ratio - 1.0) * 60.0);

        return Math.Round(Clamp(score, 0.0, 100.0), 1);
    }

    private static double CalculateRSquared(List<double> x, List<double> y)
    {
        int n = x.Count;
        if (n < 2) return 0;

        // R^2 = correlazione di Pearson al quadrato. Se r <= 0 (o indefinita)
        // non c'e' una relazione lineare positiva: contributo nullo.
        double r = Correlation(x.ToArray(), y.ToArray());
        if (double.IsNaN(r) || r <= 0) return 0;

        return r * r;
    }

    private static double Correlation(double[] x, double[] y)
    {
        int n = Math.Min(x.Length, y.Length);
        if (n < 2) return double.NaN;

        double meanX = x.Take(n).Average();
        double meanY = y.Take(n).Average();

        double ssXY = 0;
        double ssXX = 0;
        double ssYY = 0;

        for (int i = 0; i < n; i++)
        {
            double dx = x[i] - meanX;
            double dy = y[i] - meanY;
            ssXY += dx * dy;
            ssXX += dx * dx;
            ssYY += dy * dy;
        }

        if (ssXX == 0 || ssYY == 0) return double.NaN;

        return ssXY / Math.Sqrt(ssXX * ssYY);
    }

    private static double Clamp01(double value)
        => Clamp(value, 0.0, 1.0);

    private static double Clamp(double value, double min, double max)
        => Math.Max(min, Math.Min(max, value));

    private sealed class TurnAnalysisContext
    {
        public List<SkiFrame> Data { get; }
        public bool IsTurnToLeft { get; }
        public SensorSide OuterSide => IsTurnToLeft ? SensorSide.Right : SensorSide.Left;
        public double[] OuterRoll { get; }
        public double[] InnerRoll { get; }
        public double[] OuterG { get; }
        public int RollPeakIndex { get; }
        public double RollPeak { get; }
        public int EngagementIndex { get; }
        public int GMinIndex { get; }
        public int CarvingGPeakIndex { get; }
        public double CarvingGPeakAverage { get; }
        public int GMaxIndex { get; }
        public double GMax { get; }

        public TurnAnalysisContext(List<SkiFrame> data)
        {
            Data = data;
            IsTurnToLeft = data.Average(f => f.CombinedGyro) > 0;

            int n = data.Count;
            OuterRoll = new double[n];
            InnerRoll = new double[n];
            OuterG = new double[n];

            for (int i = 0; i < n; i++)
            {
                SkiFrame frame = data[i];
                OuterRoll[i] = IsTurnToLeft ? frame.Right.SmoothRoll : frame.Left.SmoothRoll;
                InnerRoll[i] = IsTurnToLeft ? frame.Left.SmoothRoll : frame.Right.SmoothRoll;
                OuterG[i] = IsTurnToLeft ? frame.Right.AdjustedG : frame.Left.AdjustedG;
            }

            GMaxIndex = 0;
            GMax = 0;
            for (int i = 0; i < n; i++)
            {
                if (OuterG[i] > GMax)
                {
                    GMax = OuterG[i];
                    GMaxIndex = i;
                }
            }

            RollPeakIndex = 0;
            RollPeak = 0;

            for (int i = 0; i < n; i++)
            {
                double absRoll = Math.Abs(OuterRoll[i]);
                if (absRoll > RollPeak)
                {
                    RollPeak = absRoll;
                    RollPeakIndex = i;
                }
            }

            EngagementIndex = RollPeakIndex;
            double engagementThreshold = Math.Max(10.0, RollPeak * 0.2);

            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(OuterRoll[i]) >= engagementThreshold)
                {
                    EngagementIndex = i;
                    break;
                }
            }

            GMinIndex = 0;
            double minG = double.MaxValue;

            for (int i = 0; i < n; i++)
            {
                if (OuterG[i] < minG)
                {
                    minG = OuterG[i];
                    GMinIndex = i;
                }
            }

            // The global G peak often falls on the release/impact spike after the
            // edge is already back to flat. Report the load around maximum edge
            // angle instead, so "FORZA G" belongs to the carved phase.
            // Finestra stretta (n/20): piu' aderente all'apice, meno contaminata
            // dal carico post-apice. E' l'unico G usato sia da "FORZA G" sia dal PI.
            int apexRadius = Math.Max(5, n / 20);
            int apexStart = Math.Max(0, RollPeakIndex - apexRadius);
            int apexEnd = Math.Min(n - 1, RollPeakIndex + apexRadius);

            CarvingGPeakIndex = RollPeakIndex;
            CarvingGPeakAverage = OuterG[RollPeakIndex];

            for (int i = apexStart; i <= apexEnd; i++)
            {
                if (OuterG[i] > CarvingGPeakAverage)
                {
                    CarvingGPeakAverage = OuterG[i];
                    CarvingGPeakIndex = i;
                }
            }
        }

        public double AverageOuterRollAround(int index, int windowSize = 5)
            => Math.Abs(AverageAround(OuterRoll, index, windowSize));

        public double AverageOuterGAround(int index, int windowSize = 5)
            => AverageAround(OuterG, index, windowSize);

        private double AverageAround(double[] series, int index, int windowSize)
        {
            if (Data.Count == 0) return 0;

            int start = Math.Max(0, index - windowSize);
            int end = Math.Min(Data.Count - 1, index + windowSize);

            double sum = 0;
            int count = 0;

            for (int i = start; i <= end; i++)
            {
                sum += series[i];
                count++;
            }

            return count > 0 ? sum / count : 0;
        }
    }
}
