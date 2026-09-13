# Changelog

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
