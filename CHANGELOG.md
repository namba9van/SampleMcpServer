# Changelog

## 2.2.0 - operator shell tools

- Added `ShellTools` (`shell_run`, `shell_run_to_file`, `shell_info`): operator-side command execution, off by default (`SHELL_ENABLED=false`), working directory and output files restricted to `SHELL_ALLOWED_ROOTS`, per-command timeout with process-tree kill and inline output truncation.
- Added `TELEGRAM_DELIVERY=queue|inbox|both` and the Telegram inbox tools `telegram_messages_poll`, `telegram_messages_wait`, `telegram_messages_ack`, so an interactive MCP client can answer Telegram messages itself instead of the Agent Host.
- Telegram addressing: inbox messages carry `Addressing` (chat / reply_to_bot / mention / reply), reply-to metadata and thread id; `TELEGRAM_INBOX_FILTER=addressed` keeps only messages addressed to the bot; `telegram_send_message` accepts `replyToMessageId` and returns sent message ids.
- Telegram polling is now guarded by a cross-process lock file next to the memory database, so several server processes started by the MCP host no longer fight over getUpdates (409 Conflict); 409 is retried with backoff instead of a stack trace.
- Host hardening: an unhandled exception in a background service (e.g. the Telegram bridge) no longer stops the MCP server and its tools (`BackgroundServiceExceptionBehavior.Ignore`).
- Release workflow: pushing a `v*` tag publishes framework-dependent builds for win-x64, linux-x64, osx-x64 and osx-arm64 and attaches them to the GitHub Release.
- CI: the repository validator now runs before `dotnet build` (it rejects the `bin`/`obj` directories the build creates, which failed every previous run) and ignores `.git`.
- Documented the `SHELL_*` environment block in `docs/CONFIGURATION.md`, `.env.example` and `docs/SECURITY.md`.

## 2.1.0 - Telegram bridge release

- Added an optional Telegram bridge for remote agent requests from allow-listed chats/channels.
- Added Telegram notification delivery so completed Telegram-origin jobs can reply to the originating chat.
- Added `telegram_bot_status` and `telegram_send_message` MCP tools.
- Documented a full `mcp.json` example with model, memory, governance and Telegram environment settings.

## 2.0.0 - handoff candidate

- Added durable SQLite model memory and semantic recall.
- Added token-budgeted `memory_context` and compact two-stage recall.
- Added memory ranking, TTL, maintenance and consolidation.
- Added provenance/evidence, graph relations, supersession and conflict resolution.
- Added persistent event bus, trigger actions and durable queue jobs.
- Added embedded Agent Host with lease/recovery and bounded tool loop.
- Added capabilities, one-shot approvals, scheduler, notifications and audit log.
- Added governed HTTP/GitHub/filesystem/shell actions for the embedded host.
- Hardened webhook/HTTP redirect handling and filesystem path checks.
- Added queue lease renew/fail operations for external orchestrators.
- Replaced the old heavy RAG dependency stack with a compact document loader + OpenAI-compatible embeddings.
- Kept backward-compatible environment aliases for historical upstream typos.
