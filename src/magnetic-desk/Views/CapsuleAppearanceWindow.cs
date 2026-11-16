using MagneticDesk.Controls;
using MagneticDesk.Models;
using MagneticDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace MagneticDesk.Views;

internal sealed class CapsuleAppearanceWindow : Window
{
    private readonly WidgetConfig _config;
    private readonly Action<WidgetCapsuleStyle, bool> _apply;
    private readonly StackPanel _root;
    private readonly ComboBox _scope;
    private readonly List<WidgetShell> _previews = [];
    private readonly List<WidgetShell> _expandedPreviews = [];
    private readonly Dictionary<WidgetCapsuleStyle, Button> _applyButtons = [];
    private readonly DispatcherQueueTimer _timer;

    internal CapsuleAppearanceWindow(WidgetConfig config, bool hasGroup,
        Action<WidgetCapsuleStyle, bool> apply)
    {
        _config = config;
        _apply = apply;
        var strings = App.Current.LocalizationService;
        Title = $"magnetic-desk · {strings.T("Capsule.Gallery.Title")}";
        _root = new StackPanel { Padding = new Thickness(20), Spacing = 18 };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
        toolbar.Children.Add(new TextBlock { Text = config.Name, FontSize = 22,
            MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        _scope = new ComboBox { Header = strings.T("Capsule.Gallery.Scope"), MinWidth = 180 };
        _scope.Items.Add(strings.T("Magnetic.Collapse.Current"));
        if (hasGroup) _scope.Items.Add(strings.T("Magnetic.Collapse.Group"));
        _scope.SelectedIndex = hasGroup ? 1 : 0;
        toolbar.Children.Add(_scope);
        var themeButton = new Button { Content = strings.T("Capsule.Gallery.Theme"), VerticalAlignment = VerticalAlignment.Bottom };
        themeButton.Click += (_, _) => _root.RequestedTheme = _root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        toolbar.Children.Add(themeButton);
        _root.Children.Add(toolbar);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
        _root.Children.Add(row);
        foreach (var style in Enum.GetValues<WidgetCapsuleStyle>())
        {
            var column = new StackPanel { Spacing = 12, Width = 290 };
            column.Children.Add(new TextBlock { Text = strings.T($"Capsule.Style.{style}"), FontSize = 20 });
            var expanded = new WidgetShell { Width = 290, Height = 210, ShowHoverButtons = false, IsHitTestVisible = false,
                ShellContent = new TextBlock { Text = strings.T("Capsule.Gallery.Sample"), Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap } };
            expanded.TitleTextElement.Text = strings.T("QuickCapture.Name");
            expanded.SetCapsuleStyle(style.ToString());
            column.Children.Add(expanded);
            _previews.Add(expanded);
            _expandedPreviews.Add(expanded);
            foreach (int width in new[] { 290, 230 })
            {
                var compact = new WidgetShell { Width = width, Height = 52, ShowHoverButtons = false, IsHitTestVisible = false };
                compact.SetCapsuleStyle(style.ToString());
                compact.SetCompactPresentation(new WidgetCompactPresentation(
                    width == 290 ? config.Name : strings.T("QuickCapture.Name"), "", "\uE8A5", ""));
                compact.SetCollapsed(true, SettingsService.WidgetCompactContentModeSummary);
                column.Children.Add(compact);
                _previews.Add(compact);
            }
            var applyButton = new Button { HorizontalAlignment = HorizontalAlignment.Stretch };
            applyButton.Click += (_, _) =>
            {
                _apply(style, _scope.SelectedIndex == 1);
                RefreshSelection();
            };
            _applyButtons.Add(style, applyButton);
            column.Children.Add(applyButton);
            row.Children.Add(column);
        }
        _root.ActualThemeChanged += (_, _) => UpdatePalette();
        Content = new ScrollViewer { Content = _root, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        App.Current.ThemeService?.TrackWindow(this);
        UpdatePalette();
        RefreshSelection();
        var area = DisplayArea.Primary.WorkArea;
        int windowWidth = Math.Min(2420, Math.Max(400, area.Width - 48));
        int windowHeight = Math.Min(940, Math.Max(400, area.Height - 48));
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - windowWidth) / 2,
            area.Y + (area.Height - windowHeight) / 2, windowWidth, windowHeight));
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _timer.Tick += (_, _) =>
        {
            double phase = clock.Elapsed.TotalSeconds % 6;
            foreach (var shell in _expandedPreviews)
                shell.SetMouseLeaveCountdown(phase < 5, 1 - Math.Min(5, phase) / 5);
        };
        Closed += (_, _) => _timer.Stop();
        _timer.Start();
    }

    private void RefreshSelection()
    {
        var selected = WidgetCapsuleAppearance.Normalize(_config.CapsuleStyle);
        foreach (var (style, button) in _applyButtons)
            button.Content = App.Current.LocalizationService.T(style == selected ? "Capsule.Gallery.Selected" : "Capsule.Gallery.Apply");
    }

    private void UpdatePalette()
    {
        bool dark = _root.ActualTheme == ElementTheme.Dark;
        _root.Background = new SolidColorBrush(dark ? ColorHelper.FromArgb(255, 36, 40, 46) : ColorHelper.FromArgb(255, 235, 241, 246));
        foreach (var shell in _previews)
            shell.BackgroundSurface.Background = new SolidColorBrush(dark ? ColorHelper.FromArgb(255, 29, 33, 39) : ColorHelper.FromArgb(255, 244, 248, 251));
    }
}
