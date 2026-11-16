# magnetic-desk

Windows desktop organizer with file widgets, persistent magnetic groups, compact capsules, notes, clipboard history, tasks, search, and local backup.

English | [简体中文](README.zh-CN.md)

## Download and install

Download `magnetic-desk_Setup_1.0.0_x64.exe` from [GitHub Releases](https://github.com/Rosszhou/magnetic-desk/releases/latest) and run the installer.

- Supports x64 Windows 10 version 2004 (build 19041) or later and Windows 11.
- Includes its runtime components for offline installation.
- Supports English and Simplified Chinese. Uninstall through Settings → About or Windows Installed apps.
- The installer is currently unsigned, so Windows may show an unknown publisher. A matching `.sha256` checksum file is available with the download.

## Features

- Map existing folders or create file widgets backed by ordinary folders.
- Drag a widget near another edge and hold for three seconds to form a magnetic group. Drag a title to move the group. Hold Ctrl to detach and move one member.
- Expand and collapse a magnetic group together. Temporary expansion collapses after the pointer stays outside for five seconds. An arriving widget inherits an existing group's shared collapse mode.
- Choose capsule styles and magnetic animations, with layouts saved across restarts.
- Add notes, clipboard, todo, and search widgets through the title-bar plus menu.
- Use local backup and recovery. The interface supports English and Simplified Chinese. System language selects Chinese for Chinese systems and English otherwise.

This edition is version 1.0.0. Weather, music, Glance, WebDAV backup, and application update entry points have been removed from this edition. Legacy configuration models remain for compatibility.

## Build and verification

Development requires Windows, .NET 10 SDK compatible with [global.json](global.json), and the Windows SDK for target 10.0.22621.0. Building native helpers and running the full test suite additionally requires the pinned Rust MSVC toolchain and Visual C++ build tools.

Build the x64 managed application with a bundled Windows App SDK runtime:

```powershell
dotnet restore src/magnetic-desk/magnetic-desk.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet build src/magnetic-desk/magnetic-desk.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:WindowsAppSDKSelfContained=true -p:MagneticDeskShellThumbnailProxy=false
```

The command above skips building the native thumbnail proxy. See [the local build guide](magnetic-desk.md) and [native sources](native/README.md) for native component requirements.

Run the managed regression subset covering magnetic layout, movement, localization, clipboard, registry, and backup:

```powershell
dotnet test tests/magnetic-desk.ManagedRegression/magnetic-desk.ManagedRegression.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

Historical tests have been migrated to the current feature set. The full Release suite passes 5,261 tests, and the standalone uninstaller passes 9 tests, with no skipped tests. The native test stage requires the Rust toolchain.

```powershell
dotnet test tests/magnetic-desk.Tests/magnetic-desk.Tests.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:WindowsAppSDKSelfContained=true -p:WindowsAppSdkBootstrapInitialize=false
dotnet test tests/magnetic-desk.Uninstaller.Tests/magnetic-desk.Uninstaller.Tests.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

The x64 Native AOT offline package has been verified on Windows 11 for installation, first launch, and uninstallation, including preservation of extra files and configuration. ARM64 releases require separate validation.

## Repository contents

`src` contains the application and updater sources, `native` contains native helper sources, `tests` contains tests and opt-in integration fixtures, and `scripts` contains build and validation tooling. Generated executables, build directories, logs, and local configuration are excluded through `.gitignore`.

## Origin and license

This project is derived from [Tianyu199509/DeskBox v1.5.9](https://github.com/Tianyu199509/DeskBox/tree/v1.5.9). The original copyright and third-party notices are retained. Licensed under GPL-3.0-only, see [LICENSE](LICENSE). Earlier entries in [CHANGELOG.md](CHANGELOG.md) document the upstream history.
