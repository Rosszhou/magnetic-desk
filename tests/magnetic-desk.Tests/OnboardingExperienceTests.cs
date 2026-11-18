using System.Text.Json;

namespace MagneticDesk.Tests;

public sealed class OnboardingExperienceTests
{
    private static readonly string[] RequiredOnboardingKeys =
    [
        "Onboarding.Back",
        "Onboarding.Next",
        "Onboarding.Start",
        "Onboarding.Step1.Title",
        "Onboarding.Step1.Body",
        "Onboarding.Step1.Hint",
        "Onboarding.Step2.Title",
        "Onboarding.Step2.Body",
        "Onboarding.Step2.PathLabel",
        "Onboarding.Step2.PathSub",
        "Onboarding.Step2.ChangeInSettings",
        "Onboarding.Step2.Hint1",
        "Onboarding.Step2.Hint2",
        "Onboarding.Scene.FileName1",
        "Onboarding.Scene.FileName2",
        "Onboarding.Scene.FileName3",
        "Onboarding.Step3.Title",
        "Onboarding.Step3.Body",
        "Onboarding.Step3.Try",
        "Onboarding.Step3.StatusReady",
        "Onboarding.Step3.StatusShown",
        "Onboarding.Step3.StatusHidden",
        "Onboarding.Step3.StatusDone",
        "Onboarding.Step3.Hint",
        "Onboarding.Step4.Title",
        "Onboarding.Step4.Body",
        "Onboarding.Step4.OpenMenu",
        "Onboarding.Step4.Hint",
        "Onboarding.Step5.Title",
        "Onboarding.Step5.Body",
        "Onboarding.Step5.OptionalBody",
        "Onboarding.Feature.Todo",
        "Onboarding.Feature.QuickCapture",
        "Onboarding.Feature.Search",
        "Onboarding.Scene.MoveBadge",
        "Widget.Empty.ActionsHint"
    ];

    [Fact]
    public void TaskFlow_PresentsThreeStepsWithOneInteractivePreview()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src/magnetic-desk/Views/OnboardingWindow.xaml"));
        string window = File.ReadAllText(Path.Combine(root, "src/magnetic-desk/Views/OnboardingWindow.xaml.cs"));
        Assert.Contains("StepCount = 3", window, StringComparison.Ordinal);
        Assert.Contains("CurrentOnboardingVersion = 4", window, StringComparison.Ordinal);
        foreach (string panel in new[] { "Step1Panel", "Step2Panel", "Step3Panel" })
            Assert.Contains("x:Name=\"" + panel + "\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<controls:MagneticLayoutPreview", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"CreateWidget_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"Settings_Click\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WelcomeBrandArt", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MagneticPractice_UsesDockingPolicyAndCleansUpInterruptedInput()
    {
        string root = FindRepositoryRoot();
        string preview = File.ReadAllText(Path.Combine(root, "src/magnetic-desk/Controls/MagneticLayoutPreview.xaml.cs"));
        string scene = File.ReadAllText(Path.Combine(root, "src/magnetic-desk/Views/OnboardingWindow.Scene.cs"));
        Assert.Contains("WidgetMagneticDockPolicy.FindContact", preview, StringComparison.Ordinal);
        Assert.Contains("DragHandle_PointerCaptureLost", preview, StringComparison.Ordinal);
        Assert.Contains("_timer.Stop()", preview, StringComparison.Ordinal);
        Assert.Contains("UndoDemo()", preview, StringComparison.Ordinal);
        Assert.Contains("Unloaded +=", preview, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(PreviewHost, stacked ? 1 : 0)", scene, StringComparison.Ordinal);
        Assert.DoesNotContain("AnimationIterationBehavior.Forever", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateAndSettingsActions_DriveTheRealEntryPoints()
    {
        string root = FindRepositoryRoot();
        string steps = File.ReadAllText(Path.Combine(root, "src/magnetic-desk/Views/OnboardingWindow.Steps.cs"));
        Assert.Contains("CreateManagedWidgetAsync()", steps, StringComparison.Ordinal);
        Assert.Contains("ShowSettings(\"MagneticLayout\")", steps, StringComparison.Ordinal);
        Assert.Contains("GlobalHotkeyService.FormatActivation", steps, StringComparison.Ordinal);
        Assert.Contains("_createWidgetTask.IsCompleted", steps, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US.json", "layout grid")]
    [InlineData("zh-CN.json", "网格布局")]
    public void TaskFlow_UsesWidgetTermInsteadOfLayoutGrid(
        string fileName,
        string forbiddenTerm)
    {
        string root = FindRepositoryRoot();
        using JsonDocument strings = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Strings",
            fileName)));

        foreach (JsonProperty property in strings.RootElement.EnumerateObject()
                     .Where(property => property.Name.StartsWith(
                         "Onboarding.",
                         StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(
                forbiddenTerm,
                property.Value.GetString(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Onboarding_IsCompletedOnlyByTheWindowAndPersistsProgress()
    {
        string root = FindRepositoryRoot();
        string appCode = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/App.xaml.cs"));
        string windowCode = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/OnboardingWindow.xaml.cs"));

        string ensureMethod = appCode[appCode.IndexOf(
            "private async Task<bool> EnsureOnboardingAsync",
            StringComparison.Ordinal)..appCode.IndexOf(
            "public void ShowOnboarding",
            StringComparison.Ordinal)];
        Assert.DoesNotContain("HasCompletedOnboarding = true", ensureMethod, StringComparison.Ordinal);
        Assert.Contains("OnboardingStepIndex = newStep", windowCode, StringComparison.Ordinal);
        Assert.Contains("CompletedOnboardingVersion = CurrentOnboardingVersion", windowCode, StringComparison.Ordinal);
    }

    [Fact]
    public void CompletionWaitsForWidgetCreationAndPersistsProgress()
    {
        string root = FindRepositoryRoot();
        string code = File.ReadAllText(Path.Combine(root, "src/magnetic-desk/Views/OnboardingWindow.xaml.cs"));
        string completion = code[code.IndexOf("private async Task CompleteOnboardingAsync", StringComparison.Ordinal)..code.IndexOf("private void OnLanguageChanged", StringComparison.Ordinal)];
        Assert.True(completion.IndexOf("await _createWidgetTask", StringComparison.Ordinal) < completion.IndexOf("Close();", StringComparison.Ordinal));
        Assert.Contains("HasCompletedOnboarding = true", completion, StringComparison.Ordinal);
        Assert.Contains("await _settingsService.SaveAsync()", completion, StringComparison.Ordinal);
        Assert.Contains("if (_completing) return", completion, StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureWidgetEnableOperations_AreSerializedAndShowingRestoresVisibility()
    {
        string root = FindRepositoryRoot();
        string manager = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Services/WidgetManager.cs"));
        string featureManager = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Services/WidgetManager.FeatureWidgets.cs"));
        string quickCaptureCoordinator = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Services/QuickCaptureSettingsCoordinator.cs"));
        string quickCaptureEditor = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Features/QuickCapture/QuickCaptureSettingsViewModel.cs"));

        Assert.Contains("_featureWidgetUpdateLocks", featureManager, StringComparison.Ordinal);
        Assert.Contains("await updateLock.WaitAsync()", featureManager, StringComparison.Ordinal);
        Assert.Contains("updateLock.Release()", featureManager, StringComparison.Ordinal);
        Assert.Contains("config.IsVisible = true;", manager, StringComparison.Ordinal);
        Assert.Contains("_settings.SetEnabledAsync(value, reveal: value)",
            quickCaptureEditor, StringComparison.Ordinal);
        Assert.Contains("await _widgetGate.WaitAsync(linked.Token)",
            quickCaptureCoordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyFileWidget_KeepsOneConciseActionHint()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Controls/WidgetContents/FileSurfaceContent.xaml"));

        Assert.Contains(
            "svc:Localized.Key=\"Widget.Empty.ActionsHint\"",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Text=\"{Binding EmptyStateText}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TaskFlow_IsLocalizedInEveryLanguage()
    {
        string root = FindRepositoryRoot();
        string stringsDirectory = Path.Combine(root, "src/magnetic-desk/Strings");
        string viewsDirectory = Path.Combine(root, "src/magnetic-desk/Views");
        var referencedKeys = new HashSet<string>(RequiredOnboardingKeys, StringComparer.Ordinal);

        IEnumerable<string> onboardingSources =
        [
            Path.Combine(viewsDirectory, "OnboardingWindow.xaml"),
            .. Directory.GetFiles(viewsDirectory, "OnboardingWindow*.cs")
        ];
        foreach (string sourcePath in onboardingSources)
        {
            string source = File.ReadAllText(sourcePath);
            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(
                         source,
                         "svc:Localized\\.Key=\"([^\"]+)\""))
            {
                referencedKeys.Add(match.Groups[1].Value);
            }

            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(
                         source,
                         "(?:\\.T|\\.Format)\\(\"([^\"]+)\""))
            {
                referencedKeys.Add(match.Groups[1].Value);
            }
        }

        foreach (string path in Directory.GetFiles(stringsDirectory, "*.json"))
        {
            using JsonDocument strings = JsonDocument.Parse(File.ReadAllText(path));
            foreach (string key in referencedKeys)
            {
                Assert.True(
                    strings.RootElement.TryGetProperty(key, out JsonElement value) &&
                    !string.IsNullOrWhiteSpace(value.GetString()),
                    $"{Path.GetFileName(path)} is missing {key}.");
            }
        }
    }

    [Fact]
    public void ChineseTaskFlow_IsDirectAndKeepsAdvancedConceptsOutOfFirstSteps()
    {
        string root = FindRepositoryRoot();
        using JsonDocument strings = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Strings/zh-CN.json")));

        Assert.Contains(
            "移动",
            strings.RootElement.GetProperty("Onboarding.Step2.Body").GetString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "复制",
            strings.RootElement.GetProperty("Onboarding.Step2.Body").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "映射",
            strings.RootElement.GetProperty("Onboarding.Step4.Body").GetString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "叫回来",
            strings.RootElement.GetProperty("Onboarding.Step3.StatusHidden").GetString(),
            StringComparison.Ordinal);
        Assert.True(
            strings.RootElement.GetProperty("Onboarding.Step1.Title").GetString()!.Length <= 24,
            "The first-step title should stay scannable.");
        Assert.True(
            strings.RootElement.GetProperty("Onboarding.Step3.Body").GetString()!.Length < 60,
            "The summon-step explanation should stay scannable.");
    }

    [Fact]
    public void DefaultManagedDropAction_RemainsMove()
    {
        string root = FindRepositoryRoot();
        string settingsModel = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Models/FileWidgetSettingsSlice.cs"));
        string userGuide = File.ReadAllText(Path.Combine(
            root,
            "docs/articles/15-getting-started.md"));

        Assert.Contains("ManagedDropAction { get; set; } = \"Move\"", settingsModel, StringComparison.Ordinal);
        Assert.Contains("默认拖入行为是移动", userGuide, StringComparison.Ordinal);
        Assert.DoesNotContain("默认拖入行为是复制", userGuide, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstStepArrivesDirectlyWithoutAnIntroScreen()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/OnboardingWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/OnboardingWindow.xaml.cs"));

        Assert.DoesNotContain("IntroOverlay", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IntroMarkHost", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayIntroSequence", code, StringComparison.Ordinal);
        Assert.Contains("RootGrid.Loaded +=", code, StringComparison.Ordinal);
        Assert.Contains("SetupStep();", code, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "magnetic-desk.slnx")) ||
                File.Exists(Path.Combine(directory.FullName, "magnetic-desk.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the MagneticDesk repository root.");
    }
}
