using MagneticDesk.Helpers;
using MagneticDesk.Platform;
using MagneticDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace MagneticDesk.Views;

public sealed partial class OnboardingWindow : Window
{
    internal const int CurrentOnboardingVersion = 4;
    private const int DesiredWindowWidth = 1120;
    private const int DesiredWindowHeight = 720;
    private const int MinWindowWidth = 740;
    private const int MinWindowHeight = 560;
    private const int StepCount = 3;
    private static readonly UIntPtr OnboardingWindowSubclassId = new(0xD05C0B01);
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localizationService;
    private readonly AppWindow _appWindow;
    private readonly IntPtr _hWnd;
    private readonly Win32Helper.SubclassProc _windowSubclassProc;
    private int _stepIndex;
    private bool _isClosed;
    private bool _completing;
    private Task _createWidgetTask = Task.CompletedTask;

    public OnboardingWindow(SettingsService settingsService, LocalizationService localizationService, int initialStep = 0)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        _stepIndex = Math.Clamp(initialStep, 0, StepCount - 1);
        _windowSubclassProc = CallbackDepth.WrapSubclassProc(WindowSubclassProc);
        InitializeComponent();
        _hWnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hWnd));
        AppBranding.ApplyWindowIcon(_appWindow);
        WindowsCompatibilityService.ApplySafeBackdrop(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarHost);
        ResizeAndCenterForDisplay();
        InstallMinimumSizeHook();
        _localizationService.LanguageChanged += OnLanguageChanged;
        RootGrid.Loaded += (_, _) => { SetupStep(); RefreshShortcut(); };
        Closed += (_, _) =>
        {
            _isClosed = true;
            RemoveMinimumSizeHook();
            _localizationService.LanguageChanged -= OnLanguageChanged;
            WindowsCompatibilityService.ReleaseTitleBarExtensions(this, _appWindow);
        };
        Title = _localizationService.T("Onboarding.WindowTitle");
    }

    private void ResizeAndCenterForDisplay()
    {
        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        double scale = GetCurrentDpiScale();
        int width = Math.Min(ToPhysicalPixels(DesiredWindowWidth, scale), area.Width - 40);
        int height = Math.Min(ToPhysicalPixels(DesiredWindowHeight, scale), area.Height - 40);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(Math.Max(1, width), Math.Max(1, height)));
        _appWindow.Move(new Windows.Graphics.PointInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2));
        if (_appWindow.Presenter is OverlappedPresenter presenter) presenter.IsMinimizable = false;
    }

    public void RestartIntro(int stepIndex = 0)
    {
        _stepIndex = Math.Clamp(stepIndex, 0, StepCount - 1);
        LayoutPreview.ResetDemo();
        SetupStep();
        RefreshShortcut();
    }

    private void NavigateToStep(int newStep)
    {
        if (_completing || newStep < 0 || newStep >= StepCount) return;
        _stepIndex = newStep;
        _settingsService.Settings.OnboardingStepIndex = newStep;
        _settingsService.SaveDebounced(notifySubscribers: false);
        SetupStep();
        RefreshShortcut();
    }

    private void Step_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int step)) NavigateToStep(step);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => NavigateToStep(_stepIndex - 1);
    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stepIndex < StepCount - 1) NavigateToStep(_stepIndex + 1);
        else await CompleteOnboardingAsync();
    }
    private async void Skip_Click(object sender, RoutedEventArgs e) => await CompleteOnboardingAsync();

    private async Task CompleteOnboardingAsync()
    {
        if (_completing) return;
        _completing = true;
        NextButton.IsEnabled = BackButton.IsEnabled = SkipButton.IsEnabled = CreateWidgetButton.IsEnabled = false;
        try
        {
            await _createWidgetTask;
            _settingsService.Settings.HasCompletedOnboarding = true;
            _settingsService.Settings.CompletedOnboardingVersion = CurrentOnboardingVersion;
            _settingsService.Settings.OnboardingStepIndex = 0;
            await _settingsService.SaveAsync();
            if (!_isClosed) Close();
        }
        finally { _completing = false; }
    }

    private void OnLanguageChanged()
    {
        if (_isClosed) return;
        Title = _localizationService.T("Onboarding.WindowTitle");
        Localized.RefreshAll(_localizationService);
        SetupStep();
        RefreshShortcut();
    }

    private void InstallMinimumSizeHook()
    {
        Win32Helper.SetWindowSubclass(_hWnd, _windowSubclassProc, OnboardingWindowSubclassId, UIntPtr.Zero);
    }

    private void RemoveMinimumSizeHook()
    {
        Win32Helper.RemoveWindowSubclass(_hWnd, _windowSubclassProc, OnboardingWindowSubclassId);
    }

    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    private IntPtr WindowSubclassProc(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr refData)
    {
        const uint WmGetMinMaxInfo = 0x0024;
        const uint WmNcDestroy = 0x0082;

        if (message == WmGetMinMaxInfo)
        {
            var minMaxInfo = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(lParam);
            double scale = GetCurrentDpiScale();
            minMaxInfo.MinTrackSize.X = Math.Max(minMaxInfo.MinTrackSize.X, ToPhysicalPixels(MinWindowWidth, scale));
            minMaxInfo.MinTrackSize.Y = Math.Max(minMaxInfo.MinTrackSize.Y, ToPhysicalPixels(MinWindowHeight, scale));
            System.Runtime.InteropServices.Marshal.StructureToPtr(minMaxInfo, lParam, false);
            return IntPtr.Zero;
        }

        if (message == WmNcDestroy)
        {
            RemoveMinimumSizeHook();
        }

        return Win32Helper.DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private double GetCurrentDpiScale()
    {
        return Win32Helper.GetDpiScaleForWindow(_hWnd, RootGrid.XamlRoot);
    }

    private static int ToPhysicalPixels(int logicalPixels, double scale)
    {
        return (int)Math.Round(logicalPixels * scale);
    }
}
