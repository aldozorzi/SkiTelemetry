using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;
using SkiTelemetry_Core;

namespace SkiTelemetry_Analyst.Views.Runs;

public partial class TurnDetailPage : ContentPage
{
    public ObservableCollection<TurnDetailItem> Items { get; } = new();

    private const double DotSize = 6;
    private const double DotSpacing = 7;
    private readonly List<Border> _dots = new();
    private bool _initialized;

    public TurnDetailPage(List<SkiFrame> frames, List<SkiTurn> turns, int index)
    {
        foreach (SkiTurn turn in turns)
        {
            var slice = frames
                .Where(f => f.Left.Timestamp >= turn.StartTimestamp && f.Left.Timestamp <= turn.EndTimestamp)
                .ToList();

            Items.Add(new TurnDetailItem(slice, turn));
        }

        InitializeComponent();
        BindingContext = this;
        BuildDots();

        int start = Items.Count == 0 ? 0 : Math.Clamp(index, 0, Items.Count - 1);
        _initialized = true;

        if (Items.Count > 0)
        {
            TurnsCarousel.Position = start;
            ShowPosition(start);
        }
    }

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        if (!_initialized) return;
        ShowPosition(e.CurrentPosition);
    }

    private void ShowPosition(int position)
    {
        if (Items.Count == 0)
        {
            LblTitle.Text = "CURVA";
            LblFoot.Text = "-";
            BtnPrev.IsEnabled = false;
            BtnNext.IsEnabled = false;
            return;
        }

        position = Math.Clamp(position, 0, Items.Count - 1);
        TurnDetailItem item = Items[position];

        LblTitle.Text = item.Title;
        LblFoot.Text = item.FootText;
        BtnPrev.IsEnabled = position > 0;
        BtnNext.IsEnabled = position < Items.Count - 1;

        for (int i = 0; i < _dots.Count; i++)
        {
            bool selected = i == position;
            _dots[i].BackgroundColor = selected
                ? Color.FromArgb("#FFC857")
                : Color.FromArgb("#40FFFFFF");
        }

        UpdateDotsOffset(position);
    }

    private void BuildDots()
    {
        DotsRow.Children.Clear();
        _dots.Clear();

        for (int i = 0; i < Items.Count; i++)
        {
            var dot = new Border
            {
                WidthRequest = DotSize,
                HeightRequest = DotSize,
                BackgroundColor = Color.FromArgb("#40FFFFFF"),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(DotSize / 2) },
                InputTransparent = true
            };

            DotsRow.Children.Add(dot);
            _dots.Add(dot);
        }
    }

    private void OnDotsHostSizeChanged(object? sender, EventArgs e)
        => UpdateDotsOffset(TurnsCarousel.Position);

    private void UpdateDotsOffset(int position)
    {
        if (_dots.Count == 0 || DotsHost.Width <= 0) return;

        position = Math.Clamp(position, 0, _dots.Count - 1);
        // Include a trailing gap: the stack reports slightly more width than
        // the nominal dot widths because of layout rounding.
        double rowWidth = _dots.Count * (DotSize + DotSpacing);
        double selectedCenter = position * (DotSize + DotSpacing) + DotSize / 2;
        double offset = DotsHost.Width / 2 - selectedCenter;

        const double edgeGuard = 1;
        offset = Math.Min(0, Math.Max(DotsHost.Width - rowWidth - edgeGuard, offset));
        DotsRow.TranslationX = offset;
    }

    private void OnPrevClicked(object? sender, TappedEventArgs e)
        => TurnsCarousel.Position = Math.Max(0, TurnsCarousel.Position - 1);

    private void OnNextClicked(object? sender, TappedEventArgs e)
        => TurnsCarousel.Position = Math.Min(Items.Count - 1, TurnsCarousel.Position + 1);

    private async void OnCloseClicked(object? sender, TappedEventArgs e)
        => await Navigation.PopModalAsync();
}
