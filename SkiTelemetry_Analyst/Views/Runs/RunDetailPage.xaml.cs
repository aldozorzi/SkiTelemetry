using SkiTelemetry_Core;
using System.Text;

namespace SkiTelemetry_Analyst.Views.Runs;

[QueryProperty(nameof(FilePath), "path")]
public partial class RunDetailPage : ContentPage
{
    private string _filePath = "";
    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = Uri.UnescapeDataString(value);
            OnPropertyChanged();
            LoadRunData(_filePath);
        }
    }

    public RunDetailPage()
    {
        InitializeComponent();
    }

    private void LoadRunData(string path)
    {
        try
        {
            var points = RunParser.ParseBinFile(path);
            var detectedTurns = new List<SkiTurn>();
            var processor = new SkiDataProcessor();
            processor.OnNewTurn += (s, turn) => detectedTurns.Add(turn);

            HashSet<MonitorMode> metrics = new();


            metrics.Add(MonitorMode.EdgeAngle);
            metrics.Add(MonitorMode.GForce);
            //metrics.Add(MonitorMode.TurnRadius);
            metrics.Add(MonitorMode.EarlyEdging);
            metrics.Add(MonitorMode.ProgressiveEdgeBuild);
            metrics.Add(MonitorMode.TurnShape);
            metrics.Add(MonitorMode.CarvingIndex);
            metrics.Add(MonitorMode.EdgeSimilarity);
            metrics.Add(MonitorMode.Foot);

            processor.setMetrics(metrics);


            foreach (var p in points)
            {
                processor.Process(p);
            }
            // aggiunti tutti i punti
            #if WINDOWS
            SkiGraphGenerator.GenerateHtmlReport(points, detectedTurns);
            #endif
            MainThread.BeginInvokeOnMainThread(() => {
                string fileName = Path.GetFileNameWithoutExtension(path);
                string[] parts = fileName.Split('_');

                int year = int.Parse(parts[0]);
                int month = int.Parse(parts[1]);
                int day = int.Parse(parts[2]);
                DateTime dt = new DateTime(year, month, day);

                // 3. Recupero del numero della run (l'ultima parte dopo "RUN")
                string runNumber = parts[4];

                // 4. Formattazione finale: "13 marzo 2026 - Run 2"
                LblRunName.Text = $"{dt.ToString("dd MMMM yyyy", new System.Globalization.CultureInfo("it-IT"))} - Run {runNumber}";
                LblTurnCount.Text = detectedTurns.Count.ToString();

                if (detectedTurns.Any())
                {
                    LblCarvingIndex.Text = $"{detectedTurns.Average(t => t.CarvingIndex):F0}%";
                    LblMaxAngle.Text = $"{detectedTurns.Max(t => t.EdgeAngle):F0}°";
                    LblMaxG.Text = $"{detectedTurns.Max(t => t.GForce):F2}";
                }

                TurnsCollection.ItemsSource = detectedTurns;
            });
        }
        catch (Exception ex)
        {
            DisplayAlertAsync("Errore", $"Impossibile leggere il file: {ex.Message}", "OK");
        }
    }
}