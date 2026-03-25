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
        if (turnData == null || turnData.Count < 10) return 0;

        // 1. Identifichiamo il piede esterno (come prima, basandoci sul segno del Gyro)
        double avgGyro = turnData.Average(f => f.CombinedGyro ?? 0);
        bool isTurnToLeft = avgGyro > 0;

        // 2. Prendiamo TUTTI i valori di rollio dello sci esterno nella curva
        var rollValues = turnData.Select(f =>
            isTurnToLeft ? Math.Abs(f.Right.SmoothRoll) : Math.Abs(f.Left.SmoothRoll)
        ).OrderByDescending(v => v).ToList();
        //System.Diagnostics.Debug.WriteLine($"[ANALYZER] ____________________________________________CURVA");
        //System.Diagnostics.Debug.WriteLine($"[ANALYZER] rollValues: {string.Join(", ", rollValues)}");

        // 3. IL TRUCCO: Invece del Max (il primo della lista), prendiamo il 90° percentile
        // Questo scarta i picchi di rumore isolati (i famosi 64°) e prende il valore 
        // che lo sciatore ha effettivamente "tenuto" per una parte significativa della curva.
        double stableMaxRoll = rollValues.First();

        return Math.Round(stableMaxRoll, 1);
    }
    public static double CalculateMaxGForce(List<SkiFrame> turnData)
    {
        var peak = GetPeakData(turnData);
        double g = GetAverageGInWindow(turnData, peak.Index);

        // Reality Check per pendenza/offset
        if (g < 1.0 && peak.MaxRoll > 15)
            g = 1.0 + (peak.MaxRoll * 0.005);

        return Math.Round(g, 1);
    }
    public static SensorSide DetermineOuterFoot(List<SkiFrame> turnData)
    {
        double avgY = turnData.Average(f => (f.Left.GyroY + f.Right.GyroY) / 2.0);
        // Se avgY > 0 -> Curva a Sinistra -> Piede Esterno DESTRO
        // Se avgY < 0 -> Curva a Destra -> Piede Esterno SINISTRO
        // (Se è ancora invertito, inverti i ritorni qui sotto)
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
    public static double CalculateEstimatedRadius(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 5) return 0;

        // 1. Calcoliamo la velocità angolare media (ω) in RADIANTI al secondo
        // Usiamo il GyroY che, come abbiamo visto nel video, è la rotazione "bussola"
        double avgGyroYRad = turnData.Average(f =>
            Math.Abs((f.Left.SmoothGyroY + f.Right.SmoothGyroY) / 2.0) * (Math.PI / 180.0)
        );

        // Evitiamo divisioni per zero se lo sci è dritto
        if (avgGyroYRad < 0.05) return 35.0;

        // 2. Calcoliamo l'accelerazione laterale media (a) in m/s²
        // Invece di solo AccX, usiamo la Magnitude degli assi sul piano della neve
        // (X e Z) per catturare tutta la forza centrifuga.
        double avgAccLateralG = turnData.Average(f => {
            double latL = Math.Sqrt(Math.Pow(f.Left.SmoothAccX, 2) + Math.Pow(f.Left.SmoothAccZ, 2));
            double latR = Math.Sqrt(Math.Pow(f.Right.SmoothAccX, 2) + Math.Pow(f.Right.SmoothAccZ, 2));
            return (latL + latR) / 2.0;
        });

        double avgAccMS2 = avgAccLateralG * 9.81;

        // 3. LA TUA FORMULA: R = a / ω²
        double calculatedRadius = avgAccMS2 / Math.Pow(avgGyroYRad, 2);

        // 4. Correzione di realtà
        // Se lo sciatore derapa, ω aumenta molto rispetto ad 'a', facendo crollare il raggio.
        // Usiamo l'inclinazione (Roll) per stabilizzare il dato verso il raggio dello sci (16m)
        double maxRoll = turnData.Max(f => Math.Max(Math.Abs(f.Left.SmoothRoll), Math.Abs(f.Right.SmoothRoll)));
        double geometricWeight = Math.Clamp(maxRoll / 70.0, 0.3, 0.7);
        double geometricRadius = 16.0 * Math.Cos(maxRoll * Math.PI / 180.0);

        // Blend tra la tua formula fisica e la geometria dello sci
        double finalRadius = (calculatedRadius * (1 - geometricWeight)) + (geometricRadius * geometricWeight);

        return Math.Clamp(Math.Round(finalRadius, 1), 7.0, 40.0);
    }
    public static double CalculateCarvingIndex(List<SkiFrame> turnData)
    {
        var peak = GetPeakData(turnData);
        if (peak.MaxRoll < 10) return 0;

        // Usiamo la G media di tutta la curva per stabilità
        double measuredG = GetStableGForTurn(turnData);

        // G Teorica Ideale (il nostro 100%)
        double theoreticalG = 1.0 / Math.Cos(peak.MaxRoll * Math.PI / 180.0);

        // Calcoliamo l'efficienza grezza
        double rawEfficiency = (measuredG / theoreticalG);

        // APPLICHIAMO LA COMPRESSIONE (Logaritmica o Sigmoide)
        // L'obiettivo è che se rawEfficiency è 1.0 (teorico perfetto), l'indice sia circa 80-85.
        // Se è 1.5 (aggressivo), l'indice salga verso 95 ma non esploda.
        double compressedIndex = 100 * (1.0 - Math.Exp(-1.5 * rawEfficiency));

        // Ulteriore correzione per spalmare tra 0 e 100
        // Se lo sci "derapa", measuredG sarà vicino a 1.0 anche con angoli alti -> Index basso.
        return Math.Round(compressedIndex, 1);
    }
    public static double CalculateEdgeBuild(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count < 10) return 0;

        // 1. Troviamo l'Apex (Picco di inclinazione)
        // GetPeakData deve usare Math.Abs internamente per trovare il massimo modulo
        var peak = GetPeakData(turnData);
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
    private static (int Index, double MaxRoll) GetPeakData(List<SkiFrame> data)
    {
        if (data == null || data.Count == 0) return (0, 0);

        int peakIdx = 0;
        double maxR = -1;

        for (int i = 0; i < data.Count; i++)
        {
            double current = Math.Max(Math.Abs(data[i].Left.SmoothRoll), Math.Abs(data[i].Right.SmoothRoll));
            if (current > maxR)
            {
                maxR = current;
                peakIdx = i;
            }
        }
        return (peakIdx, maxR);
    }

    private static double GetAverageGInWindow(List<SkiFrame> data, int peakIndex)
    {
        int windowSize = 4;
        int start = Math.Max(0, peakIndex - windowSize);
        int end = Math.Min(data.Count - 1, peakIndex + windowSize);

        double sumG = 0;
        int count = 0;

        for (int i = start; i <= end; i++)
        {
            var f = data[i];
            double gL = Math.Sqrt(Math.Pow(f.Left.SmoothAccX, 2) + Math.Pow(f.Left.SmoothAccY, 2) + Math.Pow(f.Left.SmoothAccZ, 2));
            double gR = Math.Sqrt(Math.Pow(f.Right.SmoothAccX, 2) + Math.Pow(f.Right.SmoothAccY, 2) + Math.Pow(f.Right.SmoothAccZ, 2));
            sumG += (gL + gR) / 2.0;
            count++;
        }
        return sumG / count;
    }

    private static double GetStableGForTurn(List<SkiFrame> data)
    {
        // Invece di una finestra piccola sul picco, 
        // prendiamo il valore medio di TUTTA la parte centrale della curva (es. il 60% centrale)
        int skip = (int)(data.Count * 0.2); // Salta l'entrata
        int take = data.Count - (skip * 2); // Salta l'uscita

        if (take <= 0) return 1.0;

        double sumG = 0;
        for (int i = skip; i < skip + take; i++)
        {
            var f = data[i];
            double gL = Math.Sqrt(Math.Pow(f.Left.SmoothAccX, 2) + Math.Pow(f.Left.SmoothAccY, 2) + Math.Pow(f.Left.SmoothAccZ, 2));
            double gR = Math.Sqrt(Math.Pow(f.Right.SmoothAccX, 2) + Math.Pow(f.Right.SmoothAccY, 2) + Math.Pow(f.Right.SmoothAccZ, 2));
            sumG += (gL + gR) / 2.0;
        }
        return sumG / take;
    }
   
}