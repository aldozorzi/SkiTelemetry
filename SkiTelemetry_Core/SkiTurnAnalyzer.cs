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

    public static void DiagnosticTurnAnalysis(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 5) return;

        // 1. Parametri di picco (usando la logica Max per vedere il potenziale)
        double maxG = CalculateMaxGForce(turnData);
        double maxGyroY = turnData.Max(f => Math.Abs(f.Left.SmoothGyroY + f.Right.SmoothGyroY) / 2.0);
        double avgRoll = turnData.Average(f => Math.Abs(f.Left.SmoothRoll + f.Right.SmoothRoll) / 2.0);

        // 2. Calcolo Velocità Apparente (m/s e km/h) all'apice
        double omegaRad = maxGyroY * (Math.PI / 180.0);
        double accCentripetal = 9.80665 * Math.Sqrt(Math.Max(Math.Pow(maxG, 2) - 1.0, 0));

        double vMS = (omegaRad > 0.1) ? (accCentripetal / omegaRad) : 0;
        double vKmH = vMS * 3.6;

        // 3. Rapporto di Efficienza (G-Force per unità di rotazione)
        // Più è alto, più la curva è "carica" e condotta. 
        // Se è basso, stai girando i piedi senza fare pressione (derapata/pivoting).
        double efficiency = (maxGyroY > 0) ? (maxG / maxGyroY) * 100 : 0;

        // 4. Calcolo Raggio Teorico con questa V
        double radius = (omegaRad > 0.1) ? (vMS / omegaRad) : 0;

        System.Diagnostics.Debug.WriteLine($"[DIAG] Curva");
        System.Diagnostics.Debug.WriteLine($"      -> V-Apparente: {vKmH:F1} km/h | Raggio: {radius:F1}m");
        System.Diagnostics.Debug.WriteLine($"      -> MaxG: {maxG:F2} | MaxGyroY: {maxGyroY:F1} deg/s");
        System.Diagnostics.Debug.WriteLine($"      -> Efficienza (G/Gyro): {efficiency:F2} | Roll Medio: {avgRoll:F1}°");
        System.Diagnostics.Debug.WriteLine("------------------------------------------------------------");
    }
    public static double CalculateCarvingIndex(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 5) return 0;

        // 1. Picchi Asincroni (Memoria: Roll prima, G dopo)
        double maxRoll = turnData.Max(f => Math.Abs(f.Left.SmoothRoll + f.Right.SmoothRoll) / 2.0);
        double maxG = turnData.Max(f => (f.Left.AdjustedG + f.Right.AdjustedG) / 2.0);

        if (maxRoll < 10) return 0;

        // 2. Calcolo soglia di "Taglio Passivo" (G necessaria per non derapare)
        double rollRad = maxRoll * (Math.PI / 180.0);
        double gBinario = 1.0 / Math.Cos(rollRad);

        // 3. Calcolo soglia di "Deformazione Eccellente"
        // Uno sciatore che deforma davvero aggiunge circa un 30-40% di carico extra
        double gOttimale = gBinario * 1.4;

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
    public static double CalculateEdgeBuild(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 10) return 0;

        // 1. Troviamo l'Apex (Picco di inclinazione)
        // GetPeakData deve usare Math.Abs internamente per trovare il massimo modulo
        var peak = GetRollPeak(turnData);
        double maxRoll = peak.MaxRoll;
        int peakIdx = peak.Index;

        // 2. Troviamo il punto di partenza REALE (lo zero della lamina)
        // Non ci fidiamo del primo frame, cerchiamo il minimo assoluto PRIMA del picco
        double startRoll = double.MaxValue;
        int startIdx = 0;

        for (int i = 0; i <= peakIdx; i++)
        {
            double r = Math.Max(Math.Abs(turnData[i].Left.SmoothRoll), Math.Abs(turnData[i].Right.SmoothRoll));
            if (r < startRoll)
            {
                startRoll = r;
                startIdx = i;
            }
        }

        // 3. Calcoliamo la crescita (Delta)
        double deltaRoll = maxRoll - startRoll;
        int duration = peakIdx - startIdx;

        // Se la curva è troppo strozzata o non c'è pendenza, diamo un valore neutro
        if (deltaRoll < 5 || duration < 4) return 70;

        // 4. Calcolo delle Aree (Relative allo startRoll)
        double realArea = 0;
        for (int i = startIdx; i <= peakIdx; i++)
        {
            double current = Math.Max(Math.Abs(turnData[i].Left.SmoothRoll), Math.Abs(turnData[i].Right.SmoothRoll));
            realArea += (current - startRoll);
        }

        // Area di un triangolo che cresce da 0 a deltaRoll in 'duration' frame
        double linearArea = (deltaRoll * (duration + 1)) / 2.0;

        if (linearArea <= 0) return 0;

        double buildRatio = realArea / linearArea;

        // 5. Mapping del punteggio
        // Se Ratio < 1.0 -> Progressivo (Ease-In) -> Punteggio alto
        // Se Ratio > 1.0 -> Impulsivo (Abrupt) -> Punteggio basso
        double score;
        if (buildRatio <= 1.0)
        {
            // Ottimo: premiamo la curva convessa (tipica del carving moderno)
            score = 100.0 - (buildRatio * 30.0); // Da 70 a 100
        }
        else
        {
            // Penalizziamo chi sbatte subito lo spigolo a terra
            score = 70.0 - ((buildRatio - 1.0) * 80.0);
        }

        return Math.Round(Math.Clamp(score, 0, 100), 1);
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


        int peakIdx = 0;
        double maxR = 0;

        for (int i = 0; i < data.Count; i++)
        {
            double current = isTurnToLeft ? Math.Abs(data[i].Right.AdjustedG) : Math.Abs(data[i].Left.AdjustedG);
            //System.Diagnostics.Debug.WriteLine($"[ANALYZER] comparing {current} to {maxR}");
            if (current > maxR)
            {
                maxR = current;
                peakIdx = i;
            }
        }
        return (peakIdx, maxR);
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
   
}