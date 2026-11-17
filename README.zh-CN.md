# magnetic-desk

Windows 桌面整理工具，支持文件格子、磁吸组合、胶囊收纳、便签、剪贴板、待办、搜索和本地备份。

[English](README.md) | 简体中文

## 下载与安装

在 [Releases 下载页](https://github.com/Rosszhou/magnetic-desk/releases/latest) 下载 `magnetic-desk_Setup_1.0.0_x64.exe`，双击运行安装程序。

- 适用于 Windows 10 2004（内部版本 19041）及更新版本、Windows 11 的 x64 系统。
- 安装包自带运行组件，支持离线安装。
- 支持英文和简体中文。可在软件设置的“关于”页面或 Windows“已安装的应用”中卸载。
- 安装包目前未签名，Windows 可能显示未知发布者。下载页提供对应的 `.sha256` 校验文件。

## 主要功能

- 映射现有文件夹，或创建以普通文件夹存储内容的文件格子。
- 拖动格子靠近另一格子的边缘并停留三秒，磁吸成组。拖动标题栏移动整组，按住 Ctrl 拖动可脱离并单独移动成员。
- 磁吸组合联动展开和收起。临时展开后，鼠标连续离开五秒自动收起。新成员加入已有自动收起组合后，继承统一的收起模式。
- 支持多种胶囊外观与磁吸动画，布局保存后可在重启时恢复。
- 标题栏加号菜单提供便签、剪贴板、待办和搜索等功能格子。
- 本地备份与恢复。支持简体中文和英文，跟随系统时中文系统使用简体中文，其余使用英文。

当前版本为 1.0.0。本版本已移除天气、音乐、时光格子、WebDAV 云备份及应用更新入口，保留旧配置模型以读取历史配置。完整使用说明见 [magnetic-desk.md](magnetic-desk.md)。

## 构建和检查

开发环境需要 Windows、与 [global.json](global.json) 匹配的 .NET 10 SDK，以及支持目标版本 10.0.22621.0 的 Windows SDK。原生辅助程序及完整测试还需要仓库指定的 Rust MSVC 工具链和 Visual C++ 构建工具。

构建 x64 托管应用并携带 Windows App SDK 运行时：

```powershell
dotnet restore src/magnetic-desk/magnetic-desk.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet build src/magnetic-desk/magnetic-desk.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:WindowsAppSDKSelfContained=true -p:MagneticDeskShellThumbnailProxy=false
```

上述命令跳过原生缩略图代理的编译。辅助组件构建要求见 [构建说明](magnetic-desk.md) 和 [native](native/README.md)。

运行覆盖磁吸布局、移动、语言、剪贴板、格子注册和备份的托管回归子集：

```powershell
dotnet test tests/magnetic-desk.ManagedRegression/magnetic-desk.ManagedRegression.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

历史测试已按当前功能迁移。完整 Release 套件已通过 5,261 项测试，独立卸载器通过 9 项测试，均无跳过。完整套件的原生阶段需要 Rust 工具链。

```powershell
dotnet test tests/magnetic-desk.Tests/magnetic-desk.Tests.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:WindowsAppSDKSelfContained=true -p:WindowsAppSdkBootstrapInitialize=false
dotnet test tests/magnetic-desk.Uninstaller.Tests/magnetic-desk.Uninstaller.Tests.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

x64 Native AOT 离线安装包已在 Windows 11 上验证安装、首次启动和卸载，并验证额外文件及配置保留。ARM64 发布需要单独验证。

## 目录

`src` 为应用和更新辅助程序源码，`native` 为原生组件源码，`tests` 为测试和按需启用的窗口集成验证，`scripts` 为构建和检查脚本。`.gitignore` 排除编译输出、运行日志与本机配置。

## 许可协议

本项目采用 GPL-3.0-only 协议。版权与修改声明见 [NOTICE](NOTICE)，完整许可条款见 [LICENSE](LICENSE)。
