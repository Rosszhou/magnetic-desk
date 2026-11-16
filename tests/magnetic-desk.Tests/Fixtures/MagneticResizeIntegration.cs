#if MAGNETIC_DESK_MAGNETIC_PREVIEW
using System.Reflection;
using System.Text.Json;
using MagneticDesk.Models;
using MagneticDesk.Views;
using Windows.Graphics;

namespace MagneticDesk.Services;

public sealed partial class WidgetManager
{
    internal async Task VerifyResizeRejectionOnlyAsync()
    {
        string path = Path.Combine(MagneticDeskDataPathService.Current.RootPath, "resize-rejection-only.json");
        try
        {
            var upper = GetLoadedDesktopWindows().OfType<WidgetWindowBase>().First(w => w.Config.WidgetKind == WidgetKind.File);
            var lower = await CreateTodoWidgetAsync("Resize verification");
            await Task.Delay(800);
            string group = Guid.NewGuid().ToString();
            foreach (var window in new[] { upper, lower })
            {
                WidgetCollapseBehaviorNames.SetOverride(window.Config, WidgetCollapseBehavior.Expanded);
                window.RefreshMagneticCollapseBehavior();
                window.RequestCompactState(false, false);
                SetMagneticGroupId(window.Config, group);
            }
            upper.ApplyMagneticResizeBounds(new(100, 100, 600, 700));
            lower.ApplyMagneticResizeBounds(new(100, 810, 900, 700));
            foreach (var window in new[] { upper, lower })
            {
                window.CommitMagneticResizeLayout(window.CoordinatedMoveBounds);
                window.Config.MagneticAttachmentPlacement = window.Config.MagneticExpandedPlacement;
            }
            int before = upper.ResizeRejectionFeedbackCount;
            BeginMagneticResize(upper);
            var proposed = new RectInt32(100, 100, 600, 900);
            var accepted = ConstrainMagneticResize(upper, proposed);
            Check(accepted.Height == 700, "Height growth was accepted above a wider lower widget");
            ConstrainMagneticResize(upper, proposed);
            Check(upper.ResizeRejectionFeedbackCount == before + 1, "Rejection animation did not run exactly once");
            upper.ApplyMagneticResizeBounds(accepted);
            CompleteMagneticResize(upper);
            Check(upper.Config.MagneticExpandedPlacement!.Height == 700 && lower.CoordinatedMoveBounds.Y == 810,
                "Rejected height growth changed persisted bounds");
            await Task.Delay(350);
            lower.ApplyMagneticResizeBounds(new(100, 810, 500, 700));
            lower.CommitMagneticResizeLayout(lower.CoordinatedMoveBounds);
            BeginMagneticResize(upper);
            Check(ConstrainMagneticResize(upper, proposed).Height == 900, "Narrower lower widget still blocked growth");
            CompleteMagneticResize(upper);
            await _settingsService.FlushPendingSaveAsync(false);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true,
                rejectedGrowth = true, singleShakeAnimation = true, savedHeightUnchanged = true, narrowerLowerAllowed = true }));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
        }
    }

    internal async Task VerifyMagneticResizeRestartAsync()
    {
        string root = MagneticDeskDataPathService.Current.RootPath;
        string previousPath = Path.Combine(root, "magnetic-resize-integration.json");
        if (!File.Exists(previousPath)) return;
        string resultPath = Path.Combine(root, "magnetic-resize-restart.json");
        try
        {
            using var previous = JsonDocument.Parse(await File.ReadAllTextAsync(previousPath));
            Check(previous.RootElement.GetProperty("passed").GetBoolean(), "Previous resize integration failed");
            var windows = GetLoadedDesktopWindows().OfType<WidgetWindowBase>().Where(w => w.Config.MagneticCollapseLinked).ToArray();
            foreach (var saved in previous.RootElement.GetProperty("expanded").EnumerateArray())
            {
                string id = saved.GetProperty("id").GetString()!;
                var expected = JsonSerializer.Deserialize<MagneticExpandedPlacement>(saved.GetProperty("bounds").GetRawText())!;
                Check(Same(windows.Single(w => w.Config.Id == id).Config.MagneticExpandedPlacement!.ToBounds(), expected.ToBounds()),
                    "Restart lost the resized expanded placement");
            }
            var compact = WidgetMagneticCollapseLayout.KeepVisible(WidgetMagneticCollapseLayout.Arrange(windows.Select(w => new MagneticCompactMember(
                w.Config.Id, w.Config.MagneticExpandedPlacement!.ToBounds(), w.MagneticCompactSize)).ToArray()), windows[0].MagneticWorkArea);
            foreach (var window in windows) Check(Same(window.CoordinatedMoveBounds, compact[window.Config.Id]), "Restart lost the resized capsule layout");
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = true, resizedExpandedBounds = true, resizedCapsuleLayout = true }));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
        }
    }

    private async Task VerifyMagneticWidthCrossingsAsync(WidgetWindowBase top, WidgetWindowBase bottom)
    {
        var pair = new[] { top, bottom };
        var saved = pair.ToDictionary(w => w.Config.Id, w => (
            Group: GetMagneticGroupId(w.Config), Linked: w.Config.MagneticCollapseLinked,
            Attachment: w.Config.MagneticAttachmentPlacement?.ToBounds(), Anchor: w.Config.MagneticAttachmentAnchor,
            Expanded: w.Config.MagneticExpandedPlacement!.ToBounds(),
            Compact: w.Config.MagneticCompactPlacement?.ToBounds(), Visible: w.CoordinatedMoveBounds,
            Collapsed: w.IsCompactArrangementActive, Behavior: WidgetCollapseBehaviorNames.GetOverride(w.Config)));
        var resizing = typeof(WidgetWindowBase).GetField("IsResizing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            foreach (var window in pair)
            {
                SetMagneticGroupId(window.Config, null);
                WidgetCollapseBehaviorNames.SetOverride(window.Config, WidgetCollapseBehavior.Click);
                window.RefreshMagneticCollapseBehavior();
                window.RequestCompactState(false, false);
            }
            top.ApplyMagneticResizeBounds(new(500, 150, 600, 400));
            bottom.ApplyMagneticResizeBounds(new(300, 558, 800, 400));
            foreach (var window in pair) window.CommitMagneticResizeLayout(window.CoordinatedMoveBounds);
            LinkMagneticGroups(bottom.Config, top.Config);
            top.RequestCompactState(true, false);
            resizing.SetValue(bottom, true);
            await Task.Delay(100);
            foreach (int width in new[] { 400, 900, 600, 300, 800 })
            {
                RectInt32 before = bottom.CoordinatedMoveBounds;
                BeginMagneticResize(bottom);
                bottom.ApplyMagneticResizeBounds(new(before.X + before.Width - width, before.Y, width, before.Height));
                UpdateMagneticResize(bottom, bottom.CoordinatedMoveBounds);
                CompleteMagneticResize(bottom);
                RectInt32 upper = top.CoordinatedMoveBounds;
                RectInt32 lower = bottom.CoordinatedMoveBounds;
                Check(upper.X + upper.Width == lower.X + lower.Width,
                    "A mixed compact/expanded group changed its right alignment when widths crossed");
                top.RequestCompactState(false, false);
                bottom.RequestCompactState(true, false);
                top.RequestCompactState(true, false);
                bottom.RequestCompactState(false, false);
                await Task.Delay(80);
                upper = top.CoordinatedMoveBounds;
                lower = bottom.CoordinatedMoveBounds;
                Check(upper.X + upper.Width == lower.X + lower.Width,
                    "Collapse/expand lost the attachment alignment after resizing");
            }
        }
        finally
        {
            resizing.SetValue(bottom, false);
            foreach (var window in pair)
            {
                var original = saved[window.Config.Id];
                SetMagneticGroupId(window.Config, original.Group);
                window.Config.MagneticCollapseLinked = original.Linked;
                window.Config.MagneticAttachmentAnchor = original.Anchor;
                window.Config.MagneticAttachmentPlacement = original.Attachment is { } attachment ? MagneticExpandedPlacement.FromBounds(attachment) : null;
                window.Config.MagneticExpandedPlacement = MagneticExpandedPlacement.FromBounds(original.Expanded);
                window.Config.MagneticCompactPlacement = original.Compact is { } compact ? MagneticExpandedPlacement.FromBounds(compact) : null;
                WidgetCollapseBehaviorNames.SetOverride(window.Config, original.Behavior);
                window.RefreshMagneticCollapseBehavior();
                window.ApplyMagneticCollapsedState(original.Collapsed, original.Visible, false, false);
            }
        }
    }

    internal async Task RunMagneticResizeIntegrationAsync()
    {
        string root = MagneticDeskDataPathService.Current.RootPath;
        string path = Path.Combine(root, "magnetic-resize-integration.json");
        var windows = GetLoadedDesktopWindows().OfType<WidgetWindowBase>()
            .Where(w => w.Config.WidgetKind is WidgetKind.QuickCapture or WidgetKind.File or WidgetKind.Todo).ToArray();
        var source = windows.First(w => w.Config.WidgetKind == WidgetKind.QuickCapture);
        var files = windows.First(w => w.Config.WidgetKind == WidgetKind.File);
        var todo = windows.First(w => w.Config.WidgetKind == WidgetKind.Todo);
        var begin = typeof(WidgetWindowBase).GetMethod("BeginCompactInteraction", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var end = typeof(WidgetWindowBase).GetMethod("EndCompactInteraction", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resizing = typeof(WidgetWindowBase).GetField("IsResizing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var commitResize = typeof(WidgetWindowBase).GetMethod("CommitInteractiveResizeBounds", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try
        {
            await VerifyMagneticWidthCrossingsAsync(files, todo);
            ApplyMagneticCollapse(windows, false, true, false);
            begin.Invoke(files, null);
            await Task.Delay(200);
            LeaveMagneticGroup(source.Config);
            Check(!source.Config.MagneticCollapseLinked, "Detached member kept linked collapse");
            foreach (var window in new[] { files, todo })
                foreach (var config in MagneticSurfaceConfigs(window.Config)) config.MagneticCollapseLinked = false;
            WidgetCollapseBehavior expectedBehavior = WidgetCollapseBehaviorNames.Resolve(
                files.Config, _settingsService.Settings.WidgetCollapseBehavior);
            WidgetCollapseBehaviorNames.SetOverride(source.Config, WidgetCollapseBehavior.Expanded);
            source.RefreshMagneticCollapseBehavior();
            LinkMagneticGroups(source.Config, files.Config);
            Check(windows.All(w => WidgetCollapseBehaviorNames.Resolve(w.Config,
                _settingsService.Settings.WidgetCollapseBehavior) == expectedBehavior), "Arriving member kept its old collapse mode");
            Check(windows.All(w => w.Config.MagneticCollapseLinked && HasMouseLeaveAutoCollapse(w)), "Rejoining did not inherit the shared collapse session");
            UpdateMouseLeaveAutoCollapse(TimeSpan.Zero);
            Check(_mouseLeaveScopes.Count == 1 && _mouseLeaveExpandedWindows.Count == 3, "Rejoined members have separate timers");
            UpdateMouseLeaveAutoCollapse(TimeSpan.FromSeconds(8));
            Check(windows.All(w => !w.IsCompactArrangementActive), "Activity on the group did not reset its shared countdown");
            end.Invoke(files, null);
            _mouseLeaveTimer!.Stop();
            UpdateMouseLeaveAutoCollapse(TimeSpan.FromSeconds(9));
            UpdateMouseLeaveAutoCollapse(TimeSpan.FromSeconds(11));
            Check(_mouseLeaveScopes.Count == 1 && Math.Abs(_mouseLeaveScopes.Values.Single().Policy.RemainingFraction(TimeSpan.FromSeconds(11)) - .6) < .001,
                "The group did not expose one shared countdown progress");
            UpdateMouseLeaveAutoCollapse(TimeSpan.FromSeconds(13.999));
            Check(windows.All(w => !w.IsCompactArrangementActive), "Rejoined group collapsed early");
            UpdateMouseLeaveAutoCollapse(TimeSpan.FromSeconds(14));
            Check(windows.All(w => w.IsCompactArrangementActive), "Rejoined group did not collapse together");
            await Task.Delay(500);
            ApplyMagneticCollapse(windows, false, true, false);
            begin.Invoke(source, null);
            await Task.Delay(200);
            var independentColumn = new[]
            {
                (Window: files, Bounds: new RectInt32(100, 100, 300, 400)),
                (Window: todo, Bounds: new RectInt32(410, 100, 500, 400)),
                (Window: source, Bounds: new RectInt32(710, 510, 200, 300))
            };
            foreach (var item in independentColumn)
            {
                item.Window.ApplyMagneticResizeBounds(item.Bounds);
                item.Window.CommitMagneticResizeLayout(item.Bounds);
                item.Window.Config.MagneticAttachmentPlacement = MagneticExpandedPlacement.FromBounds(item.Bounds);
            }
            // The lower panel used to span both columns before being narrowed.
            source.Config.MagneticAttachmentPlacement = MagneticExpandedPlacement.FromBounds(new(310, 510, 600, 300));
            BeginMagneticResize(files);
            files.ApplyMagneticResizeBounds(new(100, 100, 300, 700));
            UpdateMagneticResize(files, files.CoordinatedMoveBounds);
            Check(source.CoordinatedMoveBounds.Y == 510,
                "Growing the left panel pushed a narrower lower-right panel despite the free space");
            resizing.SetValue(files, true);
            commitResize.Invoke(files, null);
            Check(source.Config.MagneticExpandedPlacement!.Y == 510,
                "Completing the resize restored stale lower-panel placement");
            var rejectionLayout = new[]
            {
                (Window: files, Bounds: new RectInt32(100, 100, 600, 700)),
                (Window: todo, Bounds: new RectInt32(100, 810, 800, 700)),
                (Window: source, Bounds: new RectInt32(1010, 100, 500, 700))
            };
            foreach (var item in rejectionLayout)
            {
                item.Window.ApplyMagneticResizeBounds(item.Bounds);
                item.Window.CommitMagneticResizeLayout(item.Bounds);
                item.Window.Config.MagneticAttachmentPlacement = MagneticExpandedPlacement.FromBounds(item.Bounds);
            }
            int previousRejections = files.ResizeRejectionFeedbackCount;
            BeginMagneticResize(files);
            var proposedGrowth = new RectInt32(100, 100, 600, 900);
            var acceptedGrowth = ConstrainMagneticResize(files, proposedGrowth);
            Check(acceptedGrowth.Height == 700, "A wider lower member did not reject height growth");
            ConstrainMagneticResize(files, proposedGrowth);
            Check(files.ResizeRejectionFeedbackCount == previousRejections + 1,
                "Resize rejection did not produce one feedback animation per gesture");
            files.ApplyMagneticResizeBounds(acceptedGrowth);
            UpdateMagneticResize(files, acceptedGrowth);
            resizing.SetValue(files, true);
            commitResize.Invoke(files, null);
            Check(files.Config.MagneticExpandedPlacement!.Height == 700 && todo.CoordinatedMoveBounds.Y == 810,
                "Rejected growth changed the saved height or moved the lower member");
            await Task.Delay(350);
            var nearlyAlignedRows = new[]
            {
                (Window: files, Bounds: new RectInt32(100, 100, 420, 576)),
                (Window: todo, Bounds: new RectInt32(528, 100, 604, 567)),
                (Window: source, Bounds: new RectInt32(528, 675, 604, 616))
            };
            foreach (var item in nearlyAlignedRows)
            {
                item.Window.ApplyMagneticResizeBounds(item.Bounds);
                item.Window.CommitMagneticResizeLayout(item.Bounds);
                item.Window.Config.MagneticAttachmentPlacement = MagneticExpandedPlacement.FromBounds(item.Bounds);
                item.Window.Config.MagneticAttachmentAnchor = "RightTop";
            }
            ApplyMagneticCollapse(windows, true, true, false);
            await Task.Delay(200);
            RectInt32 lowerBefore = source.CoordinatedMoveBounds;
            BeginMagneticResize(source);
            source.ApplyMagneticResizeBounds(new(lowerBefore.X - 636, lowerBefore.Y,
                lowerBefore.Width + 636, lowerBefore.Height));
            UpdateMagneticResize(source, source.CoordinatedMoveBounds);
            Check(files.CoordinatedMoveBounds.X + files.CoordinatedMoveBounds.Width + 2 == todo.CoordinatedMoveBounds.X,
                "Resizing the lower capsule pulled the upper row apart");
            Check(files.CoordinatedMoveBounds.Y == todo.CoordinatedMoveBounds.Y,
                "Resizing the lower capsule changed the upper row order");
            resizing.SetValue(source, true);
            commitResize.Invoke(source, null);
            ApplyMagneticCollapse(windows, false, true, false);
            await Task.Delay(200);
            source.ApplyMagneticResizeBounds(new(100, 100, 350, 500));
            files.ApplyMagneticResizeBounds(new(460, 100, 330, 250));
            todo.ApplyMagneticResizeBounds(new(460, 360, 330, 300));
            foreach (var window in windows)
            {
                window.Config.MagneticAttachmentPlacement = null;
                window.CommitMagneticResizeLayout(window.CoordinatedMoveBounds);
            }
            BeginMagneticResize(source);
            source.ApplyMagneticResizeBounds(new(100, 100, 430, 550));
            UpdateMagneticResize(source, source.CoordinatedMoveBounds);
            Check(files.CoordinatedMoveBounds.X == 540 && todo.CoordinatedMoveBounds.X == 540, "Live resize did not push the adjoining column");
            resizing.SetValue(source, true);
            commitResize.Invoke(source, null);
            var expanded = windows.ToDictionary(w => w.Config.Id, w => w.CoordinatedMoveBounds);
            foreach (var window in windows)
                Check(Same(window.Config.MagneticExpandedPlacement!.ToBounds(), expanded[window.Config.Id]), "Resize did not save expanded layout");
            ApplyMagneticCollapse(windows, true, true, false);
            await Task.Delay(200);
            expanded = windows.ToDictionary(w => w.Config.Id, w => w.Config.MagneticExpandedPlacement!.ToBounds());
            var expected = WidgetMagneticCollapseLayout.KeepVisible(WidgetMagneticCollapseLayout.Arrange(windows.Select(w => new MagneticCompactMember(
                w.Config.Id, expanded[w.Config.Id], w.MagneticCompactSize)).ToArray()), source.MagneticWorkArea);
            foreach (var window in windows)
            {
                Check(Same(window.CoordinatedMoveBounds, expected[window.Config.Id]), "Resize scrambled the capsule layout");
                Check(window.CoordinatedMoveBounds.Width == window.MagneticCompactSize.Width, "A capsule lost its configured width");
            }
            ApplyMagneticCollapse(windows, false, true, false);
            await Task.Delay(200);
            foreach (var window in windows) Check(Same(window.CoordinatedMoveBounds, expanded[window.Config.Id]), "Reopening lost the resized layout");
            ApplyMagneticCollapse(windows, true, true, false);
            await Task.Delay(200);
            RectInt32 capsuleBefore = source.CoordinatedMoveBounds;
            RectInt32 fileCapsuleBefore = files.CoordinatedMoveBounds;
            BeginMagneticResize(source);
            source.ApplyMagneticResizeBounds(new(capsuleBefore.X, capsuleBefore.Y, capsuleBefore.Width + 40, capsuleBefore.Height));
            UpdateMagneticResize(source, source.CoordinatedMoveBounds);
            resizing.SetValue(source, true);
            commitResize.Invoke(source, null);
            Check(files.CoordinatedMoveBounds.X == fileCapsuleBefore.X + 40, "Resizing a capsule did not push its companion");
            var compactResizeExpanded = windows.ToDictionary(w => w.Config.Id, w => w.Config.MagneticExpandedPlacement!.ToBounds());
            ApplyMagneticCollapse(windows, false, true, false);
            await Task.Delay(200);
            foreach (var window in windows)
                Check(Same(window.CoordinatedMoveBounds, compactResizeExpanded[window.Config.Id]), "Capsule resize corrupted expanded placement");
            ApplyMagneticCollapse(windows, true, true, false);
            await Task.Delay(200);
            foreach (var window in windows)
                Check(window.CoordinatedMoveBounds.Width == window.MagneticCompactSize.Width, "Capsule resize did not persist its width");
            end.Invoke(source, null);
            await _settingsService.FlushPendingSaveAsync(false);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = true, widerLowerResizeRejected = true, rejectionAnimation = true, independentLowerRightResize = true, lowerCapsuleUpperRow = true, sharedTimerAfterRejoin = true,
                liveResizeReflow = true, ownCapsuleWidths = true, resizedLayoutRestored = true, compactResizeReflow = true,
                expanded = windows.Select(w => new { id = w.Config.Id, bounds = w.Config.MagneticExpandedPlacement }) }));
            await RunCapsuleStyleIntegrationAsync();
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
            App.Log($"[MagneticResizeIntegration] Failed: {ex}");
        }
        finally
        {
            resizing.SetValue(source, false);
            end.Invoke(files, null);
            end.Invoke(source, null);
        }
    }
}
#endif
