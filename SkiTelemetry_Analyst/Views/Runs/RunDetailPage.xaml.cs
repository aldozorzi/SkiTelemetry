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
            List<SkiFrame> frames = RunParser.ParseBinFile(path);
            var detectedTurns = new List<SkiTurn>();
            SkiDataProcessor processor = new SkiDataProcessor();
            processor.OnNewTurn += (s, turn) => detectedTurns.Add(turn);

            HashSet<MonitorMode> metrics = new();


            metrics.Add(MonitorMode.EdgeAngle);
            metrics.Add(MonitorMode.GForce);
            //metrics.Add(MonitorMode.TurnRadius);
            metrics.Add(MonitorMode.EarlyLoading);
            metrics.Add(MonitorMode.ProgressiveEdgeBuild);
            metrics.Add(MonitorMode.TurnTime);
            metrics.Add(MonitorMode.PerformanceIndex);
            metrics.Add(MonitorMode.EdgeSimilarity);
            metrics.Add(MonitorMode.Foot);

            processor.setMetrics(metrics);


            foreach (var f in frames)
            {
                processor.Process(f);
            }
            // aggiunti tutti i punti
            #if WINDOWS
            SkiGraphGenerator.GenerateHtmlReport(frames, detectedTurns);
            #endif
            MainThread.BeginInvokeOnMainThread(() => {
                string fileName = Path.GetFileNameWithoutExtension(path);
                
                string[] parts = fileName.Split('_');
                int year = int.Parse(parts[0]);
                int month = int.Parse(parts[1]);
                int day = int.Parse(parts[2]);
                DateTime dt = new DateTime(year, month, day);

                if (fileName.IndexOf("RUN") == -1)
                {
                    // formato data con minuti e secondi
                    LblRunName.Text = $"{dt.ToString("dd MMMM yyyy", new System.Globalization.CultureInfo("it-IT"))} - {parts[3]}:{parts[4]} ";
                }
                else
                {
                    // 4. Formattazione finale: "13 marzo 2026 - Run 2"
                    string runNumber = parts[4];
                    LblRunName.Text = $"{dt.ToString("dd MMMM yyyy", new System.Globalization.CultureInfo("it-IT"))} - Run {runNumber}";
                }
               

                LblTurnCount.Text = detectedTurns.Count.ToString();

                if (detectedTurns.Any())
                {
                    LblPerformanceIndex.Text = $"{detectedTurns.Average(t => t.PerformanceIndex):F0}%";
                    LblMaxAngle.Text = $"{detectedTurns.Max(t => t.EdgeAngle):F0}°";
                    LblMaxG.Text = $"{detectedTurns.Max(t => t.GForce):F2}";
                }
                SkiChart.LoadFrames(frames);
                TurnsCollection.ItemsSource = detectedTurns;
            });
        }
        catch (Exception ex)
        {
            DisplayAlertAsync("Errore", $"Impossibile leggere il file: {ex.Message}", "OK");
        }
    }

    private void OnTabCurve(object? sender, EventArgs? e)
    {
        TurnsCollection.IsVisible = true;
        SkiChart.IsVisible = false;

        BtnTabCurve.Stroke = new SolidColorBrush(Colors.White);
        BtnTabCurve.BackgroundColor = Color.FromArgb("#121212");
        ((Label)BtnTabCurve.Content!).TextColor = Colors.White;

        BtnTabGrafico.Stroke = new SolidColorBrush(Color.FromArgb("#333"));
        BtnTabGrafico.BackgroundColor = Colors.Transparent;
        ((Label)BtnTabGrafico.Content!).TextColor = Color.FromArgb("#555");
    }

    private void OnTabGrafico(object? sender, EventArgs? e)
    {
        TurnsCollection.IsVisible = false;
        SkiChart.IsVisible = true;

        BtnTabGrafico.Stroke = new SolidColorBrush(Colors.White);
        BtnTabGrafico.BackgroundColor = Color.FromArgb("#121212");
        ((Label)BtnTabGrafico.Content!).TextColor = Colors.White;

        BtnTabCurve.Stroke = new SolidColorBrush(Color.FromArgb("#333"));
        BtnTabCurve.BackgroundColor = Colors.Transparent;
        ((Label)BtnTabCurve.Content!).TextColor = Color.FromArgb("#555");
    }
}