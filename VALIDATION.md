# Validation report

Release candidate: **v2.2.0** (operator shell tools, Telegram inbox and addressing, polling lock).

## Completed checks

- Repository layout is self-contained: solution, project file, source, docs, CI, license and configuration example are present.
- 22 C# source files passed lexical/delimiter structural validation (`scripts/validate_repo.py`).
- 68 explicit MCP tool names are unique and use stable `snake_case` names.
- Embedded SQLite `CREATE TABLE` schemas execute successfully against a clean SQLite database; the `telegram_inbox` table is created on first use and upgraded in place (`ALTER TABLE` for the addressing columns).
- Environment variables referenced by the implementation are represented in `.env.example` (legacy typo aliases are intentionally accepted only for compatibility).
- Basic secret scanning found no committed API keys/tokens.
- Redirect following is disabled in governed HTTP/webhook clients so host allowlists cannot be bypassed by an automatic redirect.
- `dotnet build --configuration Release` on Windows 10 / .NET SDK 8.0.424: 0 warnings, 0 errors.
- Stdio smoke test against the Release build (JSON-RPC `initialize` → `tools/list` → `tools/call`):
  - `tools/list` returns 68 tools;
  - `shell_info`, `shell_run`, `shell_run_to_file` behave as documented: commands run inside `SHELL_ALLOWED_ROOTS`, a working directory outside the roots is rejected with a readable error, a timed-out command has its process tree killed, `SHELL_ENABLED=false` rejects execution;
  - `telegram_messages_poll` / `telegram_messages_wait` / `telegram_messages_ack` work against an empty and a populated inbox; live messages from an allow-listed group arrive with `Addressing` = `chat` or `reply_to_bot`, and `telegram_send_message` with `replyToMessageId` posts a Telegram reply and returns the sent message id;
  - two server processes started by the same MCP host share one Telegram token without `409 Conflict`: one holds the poll lock, the other stands by.

Run the included validator at any time:

```bash
python3 scripts/validate_repo.py
```

## Known limitations

- The operator shell restricts only the working directory and output file; commands run with the server account and can touch anything that account can. Keep `SHELL_ENABLED=false` unless the MCP client is a trusted local operator (see `docs/SECURITY.md`).
- `telegram_messages_wait` is a long-poll; MCP hosts that cap tool calls (Claude Desktop through a linked cloud session: 60 s) need `timeoutSeconds` below that cap.
- The GitHub Actions workflow builds on Linux; the shell tool defaults to `/bin/sh` there and to `powershell.exe` on Windows.
