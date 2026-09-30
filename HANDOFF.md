# Handoff / GitHub upload checklist

This directory is a complete repository, not an overlay patch.

Before publishing:

1. Review `README.md`, `docs/SECURITY.md` and `.env.example`.
2. Do not add a real `.env`, SQLite database, API key or agent workspace.
3. Run:

   ```bash
   python3 scripts/validate_repo.py
   dotnet restore
   dotnet build --configuration Release
   ```

4. Optionally run the MCP Inspector (or a stdio JSON-RPC script) against the server and exercise at least:
   - `random_number`;
   - `rag_search` with a small text file;
   - `memory_remember` -> `memory_recall` -> `memory_get`;
   - `events_watch` and a matching memory update;
   - scheduler/approval flow with external capabilities still disabled;
   - `shell_info` with `SHELL_ENABLED=false` (must report `enabled=false`) and `shell_run` inside `SHELL_ALLOWED_ROOTS` with it enabled;
   - `telegram_messages_wait` with `TELEGRAM_DELIVERY=inbox` when a bot token is available.
5. Deploying next to a running copy on Windows: the MCP host keeps `SampleMcpServer.exe` and its DLLs locked. Either stop the host first, or rename the locked files to `*.old`, copy the new build over, restart the host and delete `*.old`.

Releases: pushing a tag `vX.Y.Z` runs `.github/workflows/release.yml`, which publishes self-contained builds (no .NET runtime needed on the target) for win-x64, linux-x64, osx-x64 and osx-arm64, packs them (zip / tar.gz, plus SHA256SUMS) and attaches them to the GitHub Release with the matching `CHANGELOG.md` section as notes. The tag must point at a commit that contains this workflow.

Current release tag: `v2.2.0`. See `CHANGELOG.md` for the release notes and `VALIDATION.md` for the checks that were actually executed.
