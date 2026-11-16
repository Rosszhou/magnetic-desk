#if MAGNETIC_DESK_MAGNETIC_PREVIEW
using System.Text.Json;
using MagneticDesk.Controls;
using MagneticDesk.Models;
using MagneticDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MagneticDesk;

public partial class App
{
    private void ShowMagneticAnimationGallery()
    {
        var root = new StackPanel { Padding = new Thickness(20), Spacing = 12,
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 247, 249, 253)), RequestedTheme = ElementTheme.Light };
        var orientation = new Button { Content = "查看上下边缘" };
        root.Children.Add(orientation);
        var pairs = new List<(StackPanel Panel, WidgetShell Source, WidgetShell Target, MagneticAnimationStyle Style)>();
        foreach (var style in Enum.GetValues<MagneticAnimationStyle>())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new TextBlock { Text = LocalizationService.T($"Magnetic.Animation.{style}"),
                Width = 150, FontSize = 18, VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 35, 75, 102)) });
            var pair = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var source = new WidgetShell { Width = 190, Height = 104, ShowHoverButtons = false };
            var target = new WidgetShell { Width = 190, Height = 104, ShowHoverButtons = false };
            foreach (var shell in new[] { source, target })
            {
                shell.TitleTextElement.Text = "";
                shell.SetMouseLeaveCountdown(true, .6);
                pair.Children.Add(new Border { Child = shell, CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(ColorHelper.FromArgb(255, 228, 237, 248)) });
            }
            row.Children.Add(pair);
            root.Children.Add(row);
            pairs.Add((pair, source, target, style));
        }
        bool vertical = false;
        orientation.Click += (_, _) =>
        {
            vertical = !vertical;
            orientation.Content = vertical ? "查看左右边缘" : "查看上下边缘";
            foreach (var pair in pairs) pair.Panel.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        };
        _magneticPreviewWindow = new Window { Title = "magnetic-desk 五款透明磁吸动画", Content = new ScrollViewer { Content = root } };
        _magneticPreviewWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 1080));
        var timer = _magneticPreviewWindow.DispatcherQueue.CreateTimer();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool verified = false;
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) =>
        {
            double phase = clock.Elapsed.TotalSeconds % 4.5;
            foreach (var pair in pairs)
            {
                if (phase >= 3.75)
                {
                    pair.Source.HideMagneticDockFeedback();
                    pair.Target.HideMagneticDockFeedback();
                    continue;
                }
                bool complete = phase >= 3;
                double time = complete ? phase - 3 : phase;
                pair.Source.SetMagneticDockFeedback(vertical ? MagneticDockEdge.Bottom : MagneticDockEdge.Right,
                    vertical ? 95 : 52, Math.Min(1, phase / 3), complete, time, pair.Style.ToString());
                pair.Target.SetMagneticDockFeedback(vertical ? MagneticDockEdge.Top : MagneticDockEdge.Left,
                    vertical ? 95 : 52, Math.Min(1, phase / 3), complete, time, pair.Style.ToString());
            }
            if (!verified && phase > 1 && phase < 3)
            {
                verified = true;
                bool transparent = pairs.All(p =>
                    ((SolidColorBrush)((Border)p.Source.FindName("MagneticDockBadge")).Background).Color.A == 0);
                File.WriteAllText(Path.Combine(MagneticDeskDataPathService.Current.RootPath, "magnetic-animation-gallery.json"),
                    JsonSerializer.Serialize(new { passed = transparent, transparentBackground = transparent, styles = pairs.Count }));
            }
        };
        _magneticPreviewWindow.Closed += (_, _) => timer.Stop();
        _magneticPreviewWindow.Activate();
        timer.Start();
        if (WidgetManager is { } manager) _ = manager.RunMagneticDockIntegrationAsync();
    }
}
#endif
