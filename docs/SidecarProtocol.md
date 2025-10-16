# Sidecar Protocol Overview

本文件记录 SnapDescribe Host 与 Python sidecar 之间通信用的关键接口与事件类型。具体字段、示例 payload 会在实现时补充；本节仅定义最小规范，保证双方可以并行开发。

## 基本约束
- 传输：HTTP loopback（默认 `127.0.0.1:51055`），事件通过 Server-Sent Events (SSE)。
- 鉴权：所有请求需携带 `Authorization: Bearer <token>`。token 由 Host 生成并写入 sidecar 配置。
- 协议版本：`X-Sidecar-Protocol: 1.0`。升级协议时需同时 bump 版本号并兼容旧 Host。
- 通用字段：`runId`（UUID）、`recordId`、`agentId`、`timestamp`（ISO 8601）、`locale`。

## REST 端点
| Method & Path | 说明 | 关键字段 |
| ------------- | ---- | -------- |
| `GET /status` | 返回服务状态、`version`、`protocolVersion`、`agentsEtag`、`maxConcurrentRuns`。 | — |
| `GET /agents?locale=xx&etag=yy` | 返回 agent 模板列表，若 `etag` 未变化可返回 304。 | `agents`（数组），每项含 `id`、`name`/`description` 多语言、`version`、`parameters`、`tools`。 |
| `POST /runs` | 创建新的运行。请求体包含会话上下文、工具白名单、约束。响应 202，并在 `Location` 头返回事件流地址。 | `conversation`、`imageReference` 或 `imageBase64`、`toolInventory`、`constraints`。 |
| `GET /runs/{runId}/events` | SSE 事件流。见“事件类型”。 | — |
| `POST /runs/{runId}/tools/{toolCallId}` | Host 回传工具执行结果。 | `status` (`ok`/`error`/`timeout`)、`exitCode`、`stdout`、`stderr`、`durationMs`。 |
| `POST /runs/{runId}/cancel` | 请求取消运行。sidecar 必须尽快发送 `run.cancelled`。 | 可选 `reason`。 |

## SSE 事件
| 事件名 | data 结构摘要 | 说明 |
| ------ | -------------- | ---- |
| `run.started` | `{ runId }` | Run 建立成功。 |
| `run.thought` | `{ runId, text }` | 模型思考片段，供 UI 展示进度。 |
| `run.tool_call` | `{ runId, toolCallId, toolId, arguments, timeoutSec }` | 请求 Host 执行工具。 |
| `run.tool_progress` | `{ runId, toolCallId, streamChunk }`（可选） | sidecar 转发实时 stdout/stderr。 |
| `run.completed` | `{ runId, response, metadata }` | 最终回复。 |
| `run.failed` | `{ runId, errorCode, message, details }` | 异常退出。 |
| `run.cancelled` | `{ runId, reason }` | 取消成功。 |
| `run.debug` | `{ runId, message, level }`（可选） | 调试日志，默认仅开发模式启用。 |

## 错误码（草案）
- `400 INVALID_REQUEST`：请求体缺少必填字段或字段非法。
- `401 UNAUTHORIZED`：token 丢失或无效。
- `409 RUN_CONFLICT`：重复的 `runId`。
- `410 RUN_NOT_FOUND`：`runId` 不存在或已过期。
- `429 TOO_MANY_RUNS`：超过并发上限，响应体应包含 `retryAfterSec`。
- `500 INTERNAL_ERROR`：sidecar 内部错误，日志需关联 `traceId`。

## 超时与重试
- Host 在 `POST /runs` 后若 5 秒内未收到 `run.started`，需提示用户并支持重试。
- 工具执行超时由 Host 控制；若超时将 `status: "timeout"` 返回 sidecar。
- SSE 连接断开后，Host 应使用 `Last-Event-ID` 继续订阅；sidecar 应支持最近 100 条事件回放。

## 兼容性与演进
- 新增字段必须向后兼容（可选字段 + 默认值）；破坏性改动需 bump `protocolVersion` 并更新本文档。
- Agent 模板若因协议变更需要迁移，需在 `description` 中给出升级提示，并在 `agents.md` 记录对应 Roadmap。

> 实现阶段请务必同步更新示例 payload、字段类型与状态码，以便 Host 与 sidecar 团队并行开发。
