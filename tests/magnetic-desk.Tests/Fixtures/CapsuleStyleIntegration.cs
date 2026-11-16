#if MAGNETIC_DESK_MAGNETIC_PREVIEW
using System.Text.Json;
using System.Reflection;
using MagneticDesk.Controls;
using MagneticDesk.Models;
using MagneticDesk.Services;
using MagneticDesk.Views;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace MagneticDesk
{
    public partial class App
    {
        private void ShowCapsuleStyleGallery()
        {
            var config = new WidgetConfig { Name = LocalizationService.T("Capsule.Gallery.Title") };
            _magneticPreviewWindow = new CapsuleAppearanceWindow(config, false,
                (style, _) => config.CapsuleStyle = style.ToString());
            _magneticPreviewWindow.Activate();
            if (Environment.GetEnvironmentVariable("MAGNETIC_DESK_CAPSULE_GALLERY_ONLY") != "1" &&
                WidgetManager is { } manager)
                _ = Environment.GetEnvironmentVariable("MAGNETIC_DESK_CAPSULE_LAYOUT_ONLY") == "1"
                    ? manager.RunCapsuleStyleIntegrationAsync() : manager.RunMagneticDockIntegrationAsync();
        }
    }
}

namespace MagneticDesk.Services
{
    public sealed partial class WidgetManager
    {
        internal async Task InspectSurfaceStartupAsync()
        {
            await Task.Delay(2500);
            foreach (var window in GetLoadedDesktopWindows().OfType<WidgetWindowBase>())
            {
                CheckWindowClip(window);
                if (window.Config.Name != "magnetic-desk") continue;
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                int style = Platform.Win32Helper.GetWindowLong(handle, Platform.Win32Helper.GWL_EXSTYLE);
                Platform.Win32Helper.SetWindowLong(handle, Platform.Win32Helper.GWL_EXSTYLE,
                    (style & ~Platform.Win32Helper.WS_EX_TOOLWINDOW) | 0x00040000);
                window.Title = "magnetic-desk surface verification";
                window.RaiseTemporarilyFromManager();
                window.Activate();
            }
        }

        internal async Task VerifyCapsuleStyleRestartAsync()
        {
            string root = MagneticDeskDataPathService.Current.RootPath;
            string expected = Path.Combine(root, "capsule-style-restart-expected.json");
            if (!File.Exists(expected)) return;
            try
            {
                var styles = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(expected))!;
                foreach (var window in GetLoadedDesktopWindows().OfType<WidgetWindowBase>().Where(w => styles.ContainsKey(w.Config.Id)))
                {
                    Check(window.Config.CapsuleStyle == styles[window.Config.Id], "Restart lost the capsule style setting");
                    var shell = (WidgetShell)typeof(WidgetWindowBase).GetProperty("WidgetShellControl", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    var actual = (WidgetCapsuleStyle)typeof(WidgetShell).GetField("_capsuleStyle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
                    Check(actual.ToString() == styles[window.Config.Id], "Restart did not apply the saved capsule style to its view");
                }
                await File.WriteAllTextAsync(Path.Combine(root, "capsule-style-restart.json"), JsonSerializer.Serialize(new { passed = true, settingsAndViews = true }));
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(Path.Combine(root, "capsule-style-restart.json"), JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
            }
        }

        internal async Task RunCapsuleStyleIntegrationAsync()
        {
            string path = Path.Combine(MagneticDeskDataPathService.Current.RootPath, "capsule-style-integration.json");
            var members = GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
                .Where(w => w.Config.WidgetKind is WidgetKind.QuickCapture or WidgetKind.File or WidgetKind.Todo).ToArray();
            var original = members.ToDictionary(w => w.Config.Id, w => (Group: GetMagneticGroupId(w.Config), Linked: w.Config.MagneticCollapseLinked,
                Expanded: w.MagneticExpandedBounds, Style: w.Config.CapsuleStyle));
            var top = members.First(w => w.Config.WidgetKind == WidgetKind.QuickCapture);
            var bottom = members.First(w => w.Config.WidgetKind == WidgetKind.File);
            try
            {
                foreach (var window in members) SetMagneticGroupId(window.Config, null);
                foreach (var window in new[] { top, bottom })
                {
                    WidgetCollapseBehaviorNames.SetOverride(window.Config, WidgetCollapseBehavior.Click);
                    window.RefreshMagneticCollapseBehavior();
                    window.ApplyMagneticCollapsedState(false, original[window.Config.Id].Expanded, true, false);
                    SetMagneticGroupId(window.Config, "independent-vertical-test");
                }
                top.ApplyMagneticResizeBounds(new(100, 100, 500, 300));
                bottom.ApplyMagneticResizeBounds(new(240, 410, 360, 400));
                foreach (var window in new[] { top, bottom }) window.CommitMagneticResizeLayout(window.CoordinatedMoveBounds);
                var before = bottom.CoordinatedMoveBounds;
                top.RequestCompactState(true, false);
                await Task.Delay(450);
                bottom.RequestCompactState(true, false);
                await Task.Delay(450);
                Check(bottom.CoordinatedMoveBounds.Y == top.CoordinatedMoveBounds.Y + top.CoordinatedMoveBounds.Height + 2,
                    "Independent vertical capsules retained their expanded gap");
                Check(!top.Config.MagneticCollapseLinked && !bottom.Config.MagneticCollapseLinked, "Compaction changed individual collapse preferences");
                var compactHome = new[] { top, bottom }.ToDictionary(w => w.Config.Id, w => w.CoordinatedMoveBounds);
                bottom.RequestCompactState(false, false);
                await Task.Delay(450);
                Check(bottom.CoordinatedMoveBounds.Y == compactHome[bottom.Config.Id].Y, "Opening the lower member moved the compact group origin");
                Check(bottom.CoordinatedMoveBounds.Y == top.CoordinatedMoveBounds.Y + top.CoordinatedMoveBounds.Height + 2,
                    "Opening the lower member did not move the upper capsule alongside it");
                bottom.RequestCompactState(true, false);
                await Task.Delay(450);
                Check(bottom.CoordinatedMoveBounds.Y == top.CoordinatedMoveBounds.Y + top.CoordinatedMoveBounds.Height + 2,
                    "Repeated independent vertical collapse lost its attachment");
                top.RequestCompactState(false, false);
                await Task.Delay(450);
                Check(bottom.IsCompactArrangementActive, "Opening one member opened its independent companion");
                Check(bottom.CoordinatedMoveBounds.Y == top.CoordinatedMoveBounds.Y + top.CoordinatedMoveBounds.Height + 2,
                    "The lower capsule covers the independently opened panel");
                RestoreMagneticCollapseGroups();
                Check(bottom.CoordinatedMoveBounds.Y == top.CoordinatedMoveBounds.Y + top.CoordinatedMoveBounds.Height + 2,
                    "The consistency pass moved a capsule back over the open content");
                top.RequestCompactState(true, false);
                await Task.Delay(450);
                foreach (var member in new[] { top, bottom })
                    Check(Same(member.CoordinatedMoveBounds, compactHome[member.Config.Id]), "Repeated expansion changed compact home bounds");
                for (int cycle = 0; cycle < 5; cycle++)
                {
                    top.RequestCompactState(false, true);
                    await Task.Delay(35);
                    bottom.RequestCompactState(false, true);
                    await Task.Delay(45);
                    top.RequestCompactState(true, true);
                    await Task.Delay(25);
                    bottom.RequestCompactState(true, true);
                    await Task.Delay(30);
                    top.RequestCompactState(false, true);
                    await Task.Delay(35);
                    top.RequestCompactState(true, true);
                    await Task.Delay(550);
                    RestoreMagneticCollapseGroups();
                    foreach (var member in new[] { top, bottom })
                    {
                        Check(Same(member.CoordinatedMoveBounds, compactHome[member.Config.Id]), "Interrupted animation moved a capsule home");
                        Check(Same(member.Config.MagneticCompactPlacement!.ToBounds(), compactHome[member.Config.Id]), "Avoidance polluted the saved compact home");
                        var persisted = new RectInt32((int)member.Config.CompactPlacement!.X, (int)member.Config.CompactPlacement.Y,
                            compactHome[member.Config.Id].Width, compactHome[member.Config.Id].Height);
                        Check(Same(persisted, compactHome[member.Config.Id]), "Legacy placement disagrees with the compact home");
                    }
                }
                int expandedGap = (int)Math.Round(_settingsService.Settings.WidgetSnapSpacing *
                    Platform.Win32Helper.GetDpiScaleForWindow(WinRT.Interop.WindowNative.GetWindowHandle(top), top.WindowContentRoot?.XamlRoot));
                foreach (bool topFirst in new[] { true, false })
                {
                    var first = topFirst ? top : bottom;
                    var second = topFirst ? bottom : top;
                    first.RequestCompactState(false, true);
                    await Task.Delay(40);
                    second.RequestCompactState(false, true);
                    await Task.Delay(550);
                    Check(top.CoordinatedMoveBounds.Y == compactHome[top.Config.Id].Y, "Sequential expansion changed the group origin");
                    Check(bottom.CoordinatedMoveBounds.Y == top.CoordinatedMoveBounds.Y + top.CoordinatedMoveBounds.Height + expandedGap,
                        "Sequential expansion restored a stale gap between the panels");
                    Check(top.CoordinatedMoveBounds.Width == 500 && bottom.CoordinatedMoveBounds.Width == 360,
                        "Sequential expansion changed the configured widths");
                    first.RequestCompactState(true, true);
                    await Task.Delay(40);
                    second.RequestCompactState(true, true);
                    await Task.Delay(550);
                    foreach (var member in new[] { top, bottom })
                        Check(Same(member.CoordinatedMoveBounds, compactHome[member.Config.Id]), "Sequential expansion changed compact homes");
                }
                foreach (var style in Enum.GetValues<WidgetCapsuleStyle>())
                {
                    SetCapsuleAppearance(top.Config, style);
                    Check(top.Config.CapsuleStyle == style.ToString() && bottom.Config.CapsuleStyle == style.ToString(), "Appearance did not apply to the group");
                    top.RequestCompactState(false, false);
                    await Task.Delay(450);
                    CheckWindowClip(top);
                    CheckWindowClip(bottom);
                    top.RequestCompactState(true, false);
                    await Task.Delay(450);
                    CheckWindowClip(top);
                    foreach (var member in new[] { top, bottom })
                        Check(Same(member.CoordinatedMoveBounds, compactHome[member.Config.Id]), "Style switching moved a compact home");
                    await _settingsService.FlushPendingSaveAsync(false);
                }
                var menu = new MenuFlyout();
                AppendCapsuleAppearanceMenu(menu, top.Config);
                Check(((MenuFlyoutSubItem)menu.Items[0]).Items[0] is MenuFlyoutItem, "The appearance preview is missing from the widget menu");
                ShowCapsuleAppearance(top.Config);
                var gallery = _capsuleAppearanceWindows[top.Config.Id];
                await Task.Delay(350);
                var scope = (ComboBox)typeof(CapsuleAppearanceWindow).GetField("_scope", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(gallery)!;
                var buttons = (Dictionary<WidgetCapsuleStyle, Button>)typeof(CapsuleAppearanceWindow).GetField("_applyButtons", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(gallery)!;
                void ApplyPreview(WidgetCapsuleStyle style)
                {
                    var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(buttons[style]);
                    ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
                }
                scope.SelectedIndex = 0;
                ApplyPreview(WidgetCapsuleStyle.ClassicBlack);
                await Task.Delay(150);
                Check(top.Config.CapsuleStyle == "ClassicBlack" && bottom.Config.CapsuleStyle == "ClassicWhite", "The preview ignored current-widget scope");
                scope.SelectedIndex = 1;
                ApplyPreview(WidgetCapsuleStyle.Fill);
                await Task.Delay(150);
                Check(top.Config.CapsuleStyle == "Fill" && bottom.Config.CapsuleStyle == "Fill", "The preview did not apply to the magnetic group");
                gallery.Close();
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true, styles = Enum.GetValues<WidgetCapsuleStyle>().Length,
                    independentVerticalCompaction = true, mixedExpansionAvoidsOverlap = true, sequentialExpansionBothOrders = true, appearanceGalleryScopes = true,
                    roundedHostClipping = true, exactCompactHomeRestored = true, interruptedAnimationCycles = 5, repeatExpansionRestored = true, groupAppearanceSwitch = true }));
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
                App.Log($"[CapsuleIntegration] Failed: {ex}");
            }
            finally
            {
                foreach (var window in members)
                {
                    var saved = original[window.Config.Id];
                    SetMagneticGroupId(window.Config, saved.Group);
                    window.Config.MagneticCollapseLinked = saved.Linked;
                    window.Config.CapsuleStyle = saved.Style;
                    window.RefreshCapsuleAppearance();
                    window.Config.MagneticExpandedPlacement = MagneticExpandedPlacement.FromBounds(saved.Expanded);
                }
                ApplyMagneticCollapse(members, true, true, false);
                SetCapsuleAppearance(top.Config, WidgetCapsuleStyle.Segments);
                await _settingsService.FlushPendingSaveAsync(false);
                await File.WriteAllTextAsync(Path.Combine(MagneticDeskDataPathService.Current.RootPath, "capsule-style-restart-expected.json"),
                    JsonSerializer.Serialize(members.ToDictionary(w => w.Config.Id, w => w.Config.CapsuleStyle)));
            }
            await RunFeaturePruningIntegrationAsync();
        }

        private static void CheckWindowClip(WidgetWindowBase window)
        {
            Check(window.SystemBackdrop is WinUIEx.TransparentTintBackdrop tint && tint.TintColor.A == 0,
                "A window-level material still paints outside the clipped surface");
            var root = (FrameworkElement)typeof(WidgetWindowBase).GetProperty("RootElement", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var shell = (WidgetShell)typeof(WidgetWindowBase).GetProperty("WidgetShellControl", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var shellRoot = (FrameworkElement)shell.FindName("ShellRoot");
            var clip = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(shellRoot).Clip as Microsoft.UI.Composition.RectangleClip;
            Check(clip is not null, "The surface has no rounded rectangle clip");
            Check(Math.Abs(clip!.Right - root.ActualWidth) < 1 && Math.Abs(clip.Bottom - root.ActualHeight) < 1,
                "The clip did not follow the live window size");
            Check(clip.TopLeftRadius.X > 0, "The clip lost the capsule corner radius");
            int cornerResult = Platform.Win32Helper.DwmGetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(window),
                Platform.Win32Helper.DWMWA_WINDOW_CORNER_PREFERENCE, out int cornerPreference, sizeof(int));
            Check(cornerResult != 0 || cornerPreference == Platform.Win32Helper.DWMWCP_DONOTROUND,
                "The native window frame applies a second corner shape");
            var bounds = window.CoordinatedMoveBounds;
            IntPtr region = ReadRegionCreate(0, 0, 0, 0);
            try
            {
                Check(ReadWindowRegion(WinRT.Interop.WindowNative.GetWindowHandle(window), region) == 0,
                    "A native pixel region overrides the antialiased capsule edge");
            }
            finally { ReadRegionDelete(region); }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "CreateRectRgn")]
        private static extern IntPtr ReadRegionCreate(int left, int top, int right, int bottom);
        [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        private static extern bool ReadRegionDelete(IntPtr region);
        [System.Runtime.InteropServices.DllImport("gdi32.dll", EntryPoint = "PtInRegion")]
        private static extern bool ReadRegionPoint(IntPtr region, int x, int y);
        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowRgn")]
        private static extern int ReadWindowRegion(IntPtr window, IntPtr region);

    }
}
#endif
