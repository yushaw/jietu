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

## Security & Privacy

- Never send sensitive disk paths or full clipboard contents to external endpoints without explicit user consent.
- Log agent decisions with enough context for audit, but redact tokens or secrets.
- Respect the offline mode: agents must honor settings that disable network calls.

## Roadmap Alignment

1. Process-aware prompt overrides ✅
2. External tool hooks ⏳
3. MCP bridge ⏳
4. Standard agent contracts ⏳
5. Conversation experience upgrades ⏳
6. AI Assistant hub ⏳

Keep this document up to date as the implementation evolves.
