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
            metrics.Add(MonitorMode.TurnRadius);
            metrics.Add(MonitorMode.EarlyEdging);
            metrics.Add(MonitorMode.ProgressiveEdgeBuild);
            metrics.Add(MonitorMode.TurnShape);
            metrics.Add(MonitorMode.CarvingIndex);
            metrics.Add(MonitorMode.EdgeSimilarity);

            processor.setMetrics(metrics);

            foreach (var p in points)
            {
                processor.Process(p);
            }


            MainThread.BeginInvokeOnMainThread(() => {
                LblRunName.Text = Path.GetFileNameWithoutExtension(path).Replace("_", " ");
                LblTurnCount.Text = detectedTurns.Count.ToString();

                if (detectedTurns.Any())
                {
                    LblAvgAngle.Text = $"{detectedTurns.Average(t => t.EdgeAngle):F1}°";
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