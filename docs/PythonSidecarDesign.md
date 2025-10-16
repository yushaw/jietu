# Python Sidecar Design Brief

本设计针对 SnapDescribe 计划中的 Python LangGraph sidecar，总结关键架构约束与交付步骤。通信细节（请求/事件/错误码）将单独记录于 `docs/SidecarProtocol.md`。

## 目标与职责
- 保持 Avalonia/.NET Host 负责截图、OCR、设置、本地化、日志与所有 shell 工具执行。
- 通过 sidecar 托管 LangGraph/Autogen 工作流，提供更复杂的推理、工具编排与未来的 MCP/远程执行入口。
- sidecar 必须可独立发布、按需安装，并允许其他桌面/服务端应用通过同一协议复用。

## 部署与发现
- sidecar 作为独立 Release：产出 `snapdescribe-sidecar-win64.zip`（内含嵌入式 CPython、LangGraph 依赖、服务入口）与 `sidecar-manifest.json`（版本、SHA256、最低兼容 Host 版本）。
- Host 默认安装路径：`%LOCALAPPDATA%\SnapDescribe\sidecar\python\<version>\sidecar.exe`。设置页允许自定义路径或端点。
- Host 启动/调用前先访问 `http://127.0.0.1:<port>/status`，验证服务可用、协议版本匹配、返回 agent catalog etag 等信息。
- 升级流程：Host 从 Manifest 获取最新版本 → 下载校验 → 解压 → 更新 `sidecar-info.json` 并提示用户；支持回滚至旧版本。

## 运行时交互概览
```
Host (SnapDescribe.App)        Sidecar HTTP 服务              Electron 控制面板
─────────────────────────     ───────────────────────────     ───────────────────────
1. 启动/探活 → GET /status  ───────▶                           （展示状态）
2. 若未运行，Host 调用 sidecar.exe --serve （Electron 启动 Python runtime）
3. Host 拉取模板 → GET /agents ─────▶
4. 用户触发 Agent：Host 组装 StartRun payload
5. POST /runs ─────────────────────▶  FastAPI LangGraph 执行
6. GET /runs/{id}/events ◀──────────  SSE 推送事件
7. ToolCall 事件 ←──────────────────  Electron 监听并可视化
8. Host 执行工具 → ShellAgentToolRunner
9. POST /runs/{id}/tools/{callId} ──▶
10. Sidecar 推送 run.completed/failed
11. Host 写入记录、更新 UI；Electron 同步展示
```

核心流程细节：
1. Host 组装 `StartRun`（会话上下文、截图引用、工具白名单、运行约束、locale、Auth token）。
2. `POST /runs` 创建 run，sidecar 通过 SSE (`GET /runs/{id}/events`) 推送 `run.started`、`run.thought`、`run.tool_call`、`run.completed` 等事件。
3. Host 对 `run.tool_call` 使用现有 `ShellAgentToolRunner` 执行命令，随后 `POST /runs/{id}/tools/{callId}` 回传 stdout/stderr/exitCode。
4. Electron 控制面板可通过本地 IPC 订阅 Python 事件流，实时显示运行轨迹与日志。
5. 用户在 Host 侧取消时 `POST /runs/{id}/cancel`；sidecar 需发送 `run.cancelled` 并终止所有待执行步骤，同时通知 Electron UI。
6. 任一环节失败时返回 `run.failed`，Host 记录日志并提示，必要时回退至内建 `AgentExecutionService`。

## Agent 目录
- sidecar 暴露 `GET /agents?locale=xx` 返回只读模板（id、名称/描述多语言字段、版本、标签、参数定义、工具依赖）。
- Host 缓存模板（通过 `etag` 判断更新），支持用户克隆为本地 `AgentProfile` 并自定义参数/Prompt。
- 模板更新与兼容性策略需在 `agents.md` 与 `SidecarProtocol.md` 同步说明。

## 配置与设置扩展
- `AppSettings.Agent.Sidecar` 新增：`Enabled`、`Path`、`Version`、`ManifestUrl`、`CustomEndpoint`、`AuthToken`。
- 设置页提供：检测状态、安装/更新按钮、自定义端口、连接测试、降级切换。
- `AgentProfile` 增加 `Source`（Local/UserClone/SidecarTemplate）与 `TemplateId`，以跟踪模板来源和本地覆写范围。

## Host 端工作项
1. 封装 `ISidecarClient`（健康检查、Agent 目录缓存、Run 生命周期、SSE 解析、工具回调）。
2. 扩展 `AgentExecutionService` 管道：优先尝试 sidecar，失败时回落本地实现。
3. UI 调整：显示 sidecar 状态与实时事件日志；当运行在 sidecar 模式下标记响应来源。
4. 记录安装/运行/错误事件到 `DiagnosticLogger`，并在 markdown/transcript 中保留必要元数据。
5. 编写集成测试（使用 Fake sidecar 或 mock SSE 流）覆盖成功/失败/取消场景。

## Sidecar 端工作项
1. 基于 FastAPI/Uvicorn（或等价框架）实现 HTTP + SSE 服务：`/status`、`/agents`、`/runs`、`/runs/{id}/events`、`/runs/{id}/tools/{callId}`、`/runs/{id}/cancel`。
2. 集成 LangGraph：根据 `agentId` 加载对应图谱，使用 Host 传入的工具白名单与参数启动执行。
3. 事件流/日志：保证事件有序、包含 `runId`、`toolCallId`、时间戳；stdout/stderr 中禁止泄露敏感 token。
4. 鉴权：首个握手由 Host 下注 Auth Token；所有请求需携带 `Authorization: Bearer <token>`。
5. 测试：pytest 覆盖 run 生命周期、工具回调、取消、异常路径；提供 `--mock` CLI 选项输出固定响应，以便 Host 自动化。

## 安全与合规
- sidecar 不得自行启动 shell/脚本；所有命令执行必须经 Host 通知。
- 严格遵守 Host 提供的离线模式/网络限制；必要时拒绝访问外部 API。
- 日志脱敏：屏蔽路径、token、截图内容；提供 trace id 方便 Host/sidecar 交叉排查。

## 后续文档交付
- `docs/SidecarProtocol.md`：记录请求/响应 schema、事件类型、错误码、超时策略。
- `docs/agents.md`：持续同步高层策略与 Roadmap。
- sidecar 仓库 README：面向纯 sidecar 消费者说明安装、启动、协议与示例。
