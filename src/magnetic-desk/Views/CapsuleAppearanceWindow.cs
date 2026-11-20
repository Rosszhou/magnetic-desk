using MagneticDesk.Controls;
using MagneticDesk.Models;
using MagneticDesk.Platform;
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
    private const int PreferredColumns = 3;
    private const double PreviewWidth = 250;
    private const double MinimumColumnWidth = 220;
    private const double GallerySpacing = 12;
    private const double PagePadding = 14;

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
        _root = new StackPanel { Padding = new Thickness(PagePadding), Spacing = GallerySpacing };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = GallerySpacing };
        toolbar.Children.Add(new TextBlock { Text = config.Name, FontSize = 22,
            MaxWidth = 215, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
        _scope = new ComboBox { Header = strings.T("Capsule.Gallery.Scope"), MinWidth = 180 };
        _scope.Items.Add(strings.T("Magnetic.Collapse.Current"));
        if (hasGroup) _scope.Items.Add(strings.T("Magnetic.Collapse.Group"));
        _scope.SelectedIndex = hasGroup ? 1 : 0;
        toolbar.Children.Add(_scope);
        var themeButton = new Button { Content = strings.T("Capsule.Gallery.Theme"),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        themeButton.Click += (_, _) => _root.RequestedTheme = _root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        toolbar.Children.Add(themeButton);
        _root.Children.Add(toolbar);
        var gallery = new Grid
        {
            ColumnSpacing = GallerySpacing,
            RowSpacing = GallerySpacing
        };
        _root.Children.Add(gallery);
        var sampleText = strings.T("Capsule.Gallery.Sample");
        foreach (var style in Enum.GetValues<WidgetCapsuleStyle>())
        {
            var column = new StackPanel { Spacing = 5, MaxWidth = PreviewWidth };
            column.Children.Add(new TextBlock { Text = strings.T($"Capsule.Style.{style}"), FontSize = 16 });
            var expanded = new WidgetShell { Height = 102, ShowHoverButtons = false, IsHitTestVisible = false,
                ShellContent = new TextBlock { Text = sampleText, FontSize = 13, Margin = new Thickness(10, 5, 10, 5),
                    TextWrapping = TextWrapping.Wrap } };
            expanded.TitleTextElement.Text = strings.T("QuickCapture.Name");
            expanded.SetCapsuleStyle(style.ToString());
            column.Children.Add(expanded);
            _previews.Add(expanded);
            _expandedPreviews.Add(expanded);
            foreach (bool useWidgetTitle in new[] { true, false })
            {
                var compact = new WidgetShell
                {
                    Height = 40, ShowHoverButtons = false, IsHitTestVisible = false,
                    Margin = new Thickness(0, 0, useWidgetTitle ? 0 : 46, 0)
                };
                compact.SetCapsuleStyle(style.ToString());
                compact.SetCompactPresentation(new WidgetCompactPresentation(
                    useWidgetTitle ? config.Name : strings.T("QuickCapture.Name"), "", "\uE8A5", ""));
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
            gallery.Children.Add(column);
        }
        LayoutGallery(gallery, PreferredColumns);
        _root.SizeChanged += (_, args) =>
        {
            double availableWidth = args.NewSize.Width - 2 * PagePadding;
            // Keep the toolbar buttons reachable on narrow windows as the cards wrap.
            toolbar.Orientation = availableWidth >= 650 ? Orientation.Horizontal : Orientation.Vertical;
            int columns = Math.Clamp((int)((availableWidth + GallerySpacing) / (MinimumColumnWidth + GallerySpacing)),
                1, PreferredColumns);
            if (columns != gallery.ColumnDefinitions.Count) LayoutGallery(gallery, columns);
        };
        _root.ActualThemeChanged += (_, _) => UpdatePalette();
        Content = new ScrollViewer { Content = _root, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        App.Current.ThemeService?.TrackWindow(this);
        UpdatePalette();
        RefreshSelection();
        var area = DisplayArea.Primary.WorkArea;
        double dpiScale = Win32Helper.GetDpiScaleForPoint(area.X + area.Width / 2, area.Y + area.Height / 2);
        int windowWidth = Math.Min((int)Math.Ceiling(824 * dpiScale), Math.Max(1, area.Width - 24));
        int windowHeight = Math.Min((int)Math.Ceiling(660 * dpiScale), Math.Max(1, area.Height - 24));
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

    private static void LayoutGallery(Grid gallery, int columns)
    {
        // Equal-width cards remain aligned across rows, including the last partial row.
        gallery.MaxWidth = columns * PreviewWidth + (columns - 1) * GallerySpacing;
        gallery.ColumnDefinitions.Clear();
        for (int column = 0; column < columns; column++)
            gallery.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gallery.RowDefinitions.Clear();
        for (int index = 0; index < gallery.Children.Count; index++)
        {
            int row = index / columns;
            if (index % columns == 0) gallery.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var card = (FrameworkElement)gallery.Children[index];
            Grid.SetRow(card, row);
            Grid.SetColumn(card, index % columns);
        }
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
