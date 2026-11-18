using System.Text.Json;
using MagneticDesk.Models;
using MagneticDesk.Services;

namespace MagneticDesk.Tests;

public sealed class WidgetManagerLayoutHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "magnetic-desk.Tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsService _settings;
    private readonly WidgetManager _manager;
    private readonly WidgetConfig _first = new() { Id = "first", X = 100, Y = 150 };
    private readonly WidgetConfig _second = new() { Id = "second", X = 420, Y = 150 };

    public WidgetManagerLayoutHistoryTests()
    {
        _settings = new SettingsService(Path.Combine(_root, "settings"));
        var files = new FileService();
        _manager = new WidgetManager(_settings, files, TestOrganizerServices.Create(_settings, files),
            new ThemeService(_settings), new QuickCaptureService(new QuickCaptureStore(Path.Combine(_root, "capture"))),
            () => Directory.CreateDirectory(Path.Combine(_root, "desktop")).FullName, recycleManagedFolderDeletes: false);
        _settings.Settings.Widgets.AddRange([_first, _second]);
    }

    [Fact]
    public void NoOpGestureDoesNotAddUndo()
    {
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Move");
        _manager.CompleteLayoutOperation(_first.Id);
        Assert.False(_manager.CanUndoLayout);
    }

    [Fact]
    public void MoveAndResizeUndoInReverseOrder()
    {
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Move");
        _first.X = 600;
        _manager.CompleteLayoutOperation(_first.Id);
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Resize");
        _first.Width = 800;
        _manager.CompleteLayoutOperation(_first.Id);
        Assert.True(_manager.UndoLayout());
        Assert.Equal(300, _first.Width);
        Assert.Equal(600, _first.X);
        Assert.True(_manager.UndoLayout());
        Assert.Equal(100, _first.X);
        Assert.False(_manager.UndoLayout());
    }

    [Fact]
    public void GroupResizeRestoresEveryAffectedMemberTogether()
    {
        _first.MagneticGroupId = _second.MagneticGroupId = "group";
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Resize");
        _first.Width = 600;
        _second.X = 720;
        _manager.CompleteLayoutOperation(_first.Id);
        Assert.True(_manager.UndoLayout());
        Assert.Equal(300, _first.Width);
        Assert.Equal(420, _second.X);
        Assert.False(_manager.CanUndoLayout);
    }

    [Fact]
    public void ControlDetachAndMoveFormOneUndoAction()
    {
        _first.MagneticGroupId = _second.MagneticGroupId = "group";
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Move");
        Assert.True(_manager.LeaveMagneticGroup(_first));
        _first.X = 800;
        Assert.False(_manager.CanUndoLayout);
        _manager.CompleteLayoutOperation(_first.Id);
        Assert.True(_manager.UndoLayout());
        Assert.Equal("group", _first.MagneticGroupId);
        Assert.Equal("group", _second.MagneticGroupId);
        Assert.Equal(100, _first.X);
        Assert.False(_manager.CanUndoLayout);
    }

    [Fact]
    public void UnrelatedWidgetAndContentEditsSurviveUndo()
    {
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Move");
        _first.X = 600;
        _second.Y = 800;
        _manager.CompleteLayoutOperation(_first.Id);
        _first.Metadata["Notes"] = "later edit";
        Assert.True(_manager.UndoLayout());
        Assert.Equal(800, _second.Y);
        Assert.Equal("later edit", _first.Metadata["Notes"]);
    }

    [Fact]
    public void LaterPresentationSettingsSurviveMoveUndo()
    {
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Move");
        _first.X = 600;
        _manager.CompleteLayoutOperation(_first.Id);
        _first.CapsuleStyle = "Segments";
        _first.Metadata[WidgetCollapseBehaviorNames.MetadataKey] = "Smart";
        Assert.True(_manager.UndoLayout());
        Assert.Equal("Segments", _first.CapsuleStyle);
        Assert.Equal("Smart", _first.Metadata[WidgetCollapseBehaviorNames.MetadataKey]);
    }

    [Fact]
    public async Task UntrackedPlacementInvalidatesOlderHistoryPermanently()
    {
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Move");
        _first.X = 600;
        _manager.CompleteLayoutOperation(_first.Id);
        await _manager.MoveSurfaceToDisplayAsync(_first.Id, "another-display");
        Assert.False(_manager.CanUndoLayout);
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Resize");
        _first.Width = 800;
        _manager.CompleteLayoutOperation(_first.Id);
        Assert.True(_manager.UndoLayout());
        Assert.False(_manager.CanUndoLayout);
    }

    [Fact]
    public void GroupLockPersistsAndPreservesIndividualLocks()
    {
        _first.MagneticGroupId = _second.MagneticGroupId = "group";
        _second.IsSizeLocked = true;
        Assert.True(_manager.SetMagneticLayoutLocked(_first, true));
        Assert.True(_manager.IsMagneticLayoutLocked(_second));
        Assert.False(_manager.LeaveMagneticGroup(_second));
        var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(_settings.Settings,
            SettingsJsonContext.Default.AppSettings), SettingsJsonContext.Default.AppSettings)!;
        Assert.All(restored.Widgets.Where(w => w.MagneticGroupId == "group"),
            w => Assert.Equal("group", w.Metadata[WidgetManager.MagneticLayoutLockMetadataKey]));
        Assert.True(_manager.SetMagneticLayoutLocked(_second, false));
        Assert.False(_manager.IsMagneticLayoutLocked(_first));
        Assert.True(_second.IsSizeLocked);
        Assert.False(_first.IsPositionLocked);
    }

    [Fact]
    public void CapsuleReorderCanBeUndoneEvenWithoutGeometryChanges()
    {
        _settings.Settings.WidgetCapsuleBarOrder.AddRange([_first.Id, _second.Id]);
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Move");
        _settings.Settings.WidgetCapsuleBarOrder.Reverse();
        _manager.CompleteLayoutOperation(_first.Id);
        Assert.True(_manager.UndoLayout());
        Assert.Equal(new[] { _first.Id, _second.Id }, _settings.Settings.WidgetCapsuleBarOrder);
    }

    [Fact]
    public void LockBlocksUndoUntilGroupIsUnlocked()
    {
        _first.MagneticGroupId = _second.MagneticGroupId = "group";
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Move");
        _first.X = 600;
        _manager.CompleteLayoutOperation(_first.Id);
        Assert.True(_manager.SetMagneticLayoutLocked(_first, true));
        Assert.False(_manager.UndoLayout());
        Assert.True(_manager.SetMagneticLayoutLocked(_first, false));
        Assert.True(_manager.UndoLayout());
    }

    [Fact]
    public void DeletedWidgetCannotBeResurrectedByUndo()
    {
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Move");
        _first.X = 600;
        _manager.CompleteLayoutOperation(_first.Id);
        _settings.Settings.DeletedWidgetIds.Add(_first.Id);
        Assert.False(_manager.UndoLayout());
        Assert.Equal(600, _first.X);
    }

    [Fact]
    public void CancelledOwnerDoesNotLeaveUndoBlocked()
    {
        _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Move");
        _manager.CancelLayoutOperation(_first.Id);
        _manager.BeginLayoutOperation(_second.Id, "Magnetic.Undo.Move");
        _second.X = 700;
        _manager.CompleteLayoutOperation(_second.Id);
        Assert.True(_manager.UndoLayout());
        Assert.Equal(420, _second.X);
    }

    [Fact]
    public void UndoHistoryIsBoundedToThirtyActions()
    {
        for (int i = 0; i < 35; i++)
        {
            _manager.BeginLayoutOperation(_first.Id, "Magnetic.Undo.Move");
            _first.X++;
            _manager.CompleteLayoutOperation(_first.Id);
        }
        int count = 0;
        while (_manager.UndoLayout()) count++;
        Assert.Equal(30, count);
        Assert.Equal(105, _first.X);
    }

    public void Dispose()
    {
        _settings.SaveAsync().GetAwaiter().GetResult();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
