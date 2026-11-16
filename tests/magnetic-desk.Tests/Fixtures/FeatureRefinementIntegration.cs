#if MAGNETIC_DESK_MAGNETIC_PREVIEW
using System.Text.Json;
using System.Reflection;
using MagneticDesk.Controls;
using MagneticDesk.Models;
using MagneticDesk.Platform;
using MagneticDesk.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    internal async Task VerifyRefinementRestartAsync()
    {
        string root = MagneticDeskDataPathService.Current.RootPath;
        string expected = Path.Combine(root, "refinement-restart-expected.json");
        if (!File.Exists(expected)) return;
        try
        {
            var ids = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(expected))!;
            var windows = GetLoadedDesktopWindows().OfType<WidgetWindowBase>().Where(w => ids.Contains(w.Config.Id)).ToArray();
            Check(windows.Length == ids.Length, "Restart lost a pinned surface");
            Check(windows.All(w => w.Config.IsAlwaysOnTop && Win32Helper.IsWindowTopMost(WindowNative.GetWindowHandle(w))), "Restart lost a front pin");
            await File.WriteAllTextAsync(Path.Combine(root, "refinement-restart.json"), JsonSerializer.Serialize(new { passed = true, pinnedSurfaces = windows.Length }));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(root, "refinement-restart.json"), JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
        }
    }

    internal async Task RunRefinementIntegrationAsync()
    {
        string root = MagneticDeskDataPathService.Current.RootPath;
        string report = Path.Combine(root, "refinement-integration.json");
        try
        {
            var windows = GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
                .Where(w => w.Config.WidgetKind is WidgetKind.File or WidgetKind.QuickCapture or WidgetKind.Todo).ToArray();
            var original = windows.ToDictionary(w => w.Config.Id, w => (
                Group: GetMagneticGroupId(w.Config), Linked: w.Config.MagneticCollapseLinked,
                Attachment: w.Config.MagneticAttachmentPlacement?.ToBounds(), Anchor: w.Config.MagneticAttachmentAnchor,
                Expanded: w.Config.MagneticExpandedPlacement!.ToBounds(),
                Compact: w.Config.MagneticCompactPlacement!.ToBounds(), Visible: w.CoordinatedMoveBounds,
                Collapsed: w.IsCompactArrangementActive, Behavior: WidgetCollapseBehaviorNames.GetOverride(w.Config)));
            var first = windows.First(w => w.Config.WidgetKind == WidgetKind.QuickCapture);
            var second = windows.First(w => w.Config.WidgetKind == WidgetKind.File);
            var third = windows.First(w => w.Config.WidgetKind == WidgetKind.Todo);
            var styleMenu = ((ContentWidgetWindow)second).CreateMoreFlyout().Items.OfType<MenuFlyoutSubItem>()
                .Single(item => item.Text == _localizationService.T("Widget.StyleMenu.Label"));
            Check(styleMenu.Items.Count == 2, "Widget style menu still contains retired customization entries");
            foreach (var window in windows) SetPinnedToFront(window.Config, false, false);
            string? oldGroup = GetMagneticGroupId(first.Config);
            if (oldGroup is not null) ReplaceMagneticGroupId(oldGroup, null);
            foreach (var window in windows)
            {
                SetMagneticGroupId(window.Config, null);
                WidgetCollapseBehaviorNames.SetOverride(window.Config, WidgetCollapseBehavior.Smart);
                window.RefreshMagneticCollapseBehavior();
            }
            var shell = (WidgetShell)typeof(WidgetWindowBase).GetProperty("WidgetShellControl", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(second)!;
            second.RequestCompactState(false, false);
            var beforeNarrow = second.CoordinatedMoveBounds;
            second.ApplyMagneticResizeBounds(new(beforeNarrow.X, beforeNarrow.Y, 237, beforeNarrow.Height));
            await Task.Delay(150);
            Check(shell.TitleTextElement.ActualWidth >= 30 && shell.ActualTitleBarHeight > 50,
                "The narrow header did not retain its title and separate action row");
            var titlePoint = shell.TitleTextElement.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point(0, 0));
            var pinPoint = shell.FrontPinAnchor.TransformToVisual(shell).TransformPoint(new Windows.Foundation.Point(0, 0));
            Check(pinPoint.Y >= titlePoint.Y + shell.TitleTextElement.ActualHeight,
                "Narrow header buttons still compete with the title for one row");
            second.ApplyMagneticResizeBounds(beforeNarrow);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                // Orphaned flags from old saves must never create a linked new group.
                first.Config.MagneticCollapseLinked = true;
                second.Config.MagneticCollapseLinked = true;
                LinkMagneticGroups(first.Config, second.Config);
                Check(!first.Config.MagneticCollapseLinked && !second.Config.MagneticCollapseLinked, "A new group inherited stale whole-group expansion");
                first.RequestCompactState(true, false);
                second.RequestCompactState(true, false);
                first.RequestCompactState(false, false);
                await Task.Delay(200);
                Check(!first.IsCompactArrangementActive && second.IsCompactArrangementActive, "Opening one regrouped member opened its companion");
                first.RequestCompactState(true, false);
                second.RequestCompactState(false, false);
                await Task.Delay(200);
                Check(first.IsCompactArrangementActive && !second.IsCompactArrangementActive, "Reverse regrouped expansion opened both surfaces");
                string group = GetMagneticGroupId(first.Config)!;
                ReplaceMagneticGroupId(group, null);
                Check(windows.All(w => !w.Config.MagneticCollapseLinked), "Dissolving retained a linked-collapse flag");
            }
            LinkMagneticGroups(first.Config, second.Config);
            SetMagneticGroupCollapseBehavior(first.Config, WidgetCollapseBehavior.Smart);
            first.RequestCompactState(false, false);
            await Task.Delay(350);
            Check(!first.IsCompactArrangementActive && !second.IsCompactArrangementActive, "Explicit whole-group expansion was lost");
            var collapseAction = typeof(WidgetWindowBase).GetMethod("WidgetShellControl_CollapseRequested",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var routedIntent = typeof(WidgetWindowBase).GetMethod("ReleaseSmartHoverSuppressionAfterPointerIntent",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var exitRequired = typeof(WidgetWindowBase).GetField("_requirePointerExitBeforeHoverExpansion",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            collapseAction.Invoke(first, new object?[] { null, new RoutedEventArgs() });
            routedIntent.Invoke(first, new object[] { true });
            Check((bool)exitRequired.GetValue(first)!, "A routed move can immediately reverse manual collapse");
            await Task.Delay(400);
            LinkMagneticGroups(third.Config, first.Config);
            Check(windows.All(w => w.Config.MagneticCollapseLinked), "Joining an explicit whole-group scope lost its preference");
            SetPinnedToFront(first.Config, true, false);
            IntPtr firstHandle = WindowNative.GetWindowHandle(first);
            Check(Win32Helper.IsWindowTopMost(firstHandle) && !Win32Helper.IsWindowTopMost(WindowNative.GetWindowHandle(second)), "Current-surface pin changed its companion");
            var mixedOrder = new[] { WindowNative.GetWindowHandle(second), firstHandle };
            WidgetLayerService.ApplyPeerOrderHighestToLowest(mixedOrder);
            WidgetLayerService.EnsurePeerOrderHighestToLowest(mixedOrder);
            await Task.Delay(200);
            Check(Win32Helper.IsWindowTopMost(firstHandle) && !Win32Helper.IsWindowTopMost(WindowNative.GetWindowHandle(second)),
                "Peer normalization promoted an unpinned companion");
            WidgetLayerService.MoveToDesktopBottom(firstHandle, false);
            WidgetLayerService.ClearTopMostPreservingForeground(firstHandle, out _);
            WidgetLayerService.BringAbovePeerWidgets(firstHandle);
            Check(Win32Helper.IsWindowTopMost(firstHandle), "A desktop-layer restore cleared the persistent pin");
            SetPinnedToFront(first.Config, true, true);
            first.RequestCompactState(true, false);
            await Task.Delay(300);
            WidgetLayerService.RestoreGroupPreservingForeground(windows.Select(WindowNative.GetWindowHandle).ToArray(), "front-pin-regression");
            Check(windows.All(w => Win32Helper.IsWindowTopMost(WindowNative.GetWindowHandle(w))), "Group collapse or layer restore cleared a front pin");
            SetPinnedToFront(second.Config, false, false);
            Check(!Win32Helper.IsWindowTopMost(WindowNative.GetWindowHandle(second)) && Win32Helper.IsWindowTopMost(firstHandle), "Unpinning one surface changed its peers");
            SetPinnedToFront(first.Config, true, true);
            var menu = new MenuFlyout();
            AppendMagneticGroupMenu(menu, first.Config);
            var magnetic = menu.Items.OfType<MenuFlyoutSubItem>().Single(m => m.Text == _localizationService.T("Magnetic.Menu"));
            Check(magnetic.Items.OfType<MenuFlyoutSubItem>().Any(m => m.Text == _localizationService.T("Widget.Group.Join")), "Stack grouping is missing from the magnetic submenu");
            Check(!menu.Items.OfType<MenuFlyoutSubItem>().Any(m => m.Text == _localizationService.T("Widget.Group.Join")), "Stack grouping still has a top-level entry");
            App.Current.ShowSettings("Appearance");
            await Task.Delay(500);
            var appearance = ReadTexts((DependencyObject)App.Current.SettingsWindowInstance!.Content);
            foreach (string key in new[] { "Settings.TrayIcon.Title", "Settings.Accent.Source.Title", "Settings.Material.Title", "Settings.Group.AppVisual.Title", "Settings.Section.WidgetGroups", "Settings.Theme.Title", "Settings.Group.Animation.Title" })
                Check(!appearance.Contains(_localizationService.T(key)), "A removed appearance feature remains visible");
            App.Current.ShowSettings("CapsuleMode");
            await Task.Delay(250);
            var capsuleSettings = ReadTexts((DependencyObject)App.Current.SettingsWindowInstance!.Content);
            foreach (string key in new[] { "Settings.Capsule.WidthMode.Title", "Settings.Capsule.Arrangement.Title", "Settings.Capsule.Animation.Title" })
                Check(!capsuleSettings.Contains(_localizationService.T(key)), "A removed capsule option remains visible");
            var settings = App.Current.SettingsWindowInstance!;
            var search = (AutoSuggestBox)((FrameworkElement)settings.Content).FindName("SettingsSearchBox");
            string query = _localizationService.T("Settings.Group.FileLayout.Title");
            search.Text = query;
            typeof(SettingsWindow).GetMethod("UpdateSettingsSearchSuggestions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, [query]);
            await Task.Delay(150);
            var suggestion = ((System.Collections.IEnumerable)search.ItemsSource).Cast<object>().FirstOrDefault(item =>
                item.GetType().GetProperty("SectionTag")?.GetValue(item)?.ToString() == "FileDisplaySettings");
            Check(suggestion is not null && search.IsSuggestionListOpen, "The sidebar search lost its nested page suggestions");
            typeof(SettingsWindow).GetMethod("ActivateSettingsSearchResult", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, [suggestion, search]);
            await Task.Delay(200);
            Check(ReadTexts((DependencyObject)settings.Content).Contains(query), "The sidebar search did not open its destination page");
            search.Text = "";
            App.Current.ShowSettings("About");
            await Task.Delay(400);
            var about = ReadTexts((DependencyObject)App.Current.SettingsWindowInstance!.Content);
            foreach (string key in new[] { "Settings.About.AboutMeButton", "Settings.About.Website", "Settings.Update.Title", "Settings.About.StoreSupportTitle" })
                Check(!about.Contains(_localizationService.T(key)), "A removed about/update entry remains visible");
            Check(about.Contains(_localizationService.T("Settings.About.FeedbackTitle")), "The retained feedback entry disappeared");
            Check(settings.ViewModel.AppVersion == "1.0.0", "The visible product version is incorrect");
            var createMenu = ((ContentWidgetWindow)second).CreateWidgetCreateFlyout();
            Check(createMenu.Items.Count == 3 && createMenu.Items.OfType<MenuFlyoutItem>().Count() == 2,
                "Feature widgets still have duplicate top-level creation entries");
            var features = createMenu.Items.OfType<MenuFlyoutSubItem>().Single(item => item.Text == _localizationService.T("Common.AddFeatureWidget"));
            var choices = features.Items.OfType<MenuFlyoutItem>().ToArray();
            Check(choices.Select(item => (WidgetKind)item.Tag).SequenceEqual(new[] { WidgetKind.QuickCapture, WidgetKind.Clipboard, WidgetKind.Todo, WidgetKind.Search }),
                "The direct feature menu has missing or unavailable widget kinds");
            bool searchEnabled = FeatureWidgetSettings.IsEnabled(_settingsService.Settings, WidgetKind.Search);
            bool settingsVisible = settings.IsVisibleToUser;
            try
            {
                foreach (var choice in choices)
                {
                    var kind = (WidgetKind)choice.Tag;
                    await CreateWidgetOfKindAsync(kind);
                    Check(GetLoadedDesktopWindows().Any(window => window.Visible && window.Config.WidgetKind == kind), "Direct feature creation did not reveal its widget");
                    Check(settings.IsVisibleToUser == settingsVisible, "Direct creation changed the settings window visibility");
                }
            }
            finally
            {
                if (!searchEnabled) await SetFeatureWidgetEnabledAsync(WidgetKind.Search, false, false);
            }
            Check(App.Current.AppUpdateService is DisabledAppUpdateService, "The live app can still start upstream updates");
            foreach (var window in windows)
            {
                var saved = original[window.Config.Id];
                SetMagneticGroupId(window.Config, saved.Group);
                window.Config.MagneticCollapseLinked = saved.Linked;
                window.Config.MagneticAttachmentAnchor = saved.Anchor;
                window.Config.MagneticAttachmentPlacement = saved.Attachment is { } attachment ? MagneticExpandedPlacement.FromBounds(attachment) : null;
                window.Config.MagneticExpandedPlacement = MagneticExpandedPlacement.FromBounds(saved.Expanded);
                window.Config.MagneticCompactPlacement = MagneticExpandedPlacement.FromBounds(saved.Compact);
                WidgetCollapseBehaviorNames.SetOverride(window.Config, saved.Behavior);
                window.RefreshMagneticCollapseBehavior();
                window.ApplyMagneticCollapsedState(saved.Collapsed, saved.Visible, true, false);
            }
            await _settingsService.FlushPendingSaveAsync(false);
            await File.WriteAllTextAsync(Path.Combine(root, "refinement-restart-expected.json"), JsonSerializer.Serialize(windows.Select(w => w.Config.Id)));
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { passed = true, regroupCycles = 3, independentExpansion = true,
                explicitGroupScope = true, settingsSearch = true, narrowTitleBar = true, directFeatureCreation = true, versionOneZeroZero = true, manualCollapseHoverSuppression = true, persistentTopmost = true, perSurfacePin = true, groupPin = true, prunedSettings = true, nestedGroupingMenu = true }));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
        }
    }

    private static HashSet<string> ReadTexts(DependencyObject node)
    {
        var result = new HashSet<string>();
        if (node is TextBlock text) result.Add(text.Text);
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            result.UnionWith(ReadTexts(VisualTreeHelper.GetChild(node, index)));
        return result;
    }
}
#endif
