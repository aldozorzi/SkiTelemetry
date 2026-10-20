using SkiTelemetry_Core;

namespace SkiTelemetry_Analyst.Views.Runs;

[QueryProperty(nameof(FilePath), "path")]
public partial class RunDetailPage : ContentPage
{
    private string _filePath = "";
    private int _loadVersion;
    private List<SkiFrame>? _loadedFrames;
    private List<SkiTurn>? _loadedTurns;

    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = Uri.UnescapeDataString(value);
            OnPropertyChanged();
            _ = LoadRunDataAsync(_filePath);
        }
    }

    public RunDetailPage()
    {
        InitializeComponent();
    }

    private async Task LoadRunDataAsync(string path)
    {
        int version = ++_loadVersion;

        try
        {
            var result = await Task.Run(() =>
            {
                List<SkiFrame> frames = RunParser.ParseBinFile(path);
                var detectedTurns = new List<SkiTurn>();
                SkiDataProcessor processor = new SkiDataProcessor();
                processor.OnNewTurn += (_, turn) => detectedTurns.Add(turn);

                HashSet<MonitorMode> metrics = new()
                {
                    MonitorMode.EdgeAngle,
                    MonitorMode.GForce,
                    MonitorMode.EarlyLoading,
                    MonitorMode.ProgressiveEdgeBuild,
                    MonitorMode.TurnTime,
                    MonitorMode.PerformanceIndex,
                    MonitorMode.EdgeSimilarity,
                    MonitorMode.Foot
                };

                processor.setMetrics(metrics);

                foreach (var frame in frames)
                {
                    processor.Process(frame);
                }

#if WINDOWS
                SkiGraphGenerator.GenerateHtmlReport(frames, detectedTurns);
#endif

                return (frames, detectedTurns);
            });

            if (version != _loadVersion) return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                List<SkiFrame> frames = result.frames;
                List<SkiTurn> detectedTurns = result.detectedTurns;

                LblRunName.Text = BuildRunName(path);
                LblTurnCount.Text = detectedTurns.Count.ToString();

                if (detectedTurns.Count > 0)
                {
                    double averagePerformance =
                        detectedTurns.Average(t => t.PerformanceIndex.GetValueOrDefault());

                    LblPerformanceIndex.Text = $"{averagePerformance:F0}%";
                    PerfRing.Value = averagePerformance;
                    LblMaxAngle.Text =
                        $"{detectedTurns.Max(t => t.EdgeAngle.GetValueOrDefault()):F0}°";
                    LblMaxG.Text =
                        $"{detectedTurns.Max(t => t.GForce.GetValueOrDefault()):F2}";
                }
                else
                {
                    LblPerformanceIndex.Text = "0%";
                    PerfRing.Value = 0;
                    LblMaxAngle.Text = "0°";
                    LblMaxG.Text = "0.00";
                }

                _loadedFrames = frames;
                _loadedTurns = detectedTurns;

                SkiChart.LoadFrames(frames);
                TurnsCollection.ItemsSource = detectedTurns;
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Errore", $"Impossibile leggere il file: {ex.Message}", "OK");
        }
    }

    private static string BuildRunName(string path)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        string[] parts = fileName.Split('_');

        if (parts.Length < 5)
            return fileName;

        if (!int.TryParse(parts[0], out int year) ||
            !int.TryParse(parts[1], out int month) ||
            !int.TryParse(parts[2], out int day))
        {
            return fileName;
        }

        DateTime date;
        try
        {
            date = new DateTime(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return fileName;
        }

        string formattedDate = date.ToString("dd MMMM yyyy", new System.Globalization.CultureInfo("it-IT"));

        if (fileName.IndexOf("RUN") == -1)
            return $"{formattedDate} - {parts[3]}:{parts[4]}";

        return $"{formattedDate} - Manche {parts[4]}";
    }

    private async void OnTurnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not SkiTurn turn) return;
        if (_loadedFrames is null || _loadedTurns is null) return;

        int index = _loadedTurns.IndexOf(turn);
        if (index < 0) return;

        await Navigation.PushModalAsync(new TurnDetailPage(_loadedFrames, _loadedTurns, index));
    }

    private void OnTabCurve(object? sender, EventArgs? e)
    {
        TurnsCollection.IsVisible = true;
        SkiChart.IsVisible = false;

        BtnTabCurve.Stroke = new SolidColorBrush(Colors.Transparent);
        BtnTabCurve.BackgroundColor = Colors.White;
        ((Label)BtnTabCurve.Content!).TextColor = Color.FromArgb("#2B0A4E");

        BtnTabGrafico.Stroke = new SolidColorBrush(Colors.Transparent);
        BtnTabGrafico.BackgroundColor = Colors.Transparent;
        ((Label)BtnTabGrafico.Content!).TextColor = Color.FromArgb("#8E7BA8");
    }

    private void OnTabGrafico(object? sender, EventArgs? e)
    {
        TurnsCollection.IsVisible = false;
        SkiChart.IsVisible = true;

        BtnTabGrafico.Stroke = new SolidColorBrush(Colors.Transparent);
        BtnTabGrafico.BackgroundColor = Colors.White;
        ((Label)BtnTabGrafico.Content!).TextColor = Color.FromArgb("#2B0A4E");

        BtnTabCurve.Stroke = new SolidColorBrush(Colors.Transparent);
        BtnTabCurve.BackgroundColor = Colors.Transparent;
        ((Label)BtnTabCurve.Content!).TextColor = Color.FromArgb("#8E7BA8");
    }
}