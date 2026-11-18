using System.Diagnostics;
using MagneticDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;

namespace MagneticDesk.Controls;

public sealed partial class MagneticLayoutPreview : UserControl
{
    public static readonly DependencyProperty SnapSpacingProperty = DependencyProperty.Register(
        nameof(SnapSpacing), typeof(double), typeof(MagneticLayoutPreview), new PropertyMetadata(8.0, OnPreviewSettingChanged));
    public static readonly DependencyProperty SnapEnabledProperty = DependencyProperty.Register(
        nameof(SnapEnabled), typeof(bool), typeof(MagneticLayoutPreview), new PropertyMetadata(true, OnPreviewSettingChanged));

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Stopwatch _hold = new();
    private LocalizationService? _localization;
    private Point _pointerStart;
    private RectInt32 _dragStart;
    private (RectInt32 Bounds, bool Docked)? _undoState;
    private MagneticDockContact? _contact;
    private bool _dragging;
    private bool _hasMoved;

    public double SnapSpacing { get => (double)GetValue(SnapSpacingProperty); set => SetValue(SnapSpacingProperty, value); }
    public bool SnapEnabled { get => (bool)GetValue(SnapEnabledProperty); set => SetValue(SnapEnabledProperty, value); }
    public bool IsDocked { get; private set; }

    public MagneticLayoutPreview()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateCountdown();
        Loaded += (_, _) =>
        {
            _localization = App.Current.LocalizationService;
            _localization.LanguageChanged += RefreshStatus;
            ResetDemo();
        };
        Unloaded += (_, _) =>
        {
            if (_localization is not null) _localization.LanguageChanged -= RefreshStatus;
            _dragging = false;
            _timer.Stop();
            DragHandle.ReleasePointerCaptures();
        };
    }

    private static void OnPreviewSettingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((MagneticLayoutPreview)sender).ResetDemo();

    private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => ResetDemo();

    public void ResetDemo()
    {
        if (PreviewCanvas is null || PreviewCanvas.ActualWidth < 1) return;
        _dragging = false;
        _timer.Stop();
        _hold.Reset();
        DragHandle.ReleasePointerCaptures();
        _contact = null;
        _undoState = null;
        _hasMoved = false;
        IsDocked = false;
        double width = Math.Clamp((PreviewCanvas.ActualWidth - 68) / 2, 130, 208);
        FixedCard.Width = MovingCard.Width = width;
        Canvas.SetLeft(FixedCard, 20);
        Canvas.SetTop(FixedCard, 52);
        Canvas.SetLeft(MovingCard, PreviewCanvas.ActualWidth - width - 20);
        Canvas.SetTop(MovingCard, 82);
        HideGuides();
        RefreshStatus();
    }

    private static RectInt32 Bounds(FrameworkElement element) => new(
        (int)Math.Round(Canvas.GetLeft(element)), (int)Math.Round(Canvas.GetTop(element)),
        (int)Math.Round(element.Width), (int)Math.Round(element.Height));

    private void DragHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(PreviewCanvas).Properties.IsLeftButtonPressed) return;
        _pointerStart = e.GetCurrentPoint(PreviewCanvas).Position;
        _dragStart = Bounds(MovingCard);
        _undoState = (_dragStart, IsDocked);
        _dragging = true;
        _hasMoved = false;
        IsDocked = false;
        PreviewCanvas.Focus(FocusState.Pointer);
        DragHandle.CapturePointer(e.Pointer);
        RefreshStatus();
        e.Handled = true;
    }

    private void DragHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        Point pointer = e.GetCurrentPoint(PreviewCanvas).Position;
        double x = Math.Clamp(_dragStart.X + pointer.X - _pointerStart.X, 4, Math.Max(4, PreviewCanvas.ActualWidth - MovingCard.Width - 4));
        double y = Math.Clamp(_dragStart.Y + pointer.Y - _pointerStart.Y, 4, Math.Max(4, PreviewCanvas.ActualHeight - MovingCard.Height - 4));
        _hasMoved |= Math.Abs(pointer.X - _pointerStart.X) + Math.Abs(pointer.Y - _pointerStart.Y) >= 4;
        var fixedBounds = Bounds(FixedCard);
        bool aligned = SnapEnabled && Math.Abs(y - fixedBounds.Y) <= 10;
        if (aligned) y = fixedBounds.Y;
        Canvas.SetLeft(MovingCard, x);
        Canvas.SetTop(MovingCard, y);
        Canvas.SetLeft(AlignmentLine, Math.Min(x, fixedBounds.X));
        Canvas.SetTop(AlignmentLine, fixedBounds.Y);
        AlignmentLine.Width = Math.Max(x + MovingCard.Width, fixedBounds.X + fixedBounds.Width) - Math.Min(x, fixedBounds.X);
        AlignmentLine.Visibility = aligned ? Visibility.Visible : Visibility.Collapsed;
        MagneticDockContact? contact = _hasMoved
            ? WidgetMagneticDockPolicy.FindContact(Bounds(MovingCard), fixedBounds,
                (int)Math.Round(SnapSpacing), 24, 40) : null;
        if (contact is { } found)
        {
            if (_contact?.SourceEdge != found.SourceEdge) _hold.Restart();
            _contact = found;
            Canvas.SetLeft(TargetOutline, found.DockedBounds.X);
            Canvas.SetTop(TargetOutline, found.DockedBounds.Y);
            TargetOutline.Width = found.DockedBounds.Width;
            TargetOutline.Height = found.DockedBounds.Height;
            TargetOutline.Visibility = Visibility.Visible;
            Canvas.SetLeft(DockProgress, Math.Clamp(found.DockedBounds.X, 8, PreviewCanvas.ActualWidth - 60));
            Canvas.SetTop(DockProgress, Math.Max(6, found.DockedBounds.Y - 36));
            DockProgress.Visibility = Visibility.Visible;
            _timer.Start();
            UpdateCountdown();
        }
        else
        {
            _contact = null;
            _hold.Reset();
            _timer.Stop();
            TargetOutline.Visibility = DockProgress.Visibility = Visibility.Collapsed;
        }
        RefreshStatus();
        e.Handled = true;
    }

    private void UpdateCountdown()
    {
        if (_contact is not { } contact || !_dragging) return;
        DockProgressText.Text = Math.Max(1, (int)Math.Ceiling(3 - _hold.Elapsed.TotalSeconds)) + "s";
        if (_hold.Elapsed.TotalSeconds < 3) return;
        Canvas.SetLeft(MovingCard, contact.DockedBounds.X);
        Canvas.SetTop(MovingCard, contact.DockedBounds.Y);
        IsDocked = true;
        EndDrag();
        DragHandle.ReleasePointerCaptures();
    }

    private void DragHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndDrag();
        DragHandle.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void DragHandle_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndDrag();

    private void EndDrag()
    {
        _dragging = false;
        _timer.Stop();
        _contact = null;
        HideGuides();
        RefreshStatus();
    }

    private void HideGuides() => TargetOutline.Visibility = AlignmentLine.Visibility = DockProgress.Visibility = Visibility.Collapsed;

    public bool UndoDemo()
    {
        if (_undoState is not { } state) return false;
        EndDrag();
        DragHandle.ReleasePointerCaptures();
        Canvas.SetLeft(MovingCard, state.Bounds.X);
        Canvas.SetTop(MovingCard, state.Bounds.Y);
        _undoState = null;
        IsDocked = state.Docked;
        RefreshStatus();
        return true;
    }

    private void Undo_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) => args.Handled = UndoDemo();
    private void Reset_Click(object sender, RoutedEventArgs e) => ResetDemo();

    private void RefreshStatus()
    {
        if (_localization is null) return;
        StatusText.Text = _localization.T(IsDocked ? "Design.Preview.Joined" : _contact is not null
            ? "Design.Preview.Ready" : "Design.Preview.Instruction");
    }
}
