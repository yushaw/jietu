# SnapDescribe Agents Guide

This document outlines the design goals and conventions for future agent-related work within SnapDescribe. It should serve as the single source of truth for extending the application with automated assistants, MCP integrations, or external toolchains.

## Vision

Agents augments the core screenshot→prompt→response workflow by orchestrating additional reasoning steps, memory, and actions. Upcoming milestones include:

- External tool invocation (CLI, scripts, or APIs)
- Model Context Protocol (MCP) bridges
- Standardized agent interfaces exposed to the UI
- Conversation optimization and richer UX
- Dedicated AI Assistant hub that aggregates templates, personas, and automation flows

## Guiding Principles

1. **Deterministic behavior first.** Agent actions must be reproducible and observable before introducing autonomy.
2. **Configurable, not hard-coded.** End-users should be able to enable/disable or override agent logic per prompt or per process rule.
3. **Composable services.** Reuse existing services (SettingsService, ScreenshotService, LocalizationService) instead of duplicating logic.
4. **Non-blocking UI.** Long-running agent tasks must stay off the UI thread and surface progress through the ViewModels.
5. **Localized messaging.** Any user-facing string added for agents must be routed through LocalizationService resources.
6. **Bilingual UX parity.** UI changes must be validated against both zh-CN and en-US resources to ensure layout and copy remain balanced.
7. **Incremental delivery.** Finish each feature slice with a dedicated commit that references the original requirement and summarises the implementation.
8. **Test-first mindset.** Every agent-facing service must have automated coverage for critical logic (rules, IO, retries) and the test suite must pass before integration merges.

## Architecture Checklist

When introducing a new agent or capability:

- [ ] Define clear interfaces in `Services/Agents/` (e.g., `IAgent`, `IAgentContext`).
- [ ] Extend `AppSettings` with any persisted agent configuration (default prompts, toggles, credentials) and provide defaults for both zh-CN/en-US.
- [ ] Hook into `MainWindowViewModel` via dependency injection for orchestration.
- [ ] Provide granular logging using `DiagnosticLogger`.
- [ ] Update resource dictionaries with localized strings.
- [ ] Add unit/integration tests or debug harnesses to exercise new agents, and run `dotnet test SnapDescribe.sln` before submission.

## MCP Integration Notes

- Maintain a separate `Services/Mcp/` namespace for protocol adapters.
- Follow the MCP spec for session management and capability negotiation.
- Expose adapter configuration via the Settings UI (e.g., endpoint URL, credentials, enabled flag).

## External Tool Execution

- Wrap shell execution inside dedicated services to control timeouts, environment variables, and sandboxing.
- All tool outputs should be captured and attached to the conversation history when relevant.
- Ensure tooling paths are configurable via AppSettings.

## Agent UX Patterns

- Agent results should appear as additional assistant messages with metadata tags (e.g., `assistant/agent` role).
- Provide quick actions in the ResultDialog to re-run or refine agent steps.
- When agents modify files, surface diffs or summaries before applying changes.

## Current Implementation Snapshot (October 2025)

- **Capability plumbing**: `CapabilityIds.Agent` resolves to `AgentExecutionService`, which chains auto-run tools (`ShellAgentToolRunner`) and GLM chat while logging every step back into `CaptureRecord` and persisted transcripts.
- **Settings & persistence**: `AgentSettings` (on `AppSettings.Agent`) stores the global toggle plus a collection of agent profiles (`AgentProfile`). Each profile carries its own prompt, flags, and tool list. Everything is persisted to `%APPDATA%/SnapDescribe/settings.json`.
- **UI entry points**: *Settings → 智能体编排*（Agent Orchestration）标签页提供左侧的智能体列表与右侧的详细编辑器，支持新增/复制/删除智能体、管理提示词及工具链。*Settings → Trigger Rules* 里的规则在选择 “Autogen Agent Pipeline” 后，可指定具体智能体。
- **Conversation surface**: Tool outputs appear as dedicated `tool` messages in ResultDialog, immediately followed by the synthesized assistant reply. Follow-up prompts reuse the same orchestration path.
- **Testing**: `AgentExecutionServiceTests` cover message ordering, tool-output inclusion, and payload composition; integration UI flows remain manual.
- **Known limitations**: Only single-machine CLI tools are supported; MCP adapters, remote executors, multiple interacting agents, and richer error recovery are still on the roadmap.

## Configuration Walkthrough (Preview)

1. **Enable orchestration & create agents** – open *Settings → 智能体编排*，勾选 “启用智能体编排”，在左侧列表中新增智能体，右侧面板可编辑系统提示词、工具链及工具运行策略。
2. **Configure tools** – “新增工具” 支持命令行或脚本调用，参数模板同样接受 `{prompt}`、`{message}`、`{imagePath}`、`{processName}`、`{timestamp}` 等占位符，超时时间默认 30 秒。
3. **Assign via rules** – in *Settings → Trigger Rules* select “Autogen Agent Pipeline” and choose the desired agent profile from the dropdown. Key/value parameters remain available for advanced scenarios.
4. **Run & iterate** – once a capture matches the rule, SnapDescribe executes the agent’s auto-run tools, appends outputs to the conversation, and invokes GLM for the final response. Use follow-up chat to re-run the pipeline with new prompts.

> Disable the preview at any time by unchecking the toggle in the Agent Orchestration tab; rules automatically fall back to the standard language model capability.

## Security & Privacy

- Never send sensitive disk paths or full clipboard contents to external endpoints without explicit user consent.
- Log agent decisions with enough context for audit, but redact tokens or secrets.
- Respect the offline mode: agents must honor settings that disable network calls.

## Packaging Notes

- Release automation (`.github/workflows/release.yml`) now produces both a portable ZIP bundle and an NSIS installer (`SnapDescribeSetup.exe`). Keep the installer script (`installer/SnapDescribeInstaller.nsi`) aligned whenever new binaries or resources are added.
- OCR runs on bundled `tessdata` assets (`eng`, `chi_sim`). Any new language packs or native dependencies must be included in the repository and referenced by both the publish pipeline and installer.
- The desktop client exposes a `--shutdown` CLI switch. Installers and external automation should trigger it before touching files so that the running instance can exit cleanly (the NSIS installer does this automatically before falling back to `taskkill`).

## Roadmap Alignment

1. Process-aware prompt overrides ✅
2. External tool hooks ⏳
3. MCP bridge ⏳
4. Standard agent contracts ⏳
5. Conversation experience upgrades ⏳
6. AI Assistant hub ⏳

Keep this document up to date as the implementation evolves.

## Agent Runtime Direction

- Base the runtime on the latest Autogen framework concepts (conversable agents, tool registries, and coordinators) so that we can reuse contemporary multi-agent patterns without fragmenting the ecosystem.
- Map our internal services (`IAgent`, `IAgentContext`, `IAgentToolRunner`) to Autogen abstractions: a coordinator agent orchestrates specialized worker agents while snapshots remain deterministic for reproducibility.
- Start with deterministic tool execution for local CLI commands; MCP adapters and remote tools will register through the same Autogen-compatible interfaces when introduced.
- Keep the loop deterministic: resolve capability → prepare agent group → execute configured tools → feed transcripts to GLM for the final response.

## Python Sidecar Expansion (Gated Preview)

To unlock LangGraph-level agent capabilities without rewriting the Avalonia client:

- **Architecture**: Keep SnapDescribe as the orchestration host (capture, OCR, logging, tool execution). Introduce a detachable Python sidecar that runs LangGraph workflows and issues declarative actions back to the host. The sidecar never spawns shell commands directly—the host continues to enforce tool white-lists and timeouts.
- **Communication**: A local RPC channel (HTTP loopback or named pipe) exchanges `StartRun`, `AgentEvent`, `ToolResult`, and `CancelRun` payloads. Each payload carries `runId`/`recordId` for deterministic replay. Cancellation flows from the UI through the host into the sidecar.
- **Packaging & Delivery**: Package the sidecar as an embedded CPython runtime plus minimal LangGraph dependencies (~70 MB unpacked). Publish `snapdescribe-sidecar-win64.zip` and a manifest JSON with version + SHA256 on every GitHub Release. The base installer stays slim.
- **On-Demand Install**: The Agent Settings tab will detect the sidecar state, prompt users to download/update, verify hashes, then extract to `%LOCALAPPDATA%\SnapDescribe\sidecar\python\<version>\`. Power users can point to a custom path for development builds.
- **Lifecycle**: `AgentExecutionService` becomes an RPC client that streams conversation context to the sidecar and relays tool callbacks through existing `ShellAgentToolRunner`. `App.axaml.cs` owns process startup/shutdown, and `DiagnosticLogger` records install/upgrade events.
- **Testing**: Python sidecar ships with pytest coverage (graph lifecycle, tool callback serialization, cancellation). .NET tests mock the RPC layer to verify UI/state handling. CI publishes both the main installer and the optional sidecar artifact.
- **Global reuse**: The sidecar is packaged as a shared Windows module with a documented RPC schema. Future desktop or service apps can authenticate and reuse the same runtime so the broader ecosystem benefits from a single agent capability surface.

The full checklist lives in `docs/PythonSidecarDesign.md`. Use it when planning tasks for the dedicated agent to implement this preview feature.

### Local Agent & Workflow Roadmap

- 丰富本地生态：SnapDescribe 将持续引入大量本地工具、资源索引和内部接口。Agent 配置需要允许用户把这些能力作为 LangGraph 工具即时挂载或卸载。
- 多 Agent 管理：用户可在 UI 中创建/克隆多个本地 Agent Profile，并可在运行时将 Agent 之间互相嵌套调用或共享工具链。
- 动态扩展：运行过程中允许用户追加新的工具或切换至不同 Agent，而无需重启 sidecar；配置变更要可审计并可回滚。
- Workflow 规划：在框架稳定后，会基于 LangGraph 的图结构提供可视化 Workflow 设计器，支持串行/并行步骤、条件分支、人工确认节点等高级编排。
- 上述规划须继续遵守本章的 determinism、可观测性、安全约束，并在实现前更新设计文档。
