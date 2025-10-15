# SnapDescribe 截图助手

SnapDescribe 是一款基于 Avalonia 的桌面截图助手，可以在 Windows 上快速获取窗口或任意区域的图像，将截图交给智谱 GLM 多模态模型生成结构化描述，并把结果管理成可追溯的 Markdown 记录。应用支持托盘驻留、全局热键、对话续聊以及基于进程/窗口标题的自动 Prompt 匹配，为知识工作者、客服团队和个人笔记场景带来更高效的截图整理体验。

## Roadmap

1. ✅ 按截图进程自动套用对应 Prompt（已实现，可在设置页维护规则）。
2. 🚧 集成外部命令/工具调用，让截图流程联动更多自动化脚本。
3. 🚧 自动更新机制，内置渠道及时获取新版特性与修复。
4. 🚧 MCP（Model Context Protocol）支持，连接模型上下文与多源数据。
5. 🚧 标准 Agent 模式，实现可扩展的任务链和插件体系。
6. 🚧 对话体验优化，涵盖记忆、草稿和更友好的消息组织。
7. 🚧 AI 助手中心，统一管理常用 Prompt、角色与任务模板。

## 功能特点

- **窗口/区域截图**：支持高亮窗口选择或手动框选，自动识别进程名与窗口标题。
- **多模态对话**：将 PNG 以 Base64 发送到智谱 GLM `chat/completions`，支持对话续聊与上下文记忆。
- **Prompt 规则匹配**：按进程或窗口标题自动切换 Prompt（默认内置常见浏览器、PDF、IM、WPS 规则）。
- **结果预览**：弹窗展示截图、元数据、对话记录，可直接继续追问或复制内容。
- **本地持久化**：截图、Markdown 会话与日志默认保存到用户目录，易于归档。
- **全局热键与托盘**：默认 `Alt+T`，可自定义，同时支持托盘菜单快捷操作。

## 安装与快速体验

### 环境要求

- Windows 10/11（截图与托盘功能依赖 Win32 API）。
- .NET 8 SDK（开发与调试需要）。
- 可用的智谱大模型 API Key。

### 运行步骤

```bash
git clone https://github.com/yushaw/SnapDescribe.git
cd SnapDescribe
dotnet restore
dotnet run --project SnapDescribe.App
```

首次运行请在“设置”页填写 Base URL（默认 `https://open.bigmodel.cn/api/paas/v4/`）、API Key 与模型名称（默认 `glm-4.5v`），随后即可通过按钮或全局热键启动截图流程。

## 发布与升级

本地生成 Windows x64 自包含单文件：

```bash
dotnet publish SnapDescribe.App -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

输出位于 `SnapDescribe.App/bin/Release/net8.0/win-x64/publish/`，其中的 `SnapDescribe.App.exe` 可直接分发。仓库内的 `.github/workflows/release.yml` 会在推送 `v*` 标签后自动构建同样的发布包并创建 GitHub Release。

后续版本将补充自动更新通道，确保应用能在系统内直接检测并安装新版本。

## 默认配置与数据位置

- Prompt 规则与其他设置保存在 `%APPDATA%\SnapDescribe\settings.json`。首次启动会自动写入内置规则：常见 PDF 阅读器映射到 OCR Prompt，Chrome/Edge 使用网页总结，tuitui.exe 与 WeChat 给出聊天回复草案，WPS 表格/文档分别应用数据分析与润色 Prompt。
- 截图与 Markdown 对话默认位于 `我的图片/SnapDescribe`（可在设置中调整）。
- 日志输出路径为 `%APPDATA%\SnapDescribe\logs\`。

## 目录结构

```
SnapDescribe.App/         # Avalonia 客户端项目
├── App.axaml(.cs)       # 应用入口与依赖注入
├── Program.cs           # Main 方法
├── Models/              # 数据模型（设置、截图记录、Prompt 规则等）
├── Services/            # 截图、AI 调用、热键、设置存储、日志
├── ViewModels/          # MVVM ViewModel（主窗口逻辑）
├── Views/               # Avalonia 界面 (MainWindow, RegionSelectionWindow, ResultDialog)
├── Assets/              # 图标等静态资源
└── SnapDescribe.App.csproj

make_icon.cs             # 托盘图标生成脚本
SnapDescribe.sln         # 解决方案文件
```

## 开发提示

- 推荐使用 Rider、Visual Studio 或 VS Code（C# Dev Kit）进行调试，仓库已配置常见 `.gitignore`。
- MVVM 基于 `CommunityToolkit.Mvvm`，命令、通知与部分属性绑定通过源码生成。
- `ScreenshotService` 使用 Win32 API（`user32.dll`）获取窗口与进程信息；如需跨平台支持，请根据平台替换实现。
- `IAiClient` 是模型调用抽象层，当前实现使用 `GlmClient`；可在此基础上扩展自定义模型或第三方服务。
- 结果弹窗（`ResultDialog`）与主界面共享 `CaptureRecord`，流程中注意 UI 线程访问，已有示例可参考。

## 贡献

欢迎提交 Issue 或 Pull Request。建议在提交代码前运行：

```bash
dotnet build SnapDescribe.sln
dotnet publish SnapDescribe.App -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

如果涉及新特性，请同步更新 `README.md` 与 Roadmap。