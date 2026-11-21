; MagneticDesk ARM64 安装脚本
; 零售安装包由 scripts\build-stage-7c1-distribution.ps1 -Platform ARM64 产出（Full Native AOT 载荷）。
; 载荷必须由 ..\scripts\publish-aot-retail.ps1 -Platform ARM64 生成，
; 以便同时生成 magnetic-desk.InstallManifest.txt。手动编译示例：
; ISCC /DMagneticDeskNativeAot=1 /DMagneticDeskBundledRuntime=1 /DMyAppReleaseDir=<publish 目录> magnetic-desk.arm64.iss

#define MyAppName "magnetic-desk"
#define MyAppVersion "1.5.0"
#define MyAppVersionInfo "1.5.0.0"
#define MyAppPublisher "Rosszhou"
#define MyAppExeName "magnetic-desk.exe"
#define MyAppOutputBaseName "magnetic-desk_Setup"
#define MyAppRuntimeArchitecture "ARM64"
#ifndef MyAppPackageSuffix
#define MyAppPackageSuffix ""
#endif
#ifndef MagneticDeskBundledRuntime
#define MagneticDeskBundledRuntime 0
#endif
#ifndef MagneticDeskNativeAot
#define MagneticDeskNativeAot 0
#endif
#ifndef MyAppReleaseDir
#define MyAppReleaseDir "..\artifacts\publish\magnetic-desk\arm64"
#endif

[Setup]
AppId={{229CD379-9742-50B9-9D6F-3D3E9EC0F2C5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
#if MagneticDeskBundledRuntime
AppComments=Native AOT build with private Windows App Runtime components for offline installation.
#elif MagneticDeskNativeAot
AppComments=Native AOT build; Windows App Runtime is installed when missing.
#else
AppComments={cm:RuntimeDependencyComment}
#endif
UninstallDisplayName={#MyAppName} {#MyAppVersion}
UninstallDisplayIcon={app}\Assets\magnetic-desk.ico
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
MinVersion=10.0.19041
DefaultDirName={code:GetDefaultInstallDir}
DisableProgramGroupPage=yes
DisableDirPage=no
; New installs recommend the machine-wide Program Files location while still
; allowing an unelevated current-user install. Registered upgrades retain their
; previous install mode and path.
PrivilegesRequired=admin
; "commandline" lets MagneticDesk.Updater pin the install scope with
; /CURRENTUSER or /ALLUSERS so its post-install consistency check cannot
; disagree with the scope the installer actually used.
PrivilegesRequiredOverridesAllowed=dialog commandline
; Upgrade paths are resolved and locked by magnetic-desk.Installation.iss before the
; directory page is shown.
; Directory reuse is handled exclusively by magnetic-desk.Installation.iss. Leaving
; Inno's previous-directory fallback enabled could resurrect a stale uninstall
; record after the detector intentionally classified the run as a first install.
UsePreviousAppDir=no
UsePreviousPrivileges=yes
CloseApplications=force
; The short-lived thumbnail proxy batches can outlive a killed host when a
; shell handler hangs, so Restart Manager must cover it as well.
CloseApplicationsFilter={#MyAppExeName},magnetic-desk.ThumbnailProxy.exe
RestartApplications=no
OutputDir=..\Output
#if MagneticDeskBundledRuntime
OutputBaseFilename={#MyAppOutputBaseName}_{#MyAppVersion}_arm64
#else
OutputBaseFilename={#MyAppOutputBaseName}_{#MyAppVersion}_arm64{#MyAppPackageSuffix}
#endif
SetupIconFile=..\src\magnetic-desk\Assets\magnetic-desk.ico
VersionInfoVersion={#MyAppVersionInfo}
VersionInfoProductVersion={#MyAppVersionInfo}
VersionInfoTextVersion={#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; Keep this language list identical to magnetic-desk.iss: silent in-app updates
; pin /LANG from AppUpdateService.MapAppCultureToInstallerLanguage, whose
; names must match the [Languages] entries here.
ShowLanguageDialog=yes

[Languages]
Name: "english"; MessagesFile: "Languages\English.isl"
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[Messages]
#include "magnetic-desk.HindiBengaliMessages.iss"

[CustomMessages]
chinesesimplified.MultipleInstallationsTitle=检测到多个 magnetic-desk 安装
chinesesimplified.MultipleInstallationsBody=检测到以下多个 magnetic-desk 程序目录：%n%n%1
chinesesimplified.MultipleInstallationsFooter=为避免升级后产生新的 magnetic-desk，请先在系统设置中卸载多余版本，然后重新运行安装程序。
chinesesimplified.UpgradeDirectoryMismatch=检测到这是升级操作，但安装目录与已安装版本不一致。%n%n原安装目录：%1%n本次指定目录：%2%n%n升级必须使用原安装目录，请取消本次安装后重新运行。
english.MultipleInstallationsTitle=Multiple magnetic-desk installations detected
english.MultipleInstallationsBody=The following magnetic-desk installation directories were found:%n%n%1
english.MultipleInstallationsFooter=To prevent another copy from being created, uninstall the extra version from Windows Settings, then run Setup again.
english.UpgradeDirectoryMismatch=This is an upgrade, but the requested installation directory does not match the existing installation.%n%nExisting directory: %1%nRequested directory: %2%n%nAn upgrade must use the existing directory. Cancel Setup and run it again.
chinesesimplified.ConfirmStorageTitle=检测到 magnetic-desk 收纳目录中仍有内容
chinesesimplified.ConfirmStorageBody=当前包含 %1 个文件夹、%2 个文件。
chinesesimplified.ConfirmStorageFooter=卸载 magnetic-desk 不会删除这个目录，也不会删除里面的用户文件。%n请确认你已经知道这些文件的位置。是否继续卸载？
chinesesimplified.AppDataChoiceTitle=选择卸载后保留的内容
chinesesimplified.ConfirmRemoveAppData=magnetic-desk 应用数据位于：%n%1%n%n其中包含设置、格子布局、待办、随记、缓存、日志、更新文件和恢复快照。%n%n收纳路径中的文件不会被删除。
chinesesimplified.KeepAppDataButton=保留应用数据%n保留设置和内容，重新安装后可继续使用
chinesesimplified.RemoveAppDataButton=彻底删除应用数据%n永久删除以上应用数据和恢复快照
chinesesimplified.AppDataCleanupFailed=部分应用数据未能删除，请手动检查：%n%1
chinesesimplified.FolderItem=[文件夹]
chinesesimplified.FileItem=[文件]
chinesesimplified.MoreItems=...还有 %1 项未显示
chinesesimplified.DependencyDownloadTitle=正在准备 magnetic-desk 运行环境
chinesesimplified.DependencyDownloadSubtitle=正在下载缺少的运行时依赖。
chinesesimplified.DependencyInstallTitle=正在准备 magnetic-desk 运行环境
chinesesimplified.DependencyInstallSubtitle=正在安装缺少的运行时依赖。
chinesesimplified.DownloadingDotNet=正在下载 .NET 10 Runtime ARM64...
chinesesimplified.DownloadingWinAppRuntime=正在下载 Windows App Runtime 2.5 ARM64...
chinesesimplified.InstallingDependency=正在安装 %1...%n这可能需要几分钟，请勿关闭此窗口。
chinesesimplified.NeedsRestart=运行时依赖已安装，但 Windows 需要重启。请重启电脑后重新运行 magnetic-desk 安装程序。
chinesesimplified.DependencyVerificationFailed=依赖安装完成后，系统仍未识别到所需运行环境。magnetic-desk 尚未安装。请先安装稳定版 .NET 10 Runtime 和 Windows App Runtime 2.5，然后重新运行安装程序。
english.ConfirmStorageTitle=magnetic-desk storage folder still contains files
english.ConfirmStorageBody=It currently has %1 folder(s) and %2 file(s).
english.ConfirmStorageFooter=Uninstalling magnetic-desk will not delete this folder or any user files inside it.%nPlease confirm you know where these files are. Continue uninstalling?
english.AppDataChoiceTitle=Choose what to keep after uninstalling
english.ConfirmRemoveAppData=magnetic-desk application data is stored in:%n%1%n%nIt includes settings, widget layouts, todos, quick notes, caches, logs, update files, and recovery snapshots.%n%nFiles in the managed storage path will not be deleted.
english.KeepAppDataButton=Keep application data%nKeep settings and content for a future reinstall
english.RemoveAppDataButton=Permanently delete application data%nDelete the application data and recovery snapshots listed above
english.AppDataCleanupFailed=Some application data could not be removed. Check these locations manually:%n%1
english.FolderItem=[Folder]
english.FileItem=[File]
english.MoreItems=...and %1 more items not shown
english.DependencyDownloadTitle=Preparing magnetic-desk runtime
english.DependencyDownloadSubtitle=Downloading missing runtime dependencies.
english.DependencyInstallTitle=Preparing magnetic-desk runtime
english.DependencyInstallSubtitle=Installing missing runtime dependencies.
english.DownloadingDotNet=Downloading .NET 10 Runtime ARM64...
english.DownloadingWinAppRuntime=Downloading Windows App Runtime 2.5 ARM64...
english.InstallingDependency=Installing %1...%nThis may take a few minutes. Please do not close this window.
english.NeedsRestart=Runtime dependencies were installed, but Windows needs to restart. Restart your PC, then run magnetic-desk setup again.
english.DependencyVerificationFailed=The required runtime was still not detected after dependency installation. magnetic-desk has not been installed. Install the stable .NET 10 Runtime and Windows App Runtime 2.5, then run setup again.

#include "magnetic-desk.NewLanguageCustomMessages.iss"
#include "magnetic-desk.UninstallCustomMessages.iss"
#include "magnetic-desk.DependencyCustomMessages.iss"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
Type: files; Name: "{autodesktop}\{#MyAppName}.lnk"; Tasks: desktopicon
Type: files; Name: "{autostartup}\{#MyAppName}.lnk"

[Files]
#if MagneticDeskBundledRuntime
Source: "..\scripts\cleanup-magnetic-desk-install.ps1"; Flags: dontcopy
Source: "magnetic-desk.LegacyBundledRuntimeFiles.txt"; Flags: dontcopy
Source: "{#MyAppReleaseDir}\magnetic-desk.InstallManifest.txt"; DestDir: "{tmp}"; DestName: "MagneticDesk.InstallManifest.current.txt"; Flags: ignoreversion deleteafterinstall
Source: "{#MyAppReleaseDir}\*"; DestDir: "{app}"; Excludes: "magnetic-desk.Updater.*,magnetic_desk_native.dll,magnetic_desk_native.pdb,magnetic-desk.InstallManifest.txt"; Flags: ignoreversion recursesubdirs createallsubdirs
#if MagneticDeskNativeAot
Source: "{#MyAppReleaseDir}\magnetic_desk_native.dll"; DestDir: "{app}"; Flags: ignoreversion
#endif
Source: "{#MyAppReleaseDir}\magnetic-desk.InstallManifest.txt"; DestDir: "{app}"; Flags: ignoreversion; BeforeInstall: CleanupMagneticDeskInstall
#else
Source: "{#MyAppReleaseDir}\*"; DestDir: "{app}"; Excludes: "magnetic-desk.Updater.*,magnetic_desk_native.dll,magnetic_desk_native.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
#if MagneticDeskNativeAot
Source: "{#MyAppReleaseDir}\magnetic_desk_native.dll"; DestDir: "{app}"; Flags: ignoreversion
#endif
#endif

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\magnetic-desk.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\magnetic-desk.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

#include "magnetic-desk.Installation.iss"
#include "magnetic-desk.Migration.iss"
#include "magnetic-desk.Dependencies.arm64.iss"
#include "magnetic-desk.Uninstall.iss"

[Registry]
; HKA maps installation metadata to HKLM for all-users installs and HKCU for
; current-user installs. The per-user language preference is handled below.
Root: HKA; Subkey: "Software\magnetic-desk\DirectInstall"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\magnetic-desk\DirectInstall"; ValueType: string; ValueName: "InstallVersion"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKA; Subkey: "Software\magnetic-desk\DirectInstall"; ValueType: string; ValueName: "InstallScope"; ValueData: "{code:GetInstallScopeName}"; Flags: uninsdeletevalue uninsdeletekeyifempty

[Code]
function InstallLanguageCode(Value: string): string;
begin
  if ActiveLanguage = 'chinesesimplified' then Result := 'zh-CN'
  else Result := 'en-US';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Install language is a per-user preference. Never write it while running in
  // administrative install mode because HKCU may belong to an elevation
  // account rather than the person who launched Setup.
  if (CurStep = ssPostInstall) and (not IsAdminInstallMode) then
    RegWriteStringValue(
      HKEY_CURRENT_USER,
      'Software\MagneticDesk',
      'InstallLanguage',
      InstallLanguageCode(''));
end;
