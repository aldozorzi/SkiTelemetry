using SkiTelemetry_Core;

int failures = 0;
int checks = 0;

void Check(string name, bool condition, string? detail = null)
{
    checks++;
    if (condition)
    {
        Console.WriteLine($"PASS  {name}");
        return;
    }

    failures++;
    Console.WriteLine($"FAIL  {name}{(detail is null ? "" : $" - {detail}")}");
}

static SkiFrame RawFrame(uint timestamp, float gyroZ, float accX = 0.36f)
{
    return new SkiFrame(
        new SkiDataPoint
        {
            Timestamp = timestamp,
            Side = SensorSide.Left,
            AccX = accX,
            AccY = 1f,
            GyroZ = gyroZ
        },
        new SkiDataPoint
        {
            Timestamp = timestamp,
            Side = SensorSide.Right,
            AccX = accX,
            AccY = 1f,
            GyroZ = gyroZ
        });
}

static SkiFrame AnalyzerFrame(uint timestamp, double rollLeft, double rollRight, double gLeft, double gRight, double gyroZ)
{
    return new SkiFrame(
        new SkiDataPoint
        {
            Timestamp = timestamp,
            Side = SensorSide.Left,
            SmoothRoll = rollLeft,
            AdjustedG = gLeft,
            SmoothGyroZ = gyroZ
        },
        new SkiDataPoint
        {
            Timestamp = timestamp,
            Side = SensorSide.Right,
            SmoothRoll = rollRight,
            AdjustedG = gRight,
            SmoothGyroZ = gyroZ
        });
}

// 1. RunParser must read the last complete record of a file whose size is a multiple of 52 bytes.
string binPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
const int records = 3;
using (var writer = new BinaryWriter(File.Create(binPath)))
{
    for (int i = 0; i < records; i++)
    {
        writer.Write((uint)(1000 + i * 20));
        for (int j = 0; j < 12; j++)
            writer.Write((float)(i * 100 + j));
    }
}

var parsed = RunParser.ParseBinFile(binPath);
Check("RunParser legge tutti i record", parsed.Count == records, $"attesi {records}, letti {parsed.Count}");
Check("RunParser legge l'ultimo timestamp", parsed.Count > 0 && parsed[^1].Left.Timestamp == 1040,
    parsed.Count > 0 ? parsed[^1].Left.Timestamp.ToString() : "nessun frame");
File.Delete(binPath);

// 2. Edge angle must not be rounded before it is used by downstream metrics.
var edgeFrames = Enumerable.Range(0, 20)
    .Select(i => AnalyzerFrame((uint)(i * 20), 20.0, 30.4, 1.0, 1.2, 50.0))
    .ToList();
double edge = SkiTurnAnalyzer.CalculateMaxEdgeAngle(edgeFrames);
Check("CalculateMaxEdgeAngle mantiene i decimali", Math.Abs(edge - 30.4) < 0.001, edge.ToString("F3"));

// 3. A short turn with the G peak near the end must still report the peak.
var shortTurn = Enumerable.Range(0, 16)
    .Select(i => AnalyzerFrame(
        (uint)(i * 20),
        20.0,
        30.0,
        1.0,
        i < 5 ? 0.9 : 2.5,
        50.0))
    .ToList();
double shortG = SkiTurnAnalyzer.CalculateMaxGForce(shortTurn);
Check("CalculateMaxGForce gestisce curve corte e picco a fine curva", shortG >= 2.4, shortG.ToString("F2"));

// 3b. The reported G must be the true maximum of the curve, even when it comes after the apex.
var apexTurn = Enumerable.Range(0, 21)
    .Select(i => AnalyzerFrame(
        (uint)(i * 20),
        i <= 10 ? i * 3.0 : (20 - i) * 3.0,
        i <= 10 ? i * 3.0 : (20 - i) * 3.0,
        i == 10 ? 2.4 : (i >= 17 ? 4.0 : 1.1),
        i == 10 ? 2.4 : (i >= 17 ? 4.0 : 1.1),
        50.0))
    .ToList();
double apexG = SkiTurnAnalyzer.CalculateMaxGForce(apexTurn);
Check("CalculateMaxGForce restituisce il massimo vero", Math.Abs(apexG - 4.0) < 0.05, apexG.ToString("F2"));
TurnMarkers apexMarkers = SkiTurnAnalyzer.GetMarkers(apexTurn);
Check("GetMarkers colloca il picco G nel massimo reale", apexMarkers.GPeakIndex == 17,
    apexMarkers.GPeakIndex.ToString());

// 4. A peak at the very beginning has no build phase to score.
var immediatePeak = Enumerable.Range(0, 10)
    .Select(i => AnalyzerFrame((uint)(i * 20), 40.0 - i * 2, 40.0 - i * 2, 1.2, 1.2, 50.0))
    .ToList();
double build = SkiTurnAnalyzer.CalculateEdgeBuild(immediatePeak);
Check("CalculateEdgeBuild non restituisce 0.5 su scala 0-100", build == 0.0, build.ToString("F2"));

// 5. Similarity must reward matching edges and reject opposite edges.
var matchingEdges = new[] { 10.0, 20.0, 30.0, 40.0, 30.0, 20.0, 10.0 };
var matchingTurn = matchingEdges
    .Select((roll, i) => AnalyzerFrame((uint)(i * 20), roll, roll, 1.5, 1.5, 50.0))
    .ToList();
double matchingSimilarity = SkiTurnAnalyzer.CalculateSimilarity(matchingTurn);
Check("CalculateSimilarity premia spigoli coerenti", matchingSimilarity >= 80, matchingSimilarity.ToString("F1"));

var oppositeTurn = matchingEdges
    .Select((roll, i) => AnalyzerFrame((uint)(i * 20), -roll, roll, 1.5, 1.5, 50.0))
    .ToList();
double oppositeSimilarity = SkiTurnAnalyzer.CalculateSimilarity(oppositeTurn);
Check("CalculateSimilarity azzera spigoli opposti", oppositeSimilarity == 0, oppositeSimilarity.ToString("F1"));

// 6. A progressive edge build must score well; Populate must agree with the single metrics.
var progressiveTurn = Enumerable.Range(0, 30)
    .Select(i =>
    {
        double roll = i * 1.5;
        double g = 1.0 + i * 0.03;
        return AnalyzerFrame((uint)(i * 20), roll, roll, g, g, 50.0);
    })
    .ToList();
double progressiveBuild = SkiTurnAnalyzer.CalculateEdgeBuild(progressiveTurn);
Check("CalculateEdgeBuild premia una presa progressiva e caricata", progressiveBuild >= 70, progressiveBuild.ToString("F1"));

var unloadedBuildTurn = Enumerable.Range(0, 30)
    .Select(i =>
    {
        double roll = i * 1.5;
        return AnalyzerFrame((uint)(i * 20), roll, roll, 1.0, 1.0, 50.0);
    })
    .ToList();
double unloadedBuild = SkiTurnAnalyzer.CalculateEdgeBuild(unloadedBuildTurn);
Check("CalculateEdgeBuild penalizza una presa senza carico", unloadedBuild < progressiveBuild,
    $"{unloadedBuild:F1} vs {progressiveBuild:F1}");

var jerkyBuildTurn = Enumerable.Range(0, 30)
    .Select(i =>
    {
        double roll = i * 1.5 + (i % 2 == 0 ? 4.0 : -4.0);
        double g = 1.0 + i * 0.03;
        return AnalyzerFrame((uint)(i * 20), roll, roll, g, g, 50.0);
    })
    .ToList();
double jerkyBuild = SkiTurnAnalyzer.CalculateEdgeBuild(jerkyBuildTurn);
Check("CalculateEdgeBuild premia una presa liscia", progressiveBuild > jerkyBuild,
    $"{progressiveBuild:F1} vs {jerkyBuild:F1}");

var fallingLoadTurn = Enumerable.Range(0, 30)
    .Select(i =>
    {
        double roll = i * 1.5;
        double g = 2.0 - i * 0.03;
        return AnalyzerFrame((uint)(i * 20), roll, roll, g, g, 50.0);
    })
    .ToList();
double fallingLoadBuild = SkiTurnAnalyzer.CalculateEdgeBuild(fallingLoadTurn);
Check("CalculateEdgeBuild penalizza un carico che cala", fallingLoadBuild < progressiveBuild,
    $"{fallingLoadBuild:F1} vs {progressiveBuild:F1}");

var populatedTurn = new SkiTurn();
SkiTurnAnalyzer.Populate(progressiveTurn, populatedTurn);
Check("Populate mantiene EdgeAngle coerente",
    Math.Abs(populatedTurn.EdgeAngle.GetValueOrDefault() - SkiTurnAnalyzer.CalculateMaxEdgeAngle(progressiveTurn)) < 0.001,
    populatedTurn.EdgeAngle.GetValueOrDefault().ToString("F3"));
Check("Populate mantiene GForce coerente",
    Math.Abs(populatedTurn.GForce.GetValueOrDefault() - SkiTurnAnalyzer.CalculateMaxGForce(progressiveTurn)) < 0.001,
    populatedTurn.GForce.GetValueOrDefault().ToString("F2"));
Check("Populate assegna il piede esterno", populatedTurn.Foot == SensorSide.Right, populatedTurn.Foot.ToString());

// 7. Early loading: tempo per raggiungere l'80% del picco rispetto all'apice.
static double RollProfile(int i) => i <= 6 ? i * 5.0 : Math.Max(15.0, (12 - i) * 5.0);

var earlyLoadTurn = Enumerable.Range(0, 20)
    .Select(i =>
    {
        double roll = RollProfile(i);
        double g = i is >= 4 and <= 8 ? 2.2 : 1.2;
        return AnalyzerFrame((uint)(i * 20), roll, roll, g, g, 50.0);
    })
    .ToList();
double earlyLoading = SkiTurnAnalyzer.CalculateEarlyLoading(earlyLoadTurn);
Check("CalculateEarlyLoading: 80% del picco prima dell'apice", Math.Abs(earlyLoading - 93.3) < 1.5,
    earlyLoading.ToString("F1"));

var centeredLoadTurn = Enumerable.Range(0, 20)
    .Select(i =>
    {
        double roll = RollProfile(i);
        double g = i == 6 ? 2.2 : 1.2;
        return AnalyzerFrame((uint)(i * 20), roll, roll, g, g, 50.0);
    })
    .ToList();
double centeredLoading = SkiTurnAnalyzer.CalculateEarlyLoading(centeredLoadTurn);
Check("CalculateEarlyLoading: picco coincidente con l'apice -> 100", Math.Abs(centeredLoading - 100.0) < 0.1,
    centeredLoading.ToString("F1"));

var lateLoadTurn = Enumerable.Range(0, 20)
    .Select(i =>
    {
        double roll = RollProfile(i);
        double g = i >= 15 ? 2.2 : 1.2;
        return AnalyzerFrame((uint)(i * 20), roll, roll, g, g, 50.0);
    })
    .ToList();
double lateLoading = SkiTurnAnalyzer.CalculateEarlyLoading(lateLoadTurn);
Check("CalculateEarlyLoading: carico tardivo -> ~70", Math.Abs(lateLoading - 70.0) < 1.5,
    lateLoading.ToString("F1"));
Check("CalculateEarlyLoading premia il carico al picco di rollio", earlyLoading > lateLoading + 20,
    $"{earlyLoading:F1} vs {lateLoading:F1}");

var flatLoadTurn = Enumerable.Range(0, 20)
    .Select(i =>
    {
        double roll = RollProfile(i);
        return AnalyzerFrame((uint)(i * 20), roll, roll, 1.2, 1.2, 50.0);
    })
    .ToList();
Check("CalculateEarlyLoading: carico piatto -> 0",
    SkiTurnAnalyzer.CalculateEarlyLoading(flatLoadTurn) == 0,
    SkiTurnAnalyzer.CalculateEarlyLoading(flatLoadTurn).ToString("F1"));

var shortLoadTurn = Enumerable.Range(0, 9)
    .Select(i =>
    {
        double roll = RollProfile(i);
        return AnalyzerFrame((uint)(i * 20), roll, roll, 2.2, 2.2, 50.0);
    })
    .ToList();
Check("CalculateEarlyLoading: curva corta -> 0",
    SkiTurnAnalyzer.CalculateEarlyLoading(shortLoadTurn) == 0,
    SkiTurnAnalyzer.CalculateEarlyLoading(shortLoadTurn).ToString("F1"));

var lowRollLoadTurn = Enumerable.Range(0, 20)
    .Select(i =>
    {
        double roll = RollProfile(i) * 0.1;
        double g = i is >= 4 and <= 8 ? 2.2 : 1.2;
        return AnalyzerFrame((uint)(i * 20), roll, roll, g, g, 50.0);
    })
    .ToList();
Check("CalculateEarlyLoading: rollio basso -> 0",
    SkiTurnAnalyzer.CalculateEarlyLoading(lowRollLoadTurn) == 0,
    SkiTurnAnalyzer.CalculateEarlyLoading(lowRollLoadTurn).ToString("F1"));

// 7b. Performance Index: premia il carico sullo sci esterno e penalizza la derapata.
// L'outer side e' Right quando il gyro combinato e' positivo.
static List<SkiFrame> CarveTurn(double edge, double outerG, double innerG, int n = 40, double gyroZ = 50.0)
{
    var list = new List<SkiFrame>();
    for (int i = 0; i < n; i++)
    {
        double roll = edge * Math.Sin(Math.PI * i / (n - 1));
        list.Add(AnalyzerFrame((uint)(i * 20), roll * 0.5, roll, innerG, outerG, gyroZ));
    }
    return list;
}

const double piEdge = 30.0;
double gBin30 = 1.0 / Math.Cos(piEdge * Math.PI / 180.0);

double piOnEdge = SkiTurnAnalyzer.CalculatePerformanceIndex(CarveTurn(piEdge, gBin30, gBin30 * 0.6));
double piPushed = SkiTurnAnalyzer.CalculatePerformanceIndex(CarveTurn(piEdge, gBin30 * 1.30, gBin30 * 0.6));
double piSkid = SkiTurnAnalyzer.CalculatePerformanceIndex(CarveTurn(piEdge, gBin30 * 0.70, gBin30 * 0.6));

Check("PI: carico al binario -> ~60", Math.Abs(piOnEdge - 60.0) < 2.0, piOnEdge.ToString("F1"));
Check("PI: carico spinto -> 100", piPushed >= 99.0, piPushed.ToString("F1"));
Check("PI: derapata -> sotto 60", piSkid < 55.0, piSkid.ToString("F1"));
Check("PI: cresce col carico sull'esterno", piPushed > piOnEdge && piOnEdge > piSkid,
    $"{piSkid:F1} / {piOnEdge:F1} / {piPushed:F1}");

// Lo spike di rilascio a fine curva non deve alterare il punteggio (il PI usa l'apice).
static List<SkiFrame> CarveTurnWithReleaseSpike(double edge, double apexG, double releaseG, int n = 40)
{
    var list = new List<SkiFrame>();
    for (int i = 0; i < n; i++)
    {
        double roll = edge * Math.Sin(Math.PI * i / (n - 1));
        double g = i >= n - 4 ? releaseG : apexG;
        list.Add(AnalyzerFrame((uint)(i * 20), roll * 0.5, roll, apexG * 0.5, g, 50.0));
    }
    return list;
}

double piSpike = SkiTurnAnalyzer.CalculatePerformanceIndex(CarveTurnWithReleaseSpike(piEdge, gBin30 * 1.30, 4.0));
Check("PI: immune dallo spike di rilascio", Math.Abs(piSpike - piPushed) < 1.0,
    $"{piSpike:F1} vs {piPushed:F1}");

// Basso angolo con carico: non deve essere azzerato dalla penalita' (ora continua).
double gBinLow = 1.0 / Math.Cos(18.0 * Math.PI / 180.0);
double piLowAngle = SkiTurnAnalyzer.CalculatePerformanceIndex(CarveTurn(18.0, gBinLow * 1.25, gBinLow * 0.6));
Check("PI: basso angolo carico non azzerato", piLowAngle > 40.0 && piLowAngle <= 100.0,
    piLowAngle.ToString("F1"));

// Forza G mostrata: massimo vero della curva. Il PI continua a usare l'apice.
var gSpikeTurn = new SkiTurn();
SkiTurnAnalyzer.Populate(CarveTurnWithReleaseSpike(piEdge, gBin30 * 1.30, 4.0), gSpikeTurn);
Check("GForce mostra il massimo vero, anche dopo l'apice",
    Math.Abs(gSpikeTurn.GForce.GetValueOrDefault() - 4.0) < 0.05,
    gSpikeTurn.GForce.GetValueOrDefault().ToString("F2"));
Check("PI resta coerente con il carico all'apice",
    Math.Abs(gSpikeTurn.PerformanceIndex.GetValueOrDefault() - piPushed) < 1.0,
    $"{gSpikeTurn.PerformanceIndex.GetValueOrDefault():F1} vs {piPushed:F1}");

// 8. Reset must clear the turn detector so a new session cannot inherit the previous accumulator.
var processor = new SkiDataProcessor();
var emitted = new List<SkiTurn>();
processor.OnNewTurn += (_, turn) => emitted.Add(turn);

for (int i = 0; i < 45; i++)
    processor.Process(RawFrame((uint)(i * 20), 50f));

processor.Reset();

for (int i = 0; i < 45; i++)
    processor.Process(RawFrame((uint)(10000 + i * 20), -50f));

for (int i = 0; i < 30; i++)
    processor.Process(RawFrame((uint)(10000 + 45 * 20 + i * 20), 0f));

Check("Reset scarta la curva in corso", emitted.Count == 1, $"curve emesse: {emitted.Count}");
Check("Reset fa ripartire la nuova curva dal primo frame",
    emitted.Count == 1 && emitted[0].StartTimestamp == 10000,
    emitted.Count == 1 ? emitted[0].StartTimestamp.ToString() : "nessuna curva");

Console.WriteLine();
Console.WriteLine($"{checks - failures}/{checks} test superati");
return failures == 0 ? 0 : 1;
