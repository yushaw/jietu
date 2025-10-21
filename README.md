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

- **窗口/区域截图**：支持高亮窗口选择或手动框选，自动识别进程名与窗口标题。智能窗口置顶机制确保单窗口捕获时目标窗口不被遮挡（v1.0.3+）。
- **多模态对话**：将 PNG 以 Base64 发送到智谱 GLM `chat/completions`，支持对话续聊与上下文记忆。
- **Prompt 规则匹配**：按进程或窗口标题自动切换 Prompt（默认内置常见浏览器、PDF、IM、WPS 规则）。
- **结果预览**：弹窗展示截图、元数据、对话记录，可直接继续追问或复制内容。
- **本地 OCR**：在规则中选择 OCR 能力，调用 Tesseract 识别截图文本，支持多语言分段展示与复制。使用 tessdata_best 高质量训练数据与 Default 引擎模式（v1.0.2+）。
- **Autogen 智能体（预览）**：基于 Autogen 流程调度模型与外部工具，支持为特定规则配置多步骤处理（命令行工具→模型总结）。
- **本地持久化**：截图、Markdown 会话与日志默认保存到用户目录，易于归档。
- **全局热键与托盘**：默认 `Alt+T`，可自定义，同时支持托盘菜单快捷操作。
- **多语言界面**：内置中文/英文，首次启动自动匹配系统语言，可在设置页一键切换。
- **主窗口自动隐藏**：截图时自动最小化主界面，完成后恢复，避免遮挡截图区域。

## 安装与快速体验

### 环境要求

- Windows 10/11（截图与托盘功能依赖 Win32 API）。
- .NET 8 SDK（开发与调试需要）。
- 可用的智谱大模型 API Key。

### 运行步骤

1. **直接安装**（推荐）：前往 GitHub Release 下载 `SnapDescribeSetup.exe`，双击按照向导完成安装（自动包含 OCR 所需的 `tessdata` 语言包）。  
2. **源码运行**：
   ```bash
   git clone https://github.com/yushaw/SnapDescribe.git
   cd SnapDescribe
   dotnet restore
   dotnet run --project SnapDescribe.App
   ```

首次运行请在“设置”页填写 Base URL（默认 `https://open.bigmodel.cn/api/paas/v4/`）、API Key 与模型名称（默认 `glm-4.5v`），随后即可通过按钮或全局热键启动截图流程。应用会根据系统语言自动选择中文或英文界面，也可随时在设置页切换。

> 提示：命令行执行 `SnapDescribe.exe --shutdown` 可以通知正在运行的实例退出，安装包升级时会自动使用这一机制，避免“文件被占用”报错。

### 启用 Autogen 智能体（预览）

1. 在主界面打开 **设置 → 智能体编排** 标签页：
   - 左侧可以新增/复制多个智能体；每个智能体都拥有独立的提示词与工具组合。
   - 勾选 “启用智能体编排” 后，可在右侧为所选智能体配置系统提示词，以及基于命令行/脚本的工具链（参数模板支持 `{prompt}`、`{message}`、`{imagePath}`、`{timestamp}` 等占位符）。
   - 点击 “保存” 写入配置，当前选中的智能体会作为默认选项。
2. 在 **设置 → 触发规则** 里，将目标规则的能力切换为 “Autogen Agent Pipeline”，并在下拉框中选择要使用的智能体。
3. 下次命中该规则时会自动串联：
   1. 自动运行被激活的工具，记录标准输出/错误；
   2. 将工具结果附加到对话；
   3. 调用智谱 GLM 生成最终回答，并在结果窗口展示完整流水线日志。

> 当前为预览版：仅支持同机命令行工具，MCP/多 Agent 编排仍在规划中。若需要禁用，只需在“智能体编排”页取消勾选即可。

## 发布与升级

本地生成 Windows x64 自包含单文件：

```bash
dotnet publish SnapDescribe.App -c Release -r win-x64 --self-contained true \
  /p:PublishSingleFile=true \
  /p:InstallVersion=<版本号>
makensis -DSourceDir=$(pwd)/SnapDescribe.App/bin/Release/net8.0/win-x64/publish \
  -DAppIcon=$(pwd)/SnapDescribe.App/Assets/AppIcon.ico \
  -DInstallVersion=<版本号> \
  installer/SnapDescribeInstaller.nsi
```

发布目录内会生成 `SnapDescribe.exe`、`tessdata/` 语言包以及完整安装包 `SnapDescribeSetup.exe`。推送 `v*` 标签后，GitHub Actions 会自动构建 NSIS 安装器并将 `SnapDescribeSetup.exe` 发布到 Release。

后续版本将补充自动更新通道，确保应用能在系统内直接检测并安装新版本。

## 默认配置与数据位置

- Prompt 规则与其他设置保存在 `%APPDATA%\SnapDescribe\settings.json`。首次启动会自动写入内置规则：常见 PDF 阅读器映射到 OCR Prompt，Chrome/Edge 使用网页总结，tuitui.exe 与 WeChat 给出聊天回复草案，WPS 表格/文档分别应用数据分析与润色 Prompt。
- OCR 能力依赖本地 Tesseract。应用已内置 `eng` 与 `chi_sim` 高质量训练数据（tessdata_best），默认即可使用；若需其他语言，可将对应的 `.traineddata` 文件放入 `tessdata` 目录（或在设置中指定自定义路径），并在规则参数里通过 `language` 键覆盖语言组合（例如 `eng+deu`）。OCR 引擎使用 Default 模式（LSTM + Legacy），在速度与准确率间取得最佳平衡（v1.0.2+）。
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

## 版本历史

### v1.0.3 (2025-10-21)
- 修复：单窗口捕获时智能窗口置顶机制，确保目标窗口不被其他窗口遮挡
- 优化：增加窗口置顶延迟从 50ms 到 300ms，提升渲染稳定性
- 优化：PrintWindow 失败时自动重新拍摄全屏并裁剪，支持高权限应用捕获

### v1.0.2 (2025-10-16)
- 升级：OCR 训练数据从 tessdata 升级到 tessdata_best（eng: 15MB, chi_sim: 12MB）
- 升级：Tesseract 引擎模式从 LstmOnly 升级到 Default（LSTM + Legacy 混合）
- 优化：提升 OCR 识别准确率，尤其是复杂文档场景

### v1.0.1 (Earlier)
- 初始版本功能实现

## 贡献

欢迎提交 Issue 或 Pull Request。建议在提交代码前运行：

```bash
dotnet build SnapDescribe.sln
dotnet publish SnapDescribe.App -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

如果涉及新特性，请同步更新 `README.md` 与 Roadmap。
