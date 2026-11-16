# magnetic-desk

Windows 桌面整理工具，支持文件格子、磁吸组合、胶囊收纳、便签、剪贴板、待办、搜索和本地备份。

[English](README.md) | 简体中文

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

继承的完整套件仍包含天气、音乐和时光模块等已移除类型的测试，需要迁移后才能通过完整测试及现有 CI。完整套件的原生阶段还需要 Rust 工具链。完整测试入口为：

```powershell
dotnet test tests/magnetic-desk.Tests/magnetic-desk.Tests.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

本次检查的执行结果与限制随源码快照一并交付。ARM64 和 Native AOT 发布需单独验证。

## 目录

`src` 为应用和更新辅助程序源码，`native` 为原生组件源码，`tests` 为测试和按需启用的窗口集成验证，`scripts` 为构建和检查脚本。`.gitignore` 排除编译输出、运行日志与本机配置。

## 来源与协议

本项目基于 [Tianyu199509/DeskBox v1.5.9](https://github.com/Tianyu199509/DeskBox/tree/v1.5.9) 修改，保留上游版权和第三方声明，采用 GPL-3.0-only 协议，详见 [LICENSE](LICENSE)。[CHANGELOG.md](CHANGELOG.md) 中较早的条目记录上游历史。

当前源码正在准备上传新仓库，magnetic-desk 的下载入口将在首个版本发布后补充。
