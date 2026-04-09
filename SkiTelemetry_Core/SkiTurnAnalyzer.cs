using System.Diagnostics;
using System.Reflection.Metadata;

namespace SkiTelemetry_Core;



public static class SkiTurnAnalyzer
{
    
    public static TurnMetrics Compute(List<SkiFrame> turnData)
    {
        var metrics = new TurnMetrics();

        return metrics;
    }


    public static double CalculateMaxEdgeAngle(List<SkiFrame> turnData)
    {
        //System.Diagnostics.Debug.WriteLine($"START ROLL CALCULATION -------------------------------------------------");
        var peak = GetRollPeak(turnData);
        //System.Diagnostics.Debug.WriteLine($"[ANALYZER] Recupero peak.: {peak.MaxRoll} ({peak.Index})");
        double roll = GetAverageRollInWindow(turnData, peak.Index);

        return Math.Round(roll);
    }
    public static double CalculateMaxGForce(List<SkiFrame> turnData)
    {
        var peak = GetGForcePeak(turnData);
        double g = GetAverageGInWindow(turnData, peak.Index);

        // Reality Check per pendenza/offset
        if (g < 1.0 && peak.MaxRoll > 15)
            g = 1.0 + (peak.MaxRoll * 0.005);

        return Math.Round(g, 1);
    }
    public static SensorSide DetermineOuterFoot(List<SkiFrame> turnData)
    {
        double avgY = turnData.Average(f => (f.CombinedGyro));
        return avgY < 0 ? SensorSide.Right : SensorSide.Left;
    }
    public static double CalculateSimilarity(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 3) return 0;

        // Prendiamo il picco di ogni sci separatamente nella curva
        double maxRollL = 0;
        double maxRollR = 0;

        foreach (var f in turnData)
        {
            maxRollL = Math.Max(maxRollL, Math.Abs(f.Left.SmoothRoll));
            maxRollR = Math.Max(maxRollR, Math.Abs(f.Right.SmoothRoll));
        }

        // Se uno dei due non ha nemmeno inclinato, il parallelismo è nullo
        if (maxRollL < 5 || maxRollR < 5) return 0;

        // Calcoliamo la differenza relativa tra i due picchi
        // Se sono identici, diff = 0. Se uno è il doppio dell'altro, diff è alta.
        double rollDiff = Math.Abs(maxRollL - maxRollR);

        // Applichiamo una curva di punteggio più "umana"
        // Lo 0% lo diamo solo se c'è una differenza di oltre 35 gradi (praticamente uno sci dritto e uno a terra)
        double score = 100 * (1.0 - (rollDiff / 35.0));

        // Bonus/Malus dinamico: se gli angoli sono entrambi alti, siamo più tolleranti
        // (Nel corto è normale avere qualche grado di differenza se si spinge)
        if (maxRollL > 30 && maxRollR > 30) score += 5;

        return Math.Round(Math.Clamp(score, 0, 100), 1);
    }
    public static double CalculateRadius(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 5) return 0;

        // 1. Estrazione dei massimi ASINCRONI (Indipendenti dal frame)
        // Cerchiamo la massima pressione e la massima rotazione reale del sistema
        double maxG = turnData.Max(f => (f.Left.AdjustedG + f.Right.AdjustedG) / 2.0);

        // Magnitudo della rotazione (X+Y) per compensare l'inclinazione del sensore
        double maxGyroMag = turnData.Max(f =>
            Math.Sqrt(Math.Pow((f.Left.SmoothGyroY + f.Right.SmoothGyroY) / 2.0, 2) +
                      Math.Pow((f.Left.SmoothGyroX + f.Right.SmoothGyroX) / 2.0, 2)));

        // 2. Conversione in radianti al secondo
        double omegaRad = maxGyroMag * (Math.PI / 180.0);

        // 3. Validazione fisica minima per evitare divisioni per zero o errori macroscopici
        if (omegaRad < 0.05 || maxG <= 1.0) return 0;

        // 4. Calcolo dell'accelerazione centripeta (a_c = g * sqrt(G^2 - 1))
        const double gConstant = 9.80665;
        double accCentripetal = gConstant * Math.Sqrt(Math.Max(Math.Pow(maxG, 2) - 1.0, 0));

        // 5. Formula Universale: R = a_c / omega^2
        // Non ci sono costanti "16.0", non ci sono assunzioni sul tipo di sci.
        // Il raggio è il risultato puro dell'interazione tra forza sentita e rotazione misurata.
        double radius = accCentripetal / Math.Pow(omegaRad, 2);

        // Limite di sanità per evitare asintoti (es. rettilinei con vibrazioni)
        return Math.Round(Math.Min(radius, 50.0), 1);
    }
    public static double CalculatePerformanceIndex(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 5) return 0;

        // 1. Picchi Asincroni (Memoria: Roll prima, G dopo)
        double maxRoll = turnData.Max(f => Math.Abs(f.Left.SmoothRoll + f.Right.SmoothRoll) / 2.0);
        double maxG = turnData.Max(f => (f.Left.AdjustedG + f.Right.AdjustedG) / 2.0);

        if (maxRoll < 10) return 0;

        // 2. Calcolo soglia di "Taglio Passivo" (G necessaria per non derapare)
        double rollRad = maxRoll * (Math.PI / 180.0);
        double gBinario = 1.0 / Math.Cos(rollRad);

        var start = turnData[0].Left.Timestamp;
        var end = turnData[^1].Left.Timestamp;
        var duration = (end - start) / 1000.0;

        double boostRequired = GetDynamicMultiplier(maxRoll);

        double gOttimale = gBinario * boostRequired;

        // 4. Mappatura dello Score
        double score = 0;
        if (maxG <= gBinario)
        {
            // Se sei sotto il binario, lo score va da 0 a 60
            // Esempio: se hai 0.8 di G su 1.0 teorico, sei al 30-40%
            score = (maxG / gBinario) * 60.0;

            // Se la G è troppo bassa rispetto all'angolo (derapata evidente)
            if (maxG < gBinario * 0.85) score *= 0.5;
        }
        else
        {
            // Se sei sopra il binario, scali da 60 a 100
            // Raggiungi 100 solo se arrivi alla G Ottimale (deformazione vera)
            double progress = (maxG - gBinario) / (gOttimale - gBinario);
            score = 60.0 + (Math.Min(progress, 1.0) * 40.0);
        }

        // 5. IL FILTRO ANTI-SPAZZOLATA (Il "Killer" dell'ultima curva)
        // Se la G è alta ma il Roll è basso, NON è carving. È un urto laterale.
        // In una spazzolata il rapporto G/Roll è altissimo.
        if (maxRoll < 20 && maxG > 1.5)
        {
            score = Math.Max(0, score - (maxG * 40.0));
        }

        return Math.Round(score, 1);
    }

    private static double GetDynamicMultiplier(double currentRoll)
    {
        // Definiamo i nostri "pilastri" di taratura
        const double MinRoll = 20.0;  // Sotto i 20° chiediamo il massimo
        const double MaxRoll = 50.0;  // Sopra i 50° chiediamo il minimo

        const double MaxMult = 2.0;   // Moltiplicatore per il corto raggio (cattivo)
        const double MinMult = 1.6;   // Moltiplicatore per l'arco ampio (umano)

        // 1. Appiattiamo l'angolo nei limiti dei pilastri
        double clampedRoll = Math.Clamp(Math.Abs(currentRoll), MinRoll, MaxRoll);

        // 2. Calcoliamo quanto siamo "avanti" nel range (da 0.0 a 1.0)
        double t = (clampedRoll - MinRoll) / (MaxRoll - MinRoll);

        // 3. Interpolazione lineare: più t cresce (più pieghi), più il moltiplicatore scende
        return MaxMult - t * (MaxMult - MinMult);
    }

    public static double CalculateEdgeBuild(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 5)
            return 0.0;

        var rolls = turnData
            .Select(f => (f.Left.SmoothRoll + f.Right.SmoothRoll) / 2.0)
            .ToList();

        var timestamps = turnData
            .Select(f => (double)f.Left.Timestamp)
            .ToList();

        int n = rolls.Count;

        //Debug.WriteLine($"[EdgeBuild] Frame totali: {n}");
        //Debug.WriteLine($"[EdgeBuild] Roll min: {rolls.Min():F2}° | max: {rolls.Max():F2}° | range: {rolls.Max() - rolls.Min():F2}°");

        // Picco
        int peakIndex = 0;
        double peakAbs = 0;
        for (int i = 0; i < n; i++)
        {
            double absRoll = Math.Abs(rolls[i]);
            if (absRoll > peakAbs) { peakAbs = absRoll; peakIndex = i; }
        }

        //Debug.WriteLine($"[EdgeBuild] Picco: {rolls[peakIndex]:F2}° @ index {peakIndex} (timestamp {timestamps[peakIndex]:F0} ms)");

        if (peakIndex < 3)
            return 0.5;

        var buildRolls = rolls.Take(peakIndex + 1).ToList();
        var buildTimestamps = timestamps.Take(peakIndex + 1).ToList();
        int bN = buildRolls.Count;

        //Debug.WriteLine($"[EdgeBuild] Fase BUILD: {bN} frame | da {buildTimestamps.First():F0} ms a {buildTimestamps.Last():F0} ms");

        // ── METRICA A – Jerk ──────────────────────────────────────────────────
        var angularRates = new List<double>();
        for (int i = 1; i < bN; i++)
        {
            double dt = buildTimestamps[i] - buildTimestamps[i - 1];
            if (dt <= 0) continue;
            angularRates.Add((buildRolls[i] - buildRolls[i - 1]) / dt);
        }

        var jerks = new List<double>();
        for (int i = 1; i < angularRates.Count; i++)
        {
            double dt = buildTimestamps[i + 1] - buildTimestamps[i];
            if (dt <= 0) continue;
            jerks.Add(Math.Abs((angularRates[i] - angularRates[i - 1]) / dt));
        }

        double meanJerk = jerks.Count > 0 ? jerks.Average() : 0;
        double maxJerk = jerks.Count > 0 ? jerks.Max() : 0;

        //Debug.WriteLine($"[EdgeBuild] --- METRICA A: JERK ---");
        //Debug.WriteLine($"[EdgeBuild]   meanJerk: {meanJerk:F6} °/ms²");
        //Debug.WriteLine($"[EdgeBuild]   maxJerk:  {maxJerk:F6} °/ms²");

        const double jerkFluidThreshold = 0.0008; 
        const double jerkImpulseThreshold = 0.0030;
        double jerkScore = 1.0 - Clamp01(
            (meanJerk - jerkFluidThreshold) / (jerkImpulseThreshold - jerkFluidThreshold));

        //Debug.WriteLine($"[EdgeBuild]   jerkScore: {jerkScore:F4}");

        // ── METRICA B – Monotonicità ──────────────────────────────────────────
        int sign = Math.Sign(buildRolls[peakIndex]);
        int inversions = 0;
        for (int i = 1; i < bN; i++)
        {
            double delta = (buildRolls[i] - buildRolls[i - 1]) * sign;
            if (delta < -0.5) inversions++;
        }

        double inversionRatio = (double)inversions / (bN - 1);

        //Debug.WriteLine($"[EdgeBuild] --- METRICA B: MONOTONICITÀ ---");
        //Debug.WriteLine($"[EdgeBuild]   Inversioni: {inversions} su {bN - 1} step ({inversionRatio * 100:F1}%)");

        double monotonicityScore = Clamp01(1.0 - inversionRatio * 3.0);

        //Debug.WriteLine($"[EdgeBuild]   monotonicityScore: {monotonicityScore:F4}");

        
        // ── METRICA C – Progressività R² ─────────────────────────────────────
        double rSquared = CalculateRSquared(buildTimestamps, buildRolls);

        //Debug.WriteLine($"[EdgeBuild] --- METRICA D: PROGRESSIVITÀ ---");
        //Debug.WriteLine($"[EdgeBuild]   R²: {rSquared:F4}");

        double progressivityScore = Clamp01(rSquared);

        //Debug.WriteLine($"[EdgeBuild]   progressivityScore: {progressivityScore:F4}");

        // ── Score finale ──────────────────────────────────────────────────────
        const double wJerk = 0.45;
        const double wMonotonicity = 0.25;
        const double wProgressivity = 0.30;

        double finalScore =
            wJerk * jerkScore +
            wMonotonicity * monotonicityScore +
            wProgressivity * progressivityScore;

        //Debug.WriteLine($"[EdgeBuild] --- SCORE FINALE ---");
        //Debug.WriteLine($"[EdgeBuild]   Jerk        ({wJerk * 100:F0}%): {jerkScore:F4} → contributo {wJerk * jerkScore:F4}");
        //Debug.WriteLine($"[EdgeBuild]   Monotonicità({wMonotonicity * 100:F0}%): {monotonicityScore:F4} → contributo {wMonotonicity * monotonicityScore:F4}");
        //Debug.WriteLine($"[EdgeBuild]   Progressività({wProgressivity * 100:F0}%): {progressivityScore:F4} → contributo {wProgressivity * progressivityScore:F4}");
        //Debug.WriteLine($"[EdgeBuild]   *** FINAL SCORE: {finalScore:F4} ({finalScore * 100:F1}%) ***");

        return Math.Round(finalScore*100);
    }
    
    public static double CalculateEarlyLoading(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 10) return 0;

        double excellentThreshold = 0.25;  // entro il 30% → score 100
        double midScore = 60.0;            // score a metà curva (0.5)
        double minScore = 10.0;

        int totalFrames = turnData.Count;

        var rollPeak = GetRollPeak(turnData);
        var gPeak = GetGForcePeak(turnData);

        double apexIndex = (rollPeak.Index * 0.0) + (gPeak.Index * 1.0);
        double relativePosition = apexIndex / (totalFrames - 1);
        Debug.WriteLine($"[EarlyLoading]   relativePosition: {relativePosition:F2} (apexIndex:{apexIndex} - totalFrames:{totalFrames} - gPeak:{gPeak.Index})");
        double score;

        if (relativePosition <= excellentThreshold)
        {
            // Zona eccellente: score pieno
            score = 100.0;
        }
        else
        {
            // Zona decrescente: interpolazione esponenziale da 100 a minScore
            // Calibrata in modo che a relativePosition=0.5 si ottenga midScore
            // Risolviamo: midScore = 100 - (100 - minScore) * t^k
            // dove t = (pos - excellentThreshold) / (1.0 - excellentThreshold)
            // e k è ricavato dalla condizione sul midScore a pos=0.5

            double t50 = (0.5 - excellentThreshold) / (1.0 - excellentThreshold);
            double k = Math.Log((100.0 - midScore) / (100.0 - minScore)) / Math.Log(t50);

            double t = (relativePosition - excellentThreshold) / (1.0 - excellentThreshold);
            score = 100.0 - (100.0 - minScore) * Math.Pow(t, k);
        }

        /*System.Diagnostics.Debug.WriteLine(
            $"[ApexTiming] RollPeak@{rollPeak.Index} | GPeak@{gPeak.Index} | " +
            $"ApexIdx:{apexIndex:F1} | RelPos:{relativePosition:P1} | Score:{score:F1}");*/

        return Math.Round(Math.Clamp(score, minScore, 100.0), 1);
    }

    // UTILITIES
    private static (int Index, double MaxRoll) GetRollPeak(List<SkiFrame> data)
    {
        if (data == null || data.Count == 0) return (0, 0);

        double avgGyro = data.Average(f => f.CombinedGyro);
        bool isTurnToLeft = avgGyro < 0;
        

        int peakIdx = 0;
        double maxR = 0;

        for (int i = 0; i < data.Count; i++)
        {
            double current = isTurnToLeft ? Math.Abs(data[i].Right.SmoothRoll) : Math.Abs(data[i].Left.SmoothRoll);
            //System.Diagnostics.Debug.WriteLine($"[ANALYZER] comparing {current} to {maxR}");
            if (current > maxR)
            {
                maxR = current;
                peakIdx = i;
            }
        }
        return (peakIdx, maxR);
    }

    private static (int Index, double MaxRoll) GetGForcePeak(List<SkiFrame> data)
    {
        if (data == null || data.Count == 0) return (0, 0);

        double avgGyro = data.Average(f => f.CombinedGyro);
        bool isTurnToLeft = avgGyro < 0;

        // 1. Trova il minimo di G nella curva (momento di scarico/transizione)
        int minIndex = 0;
        double minG = double.MaxValue;

        for (int i = 0; i < data.Count; i++)
        {
            double g = isTurnToLeft ? data[i].Right.AdjustedG : data[i].Left.AdjustedG;
            if (g < minG)
            {
                minG = g;
                minIndex = i;
            }
        }

        // 2. Cerca il picco SOLO dopo il minimo
        int windowSize = 10;
        int bestIndex = minIndex;
        double bestAvg = 0;

        for (int i = minIndex + windowSize; i < data.Count - windowSize; i++)
        {
            double windowAvg = 0;
            for (int j = i - windowSize; j <= i + windowSize; j++)
            {
                windowAvg += isTurnToLeft ? data[j].Right.AdjustedG : data[j].Left.AdjustedG;
            }
            windowAvg /= (windowSize * 2 + 1);

            if (windowAvg > bestAvg)
            {
                bestAvg = windowAvg;
                bestIndex = i;
            }
        }

        return (bestIndex, bestAvg);
    }

    private static double GetAverageRollInWindow(List<SkiFrame> data, int peakIndex)
    {
        int windowSize = 10;
        int start = Math.Max(0, peakIndex - windowSize);
        int end = Math.Min(data.Count - 1, peakIndex + windowSize);

        double sum = 0;
        int count = 0;

        double avgGyro = data.Average(f => f.CombinedGyro);
        bool isTurnToLeft = avgGyro < 0;

        //System.Diagnostics.Debug.WriteLine($"[ANALYZER] isTurnToLeft: {isTurnToLeft}");

        for (int i = start; i <= end; i++)
        {
            var f = data[i];
            double angle;
            if (isTurnToLeft)
            {
                angle = f.Right.SmoothRoll;
            }
            else
            {
                angle = f.Left.SmoothRoll;
            }
            //System.Diagnostics.Debug.WriteLine($"[ANALYZER] adding: {angle} to {sum}");
            sum += angle;
            count++;
            //System.Diagnostics.Debug.WriteLine($"[ANALYZER] new sum: {sum}, new count {count}");
        }
        //System.Diagnostics.Debug.WriteLine($"[ANALYZER] returning: {sum}/{count}");
        return Math.Abs(sum / count);
    }

    private static double GetAverageGInWindow(List<SkiFrame> data, int peakIndex)
    {
        int windowSize = 10; 
        int start = Math.Max(0, peakIndex - windowSize);
        int end = Math.Min(data.Count - 1, peakIndex + windowSize);

        double sumG = 0;
        int count = 0;

        double avgGyro = data.Average(f => f.CombinedGyro);
        bool isTurnToLeft = avgGyro < 0;

        for (int i = start; i <= end; i++)
        {
            var f = data[i];
            double g;
            if (isTurnToLeft)
            {
                g = f.Right.AdjustedG;
            }
            else
            {
                g = f.Left.AdjustedG;
            }
            //System.Diagnostics.Debug.WriteLine($"[ANALYZER] adding: {g} to {sumG}");
            sumG += g;
            count++;
        }
        //System.Diagnostics.Debug.WriteLine($"[ANALYZER] returning: {sumG} / {count} = {sumG/count}");
        return sumG / count;
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

        if (ssXX == 0 || ssYY == 0) return 0;

        double r = ssXY / Math.Sqrt(ssXX * ssYY);
        return r * r; // R²
    }
    private static double Clamp01(double value)
        => Math.Max(0.0, Math.Min(1.0, value));

}