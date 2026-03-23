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
        // Se per qualche motivo la lista è vuota, restituiamo 0
        if (turnData == null || turnData.Count == 0) return 0;

        // Cerchiamo il valore massimo assoluto di SmoothRoll 
        // considerando entrambi gli sci in tutta la durata della curva
        double maxRoll = turnData.Max(f =>
            Math.Max(Math.Abs(f.Left.SmoothRoll), Math.Abs(f.Right.SmoothRoll))
        );

        return Math.Round(maxRoll);
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
        // USIAMO IL GYRO Y PER LA DIREZIONE (visto che sul tuo sensore è quello della rotazione)
        double avgY = turnData.Average(f => (f.Left.GyroY + f.Right.GyroY) / 2.0);

        // Se avgY > 0 -> Curva a Sinistra -> Piede Esterno DESTRO
        // Se avgY < 0 -> Curva a Destra -> Piede Esterno SINISTRO
        // (Se è ancora invertito, inverti i ritorni qui sotto)
        return avgY > 0 ? SensorSide.Right : SensorSide.Left;
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
        if (turnData == null || turnData.Count < 5) return 0;

        // 1. Prendiamo l'inclinazione massima (Roll)
        double maxRoll = turnData.Max(f => Math.Max(Math.Abs(f.Left.SmoothRoll), Math.Abs(f.Right.SmoothRoll)));

        // 2. Calcoliamo la G-Force media
        double avgG = turnData.Average(f => CalculateFrameG(f));

        // 3. LA LOGICA DI CONDUZIONE:
        // In una curva perfetta (carving), la G-Force deve essere coerente con l'angolo.
        // La G teorica di equilibrio è 1 / cos(Roll).
        double theoreticalG = 1.0 / Math.Max(Math.Cos(maxRoll * Math.PI / 180.0), 0.1);

        // 4. Calcolo dell'efficienza (Rapporto tra spinta reale e spinta d'angolo)
        // Se avgG è vicino o superiore a theoreticalG, stai conducendo.
        // Se avgG è molto più basso, lo sci sta scivolando via (derapata).
        double ratio = avgG / theoreticalG;

        // 5. Trasformazione in punteggio 0-100
        // Usiamo una curva che premia chi sta sopra il 70% del rapporto teorico
        double score = ratio * 100;

        // Bonus stabilità: se la vibrazione (GyroZ) è bassa, il carving è pulito
        double vibrationY = turnData.Average(f => Math.Abs(f.Left.SmoothGyroY + f.Right.SmoothGyroY) / 2.0);
        // Se ruoti troppo velocemente rispetto all'angolo, stai "sterzando" col piede (derapata)
        double steeringPenalty = Math.Max(0, (vibrationY / (maxRoll + 1)) - 0.5) * 20;

        double finalScore = score - steeringPenalty;

        // Se sei inclinato oltre i 45° e hai più di 2G, è praticamente impossibile 
        // che tu non stia carvando almeno all'80%
        if (maxRoll > 45 && avgG > 1.8)
        {
            finalScore = Math.Max(finalScore, 85.0);
        }

        return Math.Clamp(Math.Round(finalScore, 0), 0, 100);
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