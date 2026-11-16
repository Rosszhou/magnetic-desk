using System.Text.Json;

namespace MagneticDesk.Tests;

public sealed class SettingsCopyAndHierarchyTests
{
    [Fact]
    public void CapsuleModeIsTopLevelWithCollapseAndExpansionControls()
    {
        string xaml = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Views/SettingsWindow.xaml"));
        string capsule = File.ReadAllText(TestPaths.FromRepository("src/magnetic-desk/Views/SettingsSections/CapsuleModeSettingsSection.xaml"));
        Assert.Contains("Tag=\"CapsuleMode\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Settings.Section.CapsuleMode", capsule, StringComparison.Ordinal);
        Assert.Contains("{Binding CollapseBehavior, Mode=TwoWay}", capsule, StringComparison.Ordinal);
        Assert.Contains("{Binding ExpansionDirection, Mode=TwoWay}", capsule, StringComparison.Ordinal);
        Assert.DoesNotContain("WidgetCapsuleModeEnabled", capsule, StringComparison.Ordinal);
    }

    [Fact]
    public void OutcomeBasedSettings_UseDirectChoicesInsteadOfAmbiguousMasterSwitches()
    {
        string root = FindRepositoryRoot();
        string windowXaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsWindow.xaml"));
        string appearanceXaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsSections/AppearanceSettingsSection.xaml"));
        string fileWidgetXaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsSections/FileWidgetSettingsSection.xaml"));
        string appSettings = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Models/AppSettings.cs"));
        string fileWidgetSettings = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Models/FileWidgetSettingsSlice.cs"));
        string routes = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsWindow.xaml.cs"));

        Assert.Contains("LayoutDensity", appearanceXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsOn=\"{Binding UseSystemAccentColor", appearanceXaml, StringComparison.Ordinal);

        Assert.Contains("{Binding FileOpenMethod, Mode=TwoWay}", windowXaml, StringComparison.Ordinal);
        Assert.Contains("{Binding ShowDesktopBehavior, Mode=TwoWay}", windowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsOn=\"{Binding DoubleClickToOpen", windowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsOn=\"{Binding KeepWidgetsVisibleOnShowDesktop", windowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsOn=\"{Binding WeatherAutoLocation", windowXaml, StringComparison.Ordinal);

        // File stacking is redesigned around the auto-stacking switch as the
        // section's first-level expander; the master switch is retired.
        Assert.Contains("controls:SettingsComboBox.Value=\"{x:Bind FileStack.OpenMode, Mode=TwoWay}\"", fileWidgetXaml, StringComparison.Ordinal);
        Assert.Contains(
            "HeaderKey=\"Settings.FileStacks.Auto.Title\"",
            windowXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "HeaderKey=\"Settings.FileStacks.OpenMode.Title\"",
            windowXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsOn=\"{Binding AutoStacking, Mode=TwoWay}\"",
            windowXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "public bool FileStacksEnabled { get; set; } = true;",
            fileWidgetSettings,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public bool WidgetCapsuleModeEnabled",
            appSettings,
            StringComparison.Ordinal);
        Assert.Contains(
            "{x:Bind FeatureWidgets.FolderOpenBehavior, Mode=TwoWay}",
            fileWidgetXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsOn=\"{x:Bind Interaction.FileItemContextMenuEnabled, Mode=TwoWay}\"",
            fileWidgetXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "public bool FileItemSystemContextMenuEnabled { get; set; }",
            fileWidgetSettings,
            StringComparison.Ordinal);

        Assert.Contains("{Binding HoverButtonActionsSummary}", windowXaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"HoverButtonActionsDropDown_Click\"", windowXaml, StringComparison.Ordinal);
        Assert.Equal(
            1,
            windowXaml.Split(
                "Click=\"HoverButtonActionsDropDown_Click\"",
                StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("InteractionHoverSettings", windowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("InteractionHoverSettings", routes, StringComparison.Ordinal);
        Assert.DoesNotContain("IsOn=\"{Binding ShowHoverButtons", windowXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmedChineseCopy_UsesPreciseTerms()
    {
        string root = FindRepositoryRoot();
        using JsonDocument strings = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Strings/zh-CN.json")));

        IReadOnlyDictionary<string, string> expected = new Dictionary<string, string>
        {
            ["Settings.Nav.CapsuleMode"] = "胶囊模式",
            ["Settings.CollapseBehavior.Title"] = "默认显示方式",
            ["Settings.CollapseBehavior.Expanded"] = "始终展开",
            ["Widget.CollapseBehavior.Title"] = "展开方式",
            ["Widget.CollapseBehavior.System"] = "跟随默认",
            ["Widget.CollapseBehavior.Click"] = "单击展开",
            ["Widget.CollapseBehavior.Smart"] = "悬停自动展开",
            ["Settings.Capsule.Overrides.FollowGlobal"] = "恢复默认",
            ["Widget.OpenStorageFolder"] = "打开格子文件夹",
            ["Widget.ShowInExplorer"] = "打开文件所在的位置",
            ["Search.Menu.AttachToTodo"] = "添加到待办",
            ["QuickCapture.SaveToRecords"] = "保存为便签",
            ["QuickCapture.PinToRecords"] = "固定这条便签",
            ["Widget.Stack.FollowDefaults"] = "跟随默认设置",
            ["Widget.Stack.DisableGroup"] = "不再自动叠放此类",
            ["Widget.Stack.Dissolve"] = "解散叠放",
            ["Widget.Stack.RemoveItem"] = "移出叠放",
            ["Settings.FileStacks.Threshold.Title"] = "自动叠放数量",
            ["Widget.Group.NavigationStyle"] = "标题栏布局",
            ["Settings.WidgetGroupNavigation.Stack"] = "折叠显示",
            ["Settings.WidgetGroupNavigation.Tabs"] = "并排显示",
            ["Settings.WidgetGroupNavigation.Title"] = "标题栏布局",
            ["Settings.WidgetGroupSwitchAnimation.Auto"] = "自动",
            ["Settings.WidgetGroupSwitchAnimation.Description"] = "切换格子组内格子时的过渡效果，自动时按标题栏布局选择方向",
            ["Settings.WidgetGroupSwitchAnimation.Fade"] = "淡入淡出",
            ["Settings.WidgetGroupSwitchAnimation.Horizontal"] = "左右滑动",
            ["Settings.WidgetGroupSwitchAnimation.None"] = "无动画",
            ["Settings.WidgetGroupSwitchAnimation.Title"] = "切换动画",
            ["Settings.WidgetGroupSwitchAnimation.Vertical"] = "上下滑动",
            ["Settings.WidgetGroups.PageDescription"] = "将多个格子放在同一位置，通过标题栏切换。每个格子的内容仍然相互独立。",
            ["Settings.WidgetGroups.FollowDefaultWithValue"] = "跟随默认({0})",
            ["Widget.Group.Join"] = "组合格子…",
            ["Widget.DeleteFolderToRecycleBin"] = "同时移入回收站",
            ["Search.Delete.Action"] = "移入回收站",
            ["Settings.QuickCapture.Format.Title"] = "编辑格式",
            ["Settings.QuickCapture.Format.Description"] = "影响编辑器语法与显示样式",
            ["Settings.Accent.Source.Title"] = "主题色来源",
            ["Settings.OpenMethod.Title"] = "打开方式",
            ["Settings.ShowDesktopBehavior.Title"] = "按 Win+D 后",
            ["Settings.WidgetLayerMode.Title"] = "格子层级",
            ["Settings.WidgetLayerMode.Dynamic"] = "动态层级",
            ["Settings.WidgetLayerMode.DesktopPinned"] = "桌面固定层",
            ["Settings.WidgetLayerMode.QuickReveal"] = "快捷唤起层",
            ["Settings.AttachmentStorageMode.Title"] = "附件保存方式",
            ["Settings.ShowImageFilesAsIcons.Title"] = "图片和视频只显示图标",
            ["Settings.Capsule.WidthMode.Title"] = "展开后的宽度",
            ["Settings.Capsule.WidthMode.Aligned"] = "沿用胶囊宽度",
            ["Settings.Capsule.WidthMode.Independent"] = "使用格子宽度",
            ["Settings.ManagedStorage.ListTitle"] = "已关闭格子的收纳文件夹",
            ["Settings.AttachmentHealth.Title"] = "检查附件",
            ["Settings.RuntimeHealth.Title"] = "后台服务状态",
            ["Settings.RuntimeHealth.Resync"] = "重新同步",
            ["Settings.DataBackup.Description"] = "备份设置、格子、便签、待办和附件副本，不含文件格子中的文件",
            ["Settings.Restore.Description"] = "恢复默认设置，保留语言、开机启动、格子开关和已有内容",
            ["Settings.Restore.Tooltip"] = "恢复默认设置，不会删除已有内容",
            ["Settings.Todo.Group.FooterActions.Description"] = "选择底部显示的剩余待办数量和清除已完成按钮",
            ["Settings.Todo.FooterDisplay.Title"] = "显示内容",
            ["Settings.Todo.ShowFooterStats.Title"] = "剩余待办数量",
            ["Settings.Todo.ShowClearCompleted.Title"] = "清除已完成按钮",
            ["Settings.Onboarding.Description"] = "重新查看格子创建、文件收纳、功能格子、外观和快捷键说明",
            ["Settings.Weather.LocationMode.Title"] = "位置来源",
            ["Settings.FileStacks.Title"] = "文件叠放",
            ["Settings.FileStacks.Auto.Title"] = "自动叠放",
            ["Settings.FileStacks.Status.Manual"] = "仅手动叠放",
            ["Settings.HoverButtonActions.None"] = "不显示"
        };

        foreach ((string key, string value) in expected)
        {
            Assert.Equal(value, strings.RootElement.GetProperty(key).GetString());
        }

        string json = strings.RootElement.GetRawText();
        Assert.DoesNotContain("...", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Deskbox", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureWidgetOverview_UsesColorTitleIconsAndStandardCardSpacing()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsWindow.LocalizationAndWidgets.cs"));

        // The colorful kind icon rides in the composed header (WidgetTitleIcon
        // is a UserControl and cannot fill the native HeaderIcon slot).
        Assert.Contains("new WidgetTitleIcon", source, StringComparison.Ordinal);
        Assert.Contains(
            "IconKind = WidgetTitleIconKindNames.FromWidgetKind(entry.Kind)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Mode = WidgetTitleIconModeNames.Color", source, StringComparison.Ordinal);
        Assert.Contains("IconSize = 16", source, StringComparison.Ordinal);
        Assert.Contains("card.Header = headerPanel;", source, StringComparison.Ordinal);
        Assert.Contains("new SettingsCard", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsOverviewCards_MatchLanguageCardTypographyAndIdentitySpacing()
    {
        string root = FindRepositoryRoot();
        string windowXaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsWindow.xaml"));
        string overviewResources = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Styles/SettingsOverviewResources.xaml"));
        string appearanceXaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsSections/AppearanceSettingsSection.xaml"));
        string fileWidgetXaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsSections/FileWidgetSettingsSection.xaml"));
        string featureRows = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsWindow.LocalizationAndWidgets.cs"));

        // The shared metrics/styles live only in SettingsOverviewResources.xaml;
        // SettingsWindow.xaml merges that dictionary instead of duplicating it.
        Assert.Contains(
            "ms-appx:///Styles/SettingsOverviewResources.xaml",
            windowXaml,
            StringComparison.Ordinal);
        foreach (string resources in new[] { overviewResources })
        {
            string titleStyle = SliceSection(
                resources,
                "x:Key=\"SettingTitleTextStyle\"",
                "</Style>");
            Assert.Contains(
                "<Setter Property=\"FontWeight\" Value=\"Normal\" />",
                titleStyle,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Value=\"SemiBold\"", titleStyle, StringComparison.Ordinal);
            Assert.Contains(
                "<x:Double x:Key=\"SettingsHeaderIconTextSpacing\">20</x:Double>",
                resources,
                StringComparison.Ordinal);
            Assert.Contains(
                "x:Key=\"SettingCardIdentityGridStyle\"",
                resources,
                StringComparison.Ordinal);
        }

        // Drill-down rows are native SettingsCards now; the hand-built
        // identity-grid scaffolding must not come back.
        Assert.Equal(
            0,
            CountOccurrences(
                appearanceXaml,
                "Style=\"{StaticResource SettingCardIdentityGridStyle}\""));
        Assert.Equal(
            0,
            CountOccurrences(
                fileWidgetXaml,
                "Style=\"{StaticResource SettingCardIdentityGridStyle}\""));

        string interaction = SliceSection(
            windowXaml,
            "x:Name=\"InteractionSection\"",
            "x:Name=\"InteractionWindowSettingsSection\"");
        Assert.Equal(
            0,
            CountOccurrences(
                interaction,
                "Style=\"{StaticResource SettingCardIdentityGridStyle}\""));

        string maintenance = SliceSection(
            windowXaml,
            "x:Name=\"MaintenanceSection\"",
            "x:Name=\"BackupRestoreSettingsSection\"");
        Assert.Equal(
            0,
            CountOccurrences(
                maintenance,
                "Style=\"{StaticResource SettingCardIdentityGridStyle}\""));
        // Feature rows are native SettingsCards now; the identity-grid style
        // lookup must not come back.
        Assert.DoesNotContain(
            "SettingsRoot.Resources[\"SettingCardIdentityGridStyle\"]",
            featureRows,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsOverview_PrioritizesOrganizationAndWidgetLayer()
    {
        string root = FindRepositoryRoot();
        string fileWidgetXaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsSections/FileWidgetSettingsSection.xaml"));
        string windowXaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsWindow.xaml"));

        Assert.True(
            fileWidgetXaml.IndexOf("Tag=\"DesktopOrganizationSettings\"", StringComparison.Ordinal) <
            fileWidgetXaml.IndexOf("Tag=\"FileDisplaySettings\"", StringComparison.Ordinal));
        Assert.Contains("Click=\"OrganizeDesktopButton_Click\"", fileWidgetXaml, StringComparison.Ordinal);
        Assert.Contains("DesktopOrganization.Settings.StartAction", fileWidgetXaml, StringComparison.Ordinal);
        Assert.True(
            fileWidgetXaml.IndexOf("Tag=\"FileStackSettings\"", StringComparison.Ordinal) <
            fileWidgetXaml.IndexOf("FeatureWidgets.FolderOpenBehavior", StringComparison.Ordinal));

        int interactionSection = windowXaml.IndexOf(
            "x:Name=\"InteractionSection\"",
            StringComparison.Ordinal);
        int widgetLayer = windowXaml.IndexOf(
            "svc:Localized.HeaderKey=\"Settings.WidgetLayerMode.Title\"",
            interactionSection,
            StringComparison.Ordinal);
        int hoverActions = windowXaml.IndexOf(
            "svc:Localized.HeaderKey=\"Settings.Interaction.Hover.Title\"",
            interactionSection,
            StringComparison.Ordinal);
        int interactionDetail = windowXaml.IndexOf(
            "x:Name=\"InteractionWindowSettingsSection\"",
            interactionSection,
            StringComparison.Ordinal);
        int openMethod = windowXaml.IndexOf(
            "svc:Localized.HeaderKey=\"Settings.OpenMethod.Title\"",
            interactionDetail,
            StringComparison.Ordinal);
        int showDesktopBehavior = windowXaml.IndexOf(
            "svc:Localized.HeaderKey=\"Settings.ShowDesktopBehavior.Title\"",
            interactionDetail,
            StringComparison.Ordinal);
        int globalHotkey = windowXaml.IndexOf(
            "svc:Localized.HeaderKey=\"Settings.GlobalHotkey.Title\"",
            interactionDetail,
            StringComparison.Ordinal);
        int resizeSnap = windowXaml.IndexOf(
            "svc:Localized.HeaderKey=\"Settings.ResizeSnap.Title\"",
            interactionDetail,
            StringComparison.Ordinal);
        int desktopDoubleClick = windowXaml.IndexOf(
            "svc:Localized.HeaderKey=\"Settings.DesktopDoubleClick.Title\"",
            interactionDetail,
            StringComparison.Ordinal);

        int generalSection = windowXaml.IndexOf(
            "x:Name=\"GeneralSection\"",
            StringComparison.Ordinal);
        int language = windowXaml.IndexOf(
            "svc:Localized.HeaderKey=\"Settings.Language.Title\"",
            generalSection,
            StringComparison.Ordinal);
        int attachmentStorage = windowXaml.IndexOf(
            "svc:Localized.HeaderKey=\"Settings.AttachmentStorageMode.Title\"",
            generalSection,
            StringComparison.Ordinal);
        int autoStart = windowXaml.IndexOf(
            "svc:Localized.HeaderKey=\"Settings.AutoStart.Title\"",
            generalSection,
            StringComparison.Ordinal);
        int onboarding = windowXaml.IndexOf(
            "svc:Localized.HeaderKey=\"Settings.Onboarding.Title\"",
            generalSection,
            StringComparison.Ordinal);

        Assert.True(interactionSection >= 0);
        Assert.True(widgetLayer > interactionSection);
        Assert.True(globalHotkey > widgetLayer);
        Assert.True(showDesktopBehavior > globalHotkey);
        Assert.True(openMethod > showDesktopBehavior);
        Assert.True(hoverActions > openMethod);
        Assert.True(resizeSnap > hoverActions);
        Assert.True(desktopDoubleClick > resizeSnap);
        Assert.True(generalSection >= 0);
        Assert.True(generalSection < language);
        Assert.True(language < autoStart);
        Assert.True(autoStart < attachmentStorage);
        Assert.True(attachmentStorage < onboarding);
        Assert.DoesNotContain("InteractionHotkeySettings", windowXaml, StringComparison.Ordinal);
        // Two occurrences are expected: the card HeaderKey plus the combo box
        // AutomationNameKey, which deliberately reuses the same header key so
        // the accessible name stays same-sourced with the visible label.
        Assert.Equal(
            2,
            windowXaml.Split("Settings.WidgetLayerMode.Title", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void SearchHotkey_IsOwnedBySearchSettingsWithoutDuplicateTopHeading()
    {
        string root = FindRepositoryRoot();
        string windowXaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsWindow.xaml"));
        string searchXaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsSections/SearchSettingsSection.xaml"));
        string searchCodeBehind = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsSections/SearchSettingsSection.xaml.cs"));

        Assert.DoesNotContain("SearchHotkeyToggle2", windowXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.Search.Hotkey.Title", windowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SearchHotkeyExpander\"", searchXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SearchHotkeyToggle\"", searchXaml, StringComparison.Ordinal);
        Assert.Contains("Settings.Search.Hotkey.Title", searchXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.Search.Scope.Title", searchXaml, StringComparison.Ordinal);
        Assert.Contains("_viewModel.State.FeatureEnabled", searchCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void WidgetCreationEntries_AreAvailableFromTrayAndEveryWidgetTitleBar()
    {
        string root = FindRepositoryRoot();
        string traySource = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/App.Tray.cs"));
        string widgetCommands = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/ContentWidgetWindow.Commands.cs"));

        int trayMapEntry = traySource.IndexOf(
            "contextMenu.Items.Add(mapFolderItem)",
            StringComparison.Ordinal);
        int trayFeatureEntry = traySource.IndexOf(
            "contextMenu.Items.Add(addFeatureWidgetItem)",
            StringComparison.Ordinal);

        Assert.True(trayMapEntry >= 0);
        Assert.True(trayFeatureEntry > trayMapEntry);
        Assert.Contains("OpenFeatureWidgetsFromTray", traySource, StringComparison.Ordinal);
        Assert.Contains("ShowSettings(\"FeatureWidgets\")", traySource, StringComparison.Ordinal);

        Assert.DoesNotContain("contentCanAdd", widgetCommands, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_config.WidgetKind == WidgetKind.File)", widgetCommands, StringComparison.Ordinal);
        Assert.Contains("ContentWidgetShell.ShowAddButton =", widgetCommands, StringComparison.Ordinal);
        Assert.Contains("actions.Contains(SettingsService.WidgetHoverActionAdd)", widgetCommands, StringComparison.Ordinal);
        Assert.Contains("ShowWidgetCreateFlyout();", widgetCommands, StringComparison.Ordinal);
        Assert.Contains("Common.NewWidget", widgetCommands, StringComparison.Ordinal);
        Assert.Contains("Common.NewFolderMapping", widgetCommands, StringComparison.Ordinal);
        Assert.Contains("Common.AddFeatureWidget", widgetCommands, StringComparison.Ordinal);
        Assert.Contains("CreateFolderWidgetFromPickerAsync", widgetCommands, StringComparison.Ordinal);
        Assert.Contains("CreateWidgetOfKindAsync(entry.Kind)", widgetCommands, StringComparison.Ordinal);
    }

    [Fact]
    public void DropdownOptionCopy_DoesNotUseParentheticalBadges()
    {
        string root = FindRepositoryRoot();
        string stringsDirectory = Path.Combine(root, "src/magnetic-desk/Strings");
        string[] optionKeys =
        [
            "Settings.WidgetLayerMode.DesktopPinned",
            "Settings.CollapseBehavior.Click",
            "Settings.CollapseBehavior.Manual",
            "Settings.Capsule.WidthMode.Aligned",
            "Settings.Capsule.ExpansionDirection.Auto",
            "Settings.Capsule.ExpansionDirection.Down",
            "Settings.Capsule.ExpansionDirection.Up",
            "Settings.CompactContent.Smart",
            "Settings.Capsule.Direction.Auto",
            "Settings.Capsule.Animation.Smooth",
            "Settings.Capsule.HoverResponse.Balanced",
            "Settings.Density.Standard",
            "Settings.Density.Custom",
            "Settings.Animation.Preset.Standard",
            "Settings.Animation.Preset.Custom",
            "Settings.WidgetGroupNavigation.Auto",
            "Settings.Accent.Source.System",
            "Settings.Accent.Source.Custom",
            "Settings.OpenMethod.SingleClick",
            "Settings.OpenMethod.DoubleClick",
            "Settings.ShowDesktopBehavior.KeepVisible",
            "Settings.ShowDesktopBehavior.HideWithWindows",
            "Settings.Weather.LocationMode.Auto",
            "Settings.Weather.LocationMode.Manual",
            "Settings.FileStacks.Status.Manual",
            "Settings.HoverButtonActions.None"
        ];

        foreach (string path in Directory.EnumerateFiles(stringsDirectory, "*.json"))
        {
            using JsonDocument strings = JsonDocument.Parse(File.ReadAllText(path));
            foreach (string optionKey in optionKeys)
            {
                string value = strings.RootElement.GetProperty(optionKey).GetString()!;
                Assert.DoesNotContain('(', value);
                Assert.DoesNotContain('（', value);
            }
        }
    }

    [Fact]
    public void QuickCapturePreviewLineCount_IsBoundInTheSharedSurfaceHost()
    {
        string root = FindRepositoryRoot();
        string sharedSurface = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml"));

        Assert.Contains(
            "MaxLines=\"{Binding ElementName=ItemsList, Path=DataContext.ItemPreviewLineCount}\"",
            sharedSurface,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TextSize}\" MaxLines=\"3\"",
            sharedSurface,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedStorageDesktopShortcutToggleMirrorsFileSystemState()
    {
        string root = FindRepositoryRoot();
        string windowXaml = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsWindow.xaml"));
        string storageCode = File.ReadAllText(Path.Combine(
            root,
            "src/magnetic-desk/Views/SettingsWindow.StorageAndUpdates.cs"));

        Assert.Contains(
            "x:Name=\"ManagedStorageDesktopShortcutToggle\"",
            windowXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Toggled=\"ManagedStorageDesktopShortcutToggle_Toggled\"",
            windowXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IsOn=\"{Binding ManagedStorageDesktopShortcutEnabled",
            windowXaml,
            StringComparison.Ordinal);
        Assert.Contains("_isSynchronizingManagedStorageDesktopShortcutToggle", storageCode, StringComparison.Ordinal);
        Assert.Contains("ManagedStorageDesktopShortcutService.HasShortcut()", storageCode, StringComparison.Ordinal);
        Assert.Contains("shortcutService.CreateAsync()", storageCode, StringComparison.Ordinal);
        Assert.Contains("shortcutService.RemoveAsync()", storageCode, StringComparison.Ordinal);
    }

    private static string SliceSection(string xaml, string startToken, string endToken)
    {
        int start = xaml.IndexOf(startToken, StringComparison.Ordinal);
        int end = xaml.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return xaml[start..end];
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static void AssertInOrder(string source, params string[] values)
    {
        int lastIndex = -1;
        foreach (string value in values)
        {
            int index = source.IndexOf(value, lastIndex + 1, StringComparison.Ordinal);
            Assert.True(index > lastIndex, $"Expected '{value}' after index {lastIndex}.");
            lastIndex = index;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "src",
                    "magnetic-desk",
                    "magnetic-desk.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "MagneticDesk repository root was not found.");
    }
}
