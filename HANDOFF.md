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

4. Optionally run the MCP Inspector against the stdio server and exercise at least:
   - `random_number`;
   - `rag_search` with a small text file;
   - `memory_remember` -> `memory_recall` -> `memory_get`;
   - `events_watch` and a matching memory update;
   - scheduler/approval flow with external capabilities still disabled.
5. Create the new Git repository and push the contents of this folder as its root.

Recommended first release tag: `v2.0.0` after the .NET build and smoke tests pass on the target machine.
