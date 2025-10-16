# SnapDescribe Agents Guide

本文记录 SnapDescribe 智能体体系的目标、约束与交付准则。任何新增的自动助手、sidecar 扩展或外部工具链，都应以此为唯一基线。

## Vision

Agents 的核心使命是让截图 → Prompt → 响应 的主流程具备可插拔、可观测的扩展能力：

- 可配置的本地工具链与自动化步骤
- 基于 LangGraph / Autogen 的多轮推理（通过 Python sidecar 托管）
- MCP 与远程执行桥接
- 更细粒度的会话优化与模板管理
- AI Assistant Hub 聚合模板、Persona 与自动化流程

## Guiding Principles

1. **Deterministic behavior first.** 行为必须可复现、可审计，再引入更高自治。
2. **Configurable, not hard-coded.** 用户可按流程或单次 Prompt 定制/禁用智能体逻辑。
3. **Composable services.** 复用 SettingsService、ScreenshotService、LocalizationService 等现有服务。
4. **Non-blocking UI.** 长耗时任务必须离开 UI 线程，进度经 ViewModel 上报。
5. **Localized messaging.** 新增字符串全部走 LocalizationService 资源。
6. **Bilingual UX parity.** UI 需要在 zh-CN 与 en-US 场景下保持布局与文案均衡。
7. **Incremental delivery.** 每个功能切片以独立 commit 收尾并回溯需求。
8. **Test-first mindset.** 智能体相关核心逻辑（规则、IO、重试）必须有自动化覆盖，集成前跑通 `dotnet test`.

## Architecture Checklist

交付新能力前确认：

- [ ] `Services/Agents/` 中有清晰接口定义（如 `IAgent`, `IAgentContext`）。
- [ ] `AppSettings` 覆盖持久化配置并提供 zh-CN/en-US 默认值。
- [ ] 通过依赖注入接入 `MainWindowViewModel`，避免静态依赖。
- [ ] 使用 `DiagnosticLogger` 记录关键节点。
- [ ] 本地化资源字典同步更新。
- [ ] 新功能具备单元/集成测试或调试脚手架，并验证全量测试。

## Current Implementation（2025 Q4）

- **管道**：`CapabilityIds.Agent` 指向 `AgentExecutionService`，负责补全 system/user 消息 → 按顺序执行 AutoRun 工具（`ShellAgentToolRunner`）→ 将工具结果写入对话 → 调用 GLM → 持久化 Markdown 与日志。
- **配置**：`AppSettings.Agent` 保存全局开关、默认 Profile、自定义 Profile 与工具信息，全部写入 `%APPDATA%/SnapDescribe/settings.json`。
- **UI**：*Settings → 智能体编排* 提供 Profile/Tool 编辑；*Settings → Trigger Rules* 绑定 `CapabilityIds.Agent` 与具体 Profile；ResultDialog 以 `tool`/`assistant` 消息展示执行轨迹。
- **测试**：`AgentExecutionServiceTests` 覆盖消息顺序与工具输出选项；UI/集成流程暂以手测为主。
- **限制**：仅支持本机 CLI 工具；未实现 MCP、远程执行、多 Agent 协调与复杂重试。

## Sidecar Integration（规划中）

- **职责划分**：Host（SnapDescribe.App）继续负责截图、OCR、工具执行、安全与 UI；Python sidecar 专注 LangGraph/Autogen 编排。
- **发现与健康检查**：Host 通过本地 HTTP `/status` 识别 sidecar 是否安装、版本是否兼容，并提示安装/升级。
- **Agent 目录**：sidecar 提供 `GET /agents` 返回模板（多语言名称、描述、参数定义、工具依赖等），Host 映射为只读模板，支持用户克隆后本地调整。
- **运行闭环**：Host 以 `POST /runs` 发送对话上下文、截图引用、工具清单；sidecar 通过 SSE 推送思考、工具调用、完成事件；工具执行仍由 Host 的 `ShellAgentToolRunner` 完成并经 `POST /runs/{id}/tools/{callId}` 回传。
- **配置扩展**：`AppSettings.Agent.Sidecar` 将新增路径、版本、Manifest、AuthToken 等字段；设置页提供安装/更新、自定义端口、状态展示。
- **协议文档**：通信负载、事件类型、错误码等细节独立收敛于 `docs/SidecarProtocol.md`（由 sidecar 项目与 Host 共用）。
- **降级策略**：sidecar 未安装或通信失败时自动回落至本地 `AgentExecutionService`。

## Configuration Walkthrough（现状 + 即将扩展）

1. 打开 *Settings → 智能体编排*，启用“智能体编排”。
2. 按需新建/复制智能体，配置 system prompt、工具列表与运行策略。
3. 在 *Settings → Trigger Rules* 选择 `Autogen Agent Pipeline` 并指定目标 Profile（sidecar 模板可先克隆后使用）。
4. 捕捉截图时若匹配规则，主流程按 Profile 指示运行；后续聊天复用相同管道。
5. 安装 sidecar 后，可在设置页检测状态并选择使用 sidecar 模板或自定义流程。

## Python Sidecar Expansion（摘要）

- **交付形态**：独立仓库发布，产出 `snapdescribe-sidecar-win64.zip` + `sidecar-manifest.json`。Host 下载、校验 SHA256 后解压至 `%LOCALAPPDATA%\SnapDescribe\sidecar\python\<version>\`。
- **通信原则**：本地 HTTP + SSE；所有请求需携带 Host 生成的 Bearer Token；sidecar 不直接执行 shell，工具调用全部通过 Host 白名单执行器。
- **生命周期**：Host 按需拉起 sidecar 进程、监听日志并在应用退出或用户取消时发送 `CancelRun`；故障时提供回退提示。
- **测试与调试**：sidecar 带 pytest 覆盖（workflow、工具回调、取消）；Host 引入 `ISidecarClient` 抽象并提供 Fake client；支持设置自定义端点连接开发版 sidecar。
- **演进路线**：后续 MCP、LangGraph Workflow Designer、本地工具注册、Multi-Agent 调度均基于统一协议推进。任何变更需同步更新 `agents.md`、`docs/PythonSidecarDesign.md` 与 `docs/SidecarProtocol.md`。

## Roadmap（对齐）

1. Process-aware prompt overrides ✅
2. External tool hooks ✅（基础版）
3. Python sidecar & LangGraph orchestration ⏳
4. MCP bridge ⏳
5. 标准化 Agent 合同与模板共享 ⏳
6. Conversation 体验升级 ⏳
7. AI Assistant Hub ⏳

保持本文档随实现演化持续更新。
