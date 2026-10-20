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
            context.CarvingGPeakIndex,
            context.RollPeak,
            context.CarvingGPeakAverage);
    }

    private static double ComputeEdgeAngle(TurnAnalysisContext context)
        => context.AverageOuterRollAround(context.RollPeakIndex);

    private static double ComputeMaxGForce(TurnAnalysisContext context)
    {
        return Math.Round(Math.Max(context.CarvingGPeakAverage, 1.0), 1);
    }

    private static double ComputeLoadGForce(TurnAnalysisContext context)
    {
        double g = context.GPeakAverage;

        if (g < 1.0 && context.RollPeak > 15)
            g = 1.0 + context.RollPeak * 0.005;

        return Math.Round(g, 1);
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
        if (context.Data.Count < 10) return 0;
        if (context.RollPeak < 5) return 0;

        const int window = 3;
        double gAtEdge = context.AverageOuterGAround(context.RollPeakIndex, window);
        double gPeak = context.AverageOuterGAround(context.GPeakIndex, window);

        if (gPeak <= 0) return 0;

        double ratio = gAtEdge / gPeak;
        return Math.Round(Clamp(ratio, 0, 1) * 100.0, 1);
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

        var angularRates = new List<double>();
        for (int i = 1; i < buildCount; i++)
        {
            double dt = timestamps[i] - timestamps[i - 1];
            if (dt <= 0) continue;
            angularRates.Add((rolls[i] - rolls[i - 1]) / dt);
        }

        var jerks = new List<double>();
        for (int i = 1; i < angularRates.Count; i++)
        {
            double dt = timestamps[i + 1] - timestamps[i];
            if (dt <= 0) continue;
            jerks.Add(Math.Abs((angularRates[i] - angularRates[i - 1]) / dt));
        }

        double meanJerk = jerks.Count > 0 ? jerks.Average() : 0;
        double jerkIndex = meanJerk * buildDuration * buildDuration / Math.Max(peakAbs, 1.0);
        double smoothnessScore = 1.0 / (1.0 + 4.0 * jerkIndex);

        double loadCorrelation = Correlation(rolls, gs);
        double loadSupportScore = double.IsNaN(loadCorrelation) ? 0.0 : Clamp01(loadCorrelation);

        int inversions = 0;
        for (int i = 1; i < buildCount; i++)
        {
            if (rolls[i] - rolls[i - 1] < -0.5) inversions++;
        }

        double inversionRatio = (double)inversions / (buildCount - 1);
        double monotonicityScore = Clamp01(1.0 - inversionRatio * 3.0);

        const double wProgression = 0.30;
        const double wSmoothness = 0.25;
        const double wLoadSupport = 0.35;
        const double wMonotonicity = 0.10;

        double finalScore =
            wProgression * progressionScore +
            wSmoothness * smoothnessScore +
            wLoadSupport * loadSupportScore +
            wMonotonicity * monotonicityScore;

        return Math.Round(Clamp01(finalScore) * 100);
    }

    private static double ComputePerformanceIndex(TurnAnalysisContext context)
    {
        double maxRoll = ComputeEdgeAngle(context);
        double maxG = ComputeLoadGForce(context);

        if (maxRoll < 10) return 0;

        double rollRad = maxRoll * (Math.PI / 180.0);
        double gBinario = 1.0 / Math.Cos(rollRad);
        double boostRequired = GetDynamicMultiplier(maxRoll);
        double gOttimale = gBinario * boostRequired;

        double score;

        if (maxG <= gBinario)
        {
            score = (maxG / gBinario) * 60.0;
            if (maxG < gBinario * 0.85) score *= 0.5;
        }
        else
        {
            double progress = (maxG - gBinario) / (gOttimale - gBinario);
            score = 60.0 + (Math.Min(progress, 1.0) * 40.0);
        }

        if (maxRoll < 20 && maxG > 1.5)
            score = Math.Max(0, score - (maxG * 40.0));

        return Math.Round(score, 1);
    }

    private static double GetDynamicMultiplier(double currentRoll)
    {
        const double MinRoll = 30.0;
        const double MaxRoll = 50.0;
        const double MaxMult = 2.4;
        const double MinMult = 1.8;

        double clampedRoll = Math.Clamp(Math.Abs(currentRoll), MinRoll, MaxRoll);
        double t = (clampedRoll - MinRoll) / (MaxRoll - MinRoll);

        return MaxMult - t * (MaxMult - MinMult);
    }

    private static double CalculateRSquared(List<double> x, List<double> y)
    {
        int n = x.Count;
        if (n < 2) return 0;

        double meanX = x.Average();
        double meanY = y.Average();

        double ssXY = x.Zip(y, (xi, yi) => (xi - meanX) * (yi - meanY)).Sum();
        double ssXX = x.Sum(xi => (xi - meanX) * (xi - meanX));
        double ssYY = y.Sum(yi => (yi - meanY) * (yi - meanY));

        if (ssXX == 0 || ssYY == 0 || ssXY <= 0) return 0;

        double r = ssXY / Math.Sqrt(ssXX * ssYY);
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
        => Math.Max(0.0, Math.Min(1.0, value));

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
        public int GPeakIndex { get; }
        public double GPeakAverage { get; }
        public int CarvingGPeakIndex { get; }
        public double CarvingGPeakAverage { get; }

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

            int windowSize = Math.Min(10, Math.Max(1, n / 4));
            GPeakIndex = GMinIndex;
            GPeakAverage = double.MinValue;

            for (int i = GMinIndex; i < n; i++)
            {
                double average = AverageOuterGAround(i, windowSize);
                if (average > GPeakAverage)
                {
                    GPeakAverage = average;
                    GPeakIndex = i;
                }
            }

            if (GPeakAverage == double.MinValue)
            {
                GPeakAverage = minG;
                GPeakIndex = GMinIndex;
            }

            // The global G peak often falls on the release/impact spike after the
            // edge is already back to flat. Report the load around maximum edge
            // angle instead, so "FORZA G" belongs to the carved phase.
            int apexRadius = Math.Max(5, n / 10);
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
        {
            if (Data.Count == 0) return 0;

            int start = Math.Max(0, index - windowSize);
            int end = Math.Min(Data.Count - 1, index + windowSize);

            double sum = 0;
            int count = 0;

            for (int i = start; i <= end; i++)
            {
                sum += OuterRoll[i];
                count++;
            }

            return count > 0 ? Math.Abs(sum / count) : 0;
        }

        public double AverageOuterGAround(int index, int windowSize = 5)
        {
            if (Data.Count == 0) return 0;

            int start = Math.Max(0, index - windowSize);
            int end = Math.Min(Data.Count - 1, index + windowSize);

            double sum = 0;
            int count = 0;

            for (int i = start; i <= end; i++)
            {
                sum += OuterG[i];
                count++;
            }

            return count > 0 ? sum / count : 0;
        }
    }
}
