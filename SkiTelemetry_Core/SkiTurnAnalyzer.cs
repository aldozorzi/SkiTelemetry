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

        // 1. Troviamo il frame che ha il roll più alto (il nostro candidato picco)
        var peakFrame = turnData.OrderByDescending(f =>
            Math.Max(Math.Abs(f.Left.SmoothRoll), Math.Abs(f.Right.SmoothRoll))
        ).First();

        int peakIndex = turnData.IndexOf(peakFrame);

        // 2. Prendiamo una finestra di 5-7 campioni attorno al picco
        // (circa 100-140ms di stabilità sulla lamina)
        int windowSize = 3; // 3 a sx, 1 al centro, 3 a dx = 7 frame
        int start = Math.Max(0, peakIndex - windowSize);
        int count = Math.Min(turnData.Count - start, windowSize * 2 + 1);

        var peakWindow = turnData.GetRange(start, count);

        // 3. Facciamo la media su questa finestra
        double stableRoll = peakWindow.Average(f =>
            Math.Max(Math.Abs(f.Left.SmoothRoll), Math.Abs(f.Right.SmoothRoll))
        );

        // 4. Applichiamo un fattore di correzione realistico
        // Se la GForce è alta, l'angolo misurato è sempre sovrastimato.
        // Un trucco del mestiere è limitare il risultato o scalarlo leggermente.
        return Math.Round(stableRoll);
    }
    public static double CalculateMaxGForce(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count == 0) return 0;

        // Calcoliamo la Magnitude (Risultante) per ogni istante e prendiamo il massimo
        double maxG = turnData.Max(f => {
            // Sci Sinistro
            double gLeft = Math.Sqrt(
                Math.Pow(f.Left.SmoothAccX, 2) +
                Math.Pow(f.Left.SmoothAccY, 2) +
                Math.Pow(f.Left.SmoothAccZ, 2)
            );

            // Sci Destro
            double gRight = Math.Sqrt(
                Math.Pow(f.Right.SmoothAccX, 2) +
                Math.Pow(f.Right.SmoothAccY, 2) +
                Math.Pow(f.Right.SmoothAccZ, 2)
            );

            // Media dei due sci per quell'istante
            return (gLeft + gRight) / 2.0;
        });

        // Restituiamo il valore arrotondato (es. 2.15)
        return Math.Round(maxG, 2);
    }
    public static SensorSide DetermineOuterFoot(List<SkiFrame> turnData)
    {
        double avgY = turnData.Average(f => (f.Left.GyroZ + f.Right.GyroZ) / 2.0);

        // Se avgY > 0 -> Curva a Sinistra -> Piede Esterno DESTRO
        // Se avgY < 0 -> Curva a Destra -> Piede Esterno SINISTRO
        // (Se è ancora invertito, inverti i ritorni qui sotto)
        return avgY < 0 ? SensorSide.Right : SensorSide.Left;
    }
    public static double CalculateSimilarity(List<SkiFrame> turnData)
    {
        if (turnData == null || turnData.Count == 0) return 0;

        // Calcoliamo la differenza di inclinazione media tra i due sci
        // Usiamo il valore assoluto della differenza tra i Roll assoluti
        double avgDiff = turnData.Average(f =>
            Math.Abs(Math.Abs(f.Left.SmoothRoll) - Math.Abs(f.Right.SmoothRoll))
        );

        // LOGICA DI PUNTEGGIO:
        // 0° diff -> 100%
        // 5° diff -> 90% (accettabile, è quasi parallelo)
        // 10° diff -> 75% (inizia a vedersi l'errore)
        // 20° diff -> 40% (gambe decisamente non parallele)

        double score = 100 * Math.Exp(-0.04 * avgDiff);

        return Math.Clamp(Math.Round(score, 0), 0, 100);
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
        if (turnData == null || turnData.Count < 10) return 0;

        double maxRoll = CalculateMaxEdgeAngle(turnData);
        double maxG = turnData.Max(f => CalculateFrameG(f));
        double avgG = turnData.Average(f => CalculateFrameG(f));

        // 1. PILASTRO PRESSIONE (40 punti)
        // Se spingi, stai carvando. Sotto 1.2G non è carving. Sopra 1.8G è ottimo.
        double pressureScore = Math.Clamp((maxG - 1.1) * 50, 0, 40);

        // 2. PILASTRO INCLINAZIONE (40 punti)
        // Premia l'angolo mediato. 45° è il target per il "punteggio pieno" in questa sezione.
        double angleScore = Math.Clamp((maxRoll - 15) * 1.3, 0, 40);

        // 3. PILASTRO STABILITÀ (20 punti)
        // Calcoliamo quanto è "fermo" lo sci. Se GyroY (vibrazione laterale) è bassa, 
        // significa che lo sci è in un binario.
        double lateralVibration = turnData.Average(f => Math.Abs(f.Left.SmoothGyroY + f.Right.SmoothGyroY) / 2.0);
        double stabilityScore = Math.Clamp(20 - (lateralVibration * 0.5), 0, 20);

        // TOTALE BASE
        double totalScore = pressureScore + angleScore + stabilityScore;

        // IL "BOOSTER" PER LE CURVE TIRATE
        // Se hai più di 45° e più di 1.6G, non puoi essere sotto l'80. Punto.
        if (maxRoll > 40 && maxG > 1.6)
        {
            totalScore = Math.Max(totalScore, 75 + (maxRoll - 40));
        }

        // PENALITÀ DERAPATA (Se la rotazione Z è incostante)
        // Se il picco di rotazione è troppo lontano dalla media, c'è stato uno "scatto" (derapata)
        /*double maxZ = turnData.Max(f => Math.Abs(f.CombinedGyroZ ?? 0));
        double avgZ = turnData.Average(f => Math.Abs(f.CombinedGyroZ ?? 0));
        if (maxZ / (avgZ + 1) > 2.5)
        {
            totalScore -= 20;
        }*/

        return Math.Clamp(Math.Round(totalScore), 0, 100);
    }

    private static double CalculateFrameG(SkiFrame f)
    {
        // Calcoliamo la Magnitude dell'accelerazione per entrambi gli sci
        // Usiamo la formula: sqrt(x² + y² + z²)
        double gLeft = Math.Sqrt(Math.Pow(f.Left.SmoothAccX, 2) +
                                 Math.Pow(f.Left.SmoothAccY, 2) +
                                 Math.Pow(f.Left.SmoothAccZ, 2));

        double gRight = Math.Sqrt(Math.Pow(f.Right.SmoothAccX, 2) +
                                  Math.Pow(f.Right.SmoothAccY, 2) +
                                  Math.Pow(f.Right.SmoothAccZ, 2));

        // Restituiamo la media dei due sci per quel preciso istante
        return (gLeft + gRight) / 2.0;
    }
}