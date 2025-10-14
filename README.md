# SnapDescribe 截图助手

SnapDescribe 是一款基于 Avalonia 的桌面工具，帮助你迅速捕捉屏幕、调用智谱 GLM 多模态模型生成描述，并把结果保存成 Markdown 记录。应用支持托盘运行、全局快捷键、窗口/区域自由截取以及对话续聊，非常适合知识工作者和客服团队快速整理截图说明。

## 功能亮点

- **跨应用截图**：支持高亮窗口选择或框选任意屏幕区域。
- **多模态描述**：将 PNG 图像内联为 Base64，调用 GLM `chat/completions` 接口返回 Markdown 描述。
- **对话追问**：在结果弹窗中继续向模型提问，历史对话自动持久化为 Markdown。
- **全局快捷键**：默认 `Alt+T`，可在设置页自定义并自动写入配置。
- **后台托盘**：支持最小化到托盘，直接通过托盘菜单唤起、截图或退出。
- **持久化存档**：截图、Markdown 及日志保存到本地，可随时查阅。

## 系统要求

- Windows 10/11（目前仅 Windows 平台提供截图与托盘集成）。
- .NET 8 SDK。
- 智谱大模型账号及可用的 API Key。

## 快速开始

```bash
git clone https://github.com/<your-username>/SnapDescribe.git
cd SnapDescribe
dotnet restore
dotnet run --project SnapDescribe.App
```

首启后请在“设置”页填写：

1. Base URL（默认 `https://open.bigmodel.cn/api/paas/v4/`）。
2. API Key。
3. 模型名称（默认 `glm-4.5v`）。

保存后即可通过“捕捉屏幕”按钮或全局快捷键执行截图，结果会弹出对话窗口并自动复制截图到剪贴板。

## 发布与分发

生成 Windows x64 独立发行文件：

```bash
dotnet publish SnapDescribe.App -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

构建输出位于 `SnapDescribe.App/bin/Release/net8.0/win-x64/publish`。你可以将整个目录打包发布，或借助 WiX/NSIS 制作安装包。

> 在仓库打上 `v*` 标签并推送后，GitHub Actions (`.github/workflows/release.yml`) 会自动构建上述包、创建 Release 并上传 `SnapDescribe-win-x64.zip` 资产。

## 配置与数据存储

- 用户设置：`%APPDATA%\SnapDescribe\settings.json`
- 日志文件：`%APPDATA%\SnapDescribe\logs\snap-YYYYMMDD.log`
- 截图与 Markdown：默认 `我的图片/SnapDescribe`，可在设置页调整输出目录。

## 目录结构

```
SnapDescribe.App/         # Avalonia 客户端项目
├── App.axaml(.cs)       # 应用入口与 DI 配置
├── Program.cs           # Main 方法
├── Models/              # 配置、热键、截图记录等数据模型
├── Services/            # 截图、AI 调用、全局热键、日志等服务
├── ViewModels/          # MVVM ViewModel（主窗口）
├── Views/               # Avalonia 界面 (MainWindow, RegionSelectionWindow, ResultDialog)
├── Assets/              # 应用图标等静态资源
└── SnapDescribe.App.csproj

make_icon.cs             # 生成托盘图标的辅助脚本
SnapDescribe.sln         # 解决方案文件
```

## 开发指南

- 推荐使用 Rider、Visual Studio 或 VS Code (C# Dev Kit) 作为 IDE，`.gitignore` 已排除常见缓存。
- 项目使用 `CommunityToolkit.Mvvm` 简化命令与通知，设计阶段的绑定通过 `AppBuilder.Configure().UsePlatformDetect()` 自动处理。
- 截图服务依赖 Win32 API (`user32.dll`)，如需跨平台支持可替换为平台特定实现。
- `GlmClient` 目前仅封装 `chat/completions` 接口，若要支持其他模型/厂商，可通过实现 `IAiClient` 拓展。

## 截图示例

建议在 `docs/images/` 下补充应用截图，并在此处以 `![主界面](docs/images/main.png)` 方式引用，帮助 GitHub 访客快速了解界面。

## TODO / Roadmap（可选）

- [ ] 支持多语言界面。
- [ ] 自定义 Prompt 模板库。
- [ ] 导出到 Evernote / OneNote 等第三方平台。
- [ ] 跨平台截图实现（macOS / Linux）。

## 许可协议

选择合适的开源许可证并将文本放入 `LICENSE` 文件，例如 MIT、Apache-2.0 或 GPLv3。

---

> 发布前记得在 GitHub 上添加项目封面图片、设置标签，并开启“Releases”供用户下载二进制包。
