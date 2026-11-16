#if MAGNETIC_DESK_MAGNETIC_PREVIEW
using MagneticDesk.Controls;
using MagneticDesk.Controls.WidgetContents;
using MagneticDesk.Models;
using MagneticDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagneticDesk;

public partial class App
{
    private Window? _magneticPreviewWindow;

    partial void ShowMagneticDockPreview()
    {
        if (Environment.GetEnvironmentVariable("MAGNETIC_DESK_RESIZE_REJECTION_ONLY") == "1")
        {
            if (WidgetManager is { } resizeManager) _ = resizeManager.VerifyResizeRejectionOnlyAsync();
            return;
        }
        if (Environment.GetEnvironmentVariable("MAGNETIC_DESK_SURFACE_INSPECT") == "1")
        {
            if (WidgetManager is { } inspector) _ = inspector.InspectSurfaceStartupAsync();
            return;
        }
        if (Environment.GetEnvironmentVariable("MAGNETIC_DESK_CAPSULE_PREVIEW") == "1")
        {
            ShowCapsuleStyleGallery();
            return;
        }
        if (Environment.GetEnvironmentVariable("MAGNETIC_DESK_ANIMATION_PREVIEW") == "1")
        {
            ShowMagneticAnimationGallery();
            return;
        }
        var left = new WidgetShell { Width = 250, Height = 220 };
        var right = new WidgetShell { Width = 250, Height = 220 };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(left);
        row.Children.Add(right);
        _magneticPreviewWindow = new Window { Title = "magnetic-desk 磁吸动画预览", Content = row };
        _magneticPreviewWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(850, 440));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool logged = false;
        var timer = _magneticPreviewWindow.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) =>
        {
            double phase = clock.Elapsed.TotalSeconds % 5;
            if (phase > 3.75)
            {
                left.HideMagneticDockFeedback();
                right.HideMagneticDockFeedback();
                return;
            }
            bool complete = phase >= 3;
            double progress = Math.Min(1, phase / 3);
            left.SetMagneticDockFeedback(MagneticDockEdge.Right, 110, progress, complete, complete ? phase - 3 : phase);
            right.SetMagneticDockFeedback(MagneticDockEdge.Left, 110, progress, complete, complete ? phase - 3 : phase);
            if (!logged && phase > 1)
            {
                logged = true;
                var root = (FrameworkElement)left.FindName("ShellRoot");
                var badge = (FrameworkElement)left.FindName("MagneticDockBadge");
                var point = badge.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point());
                Log($"[MagneticPreview] root={root.ActualWidth}x{root.ActualHeight} badge={badge.ActualWidth}x{badge.ActualHeight} visible={badge.Visibility} opacity={badge.Opacity} position={point.X},{point.Y}");
            }
        };
        _magneticPreviewWindow.Closed += (_, _) => timer.Stop();
        _magneticPreviewWindow.Activate();
        timer.Start();
        if (WidgetManager is { } manager)
        {
            _ = ShowNotesClipboardPreviewAsync(manager);
        }
    }

    private async Task ShowNotesClipboardPreviewAsync(WidgetManager manager)
    {
        await manager.RunMagneticDockIntegrationAsync();
        var notes = await manager.CreateOrShowQuickCaptureWidgetAsync(reveal: false);
        var clipboard = await manager.CreateOrShowClipboardWidgetAsync();
        var root = new Grid { Padding = new Thickness(20), RowSpacing = 12, ColumnSpacing = 20 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition());
        var add = new Button { Content = "＋ 新建格子" };
        add.Click += (_, _) => notes.CreateWidgetCreateFlyout().ShowAt(add);
        root.Children.Add(add);
        var scopes = new Button { Content = "展开方式", HorizontalAlignment = HorizontalAlignment.Right };
        scopes.Click += (_, _) => notes.CreateMoreFlyout().ShowAt(scopes);
        Grid.SetColumn(scopes, 1);
        root.Children.Add(scopes);
        var adapters = new List<QuickCaptureWidgetContentAdapter>();
        foreach (var (config, column) in new[] { (notes.Config, 0), (clipboard.Config, 1) })
        {
            var adapter = new QuickCaptureWidgetContentAdapter(config, QuickCaptureService, SettingsService,
                LocalizationService, _magneticPreviewWindow!.DispatcherQueue);
            adapters.Add(adapter);
            var shell = new WidgetShell
            {
                ShellContent = adapter.View,
                TitleGlyph = config.WidgetKind == WidgetKind.Clipboard ? "\uE77F" : "\uE70F",
                TitleIconKind = WidgetTitleIconKindNames.FromWidgetKind(config.WidgetKind),
                TitleIconMode = WidgetTitleIconModeNames.Color
            };
            shell.TitleTextElement.Text = config.Name;
            shell.SetCollapsed(false, "Minimal");
            var border = new Border
            {
                Child = shell, CornerRadius = new CornerRadius(12), Padding = new Thickness(8),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.WhiteSmoke)
            };
            Grid.SetRow(border, 1);
            Grid.SetColumn(border, column);
            root.Children.Add(border);
        }
        _magneticPreviewWindow!.Content = root;
        _magneticPreviewWindow.Title = "magnetic-desk 便签与剪贴板验证";
        _magneticPreviewWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 850));
        foreach (var adapter in adapters) await adapter.InitializeAsync();
    }
}
#endif
