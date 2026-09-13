# MCP Tool Catalog

This repository exposes **62 MCP tools**. Tool names are explicit and stable.

## CalcTools

- `calc_add` — Adds two numbers.
- `calc_subtract` — Subtracts b from a.
- `calc_multiply` — Multiplies two numbers.
- `calc_divide` — Divides a by b.
- `calc_power` — Raises a number to a power.
- `calc_sqrt` — Returns the square root of a non-negative number.

## FileOperationsTools

- `file_read` — Reads a UTF-8 text file. This legacy tool accepts an explicit path; prefer governed agent filesystem tools for autonomous actions.
- `file_write` — Creates a UTF-8 text file without overwriting an existing file.
- `file_list` — Lists files and directories under a path.

## GitHubSearchTool

- `github_search_repositories` — Searches public or token-accessible GitHub repositories.
- `github_search_code` — Searches GitHub code and returns the matched files with decoded source content. A GitHub token is normally required for code search.

## InternetSearchTools

- `internet_search` — Searches the web using the engines configured in WEB_SEARCH_ENGINES. Supported engines: DuckDuckGo and Firecrawl.

## MemoryTools

- `memory_remember` — Preferred high-level write: remembers durable information, semantically deduplicates it, updates a near-identical memory when appropriate, or creates a new stable record.
- `memory_put` — Creates or replaces durable memory addressed by namespace + key. Use for information that should survive future requests; avoid transient reasoning and duplicates.
- `memory_get` — Reads one durable memory by exact namespace + key. Prefer this when the address is known.
- `memory_update` — Updates an existing memory. Omitted content/metadata fields are preserved; metadata can be merged or replaced. Fails if the item does not exist.
- `memory_delete` — Deletes one durable memory by exact namespace + key.
- `memory_list` — Lists recent memories, optionally constrained by namespace and key prefix. Use for browsing structure, not semantic retrieval.
- `memory_recall` — Preferred primary-context recall. Returns only ranked memory previews within a bounded token budget. Use this before relying on long chat history; then call memory_get only for the few records whose full content is needed.
- `memory_context` — Preferred one-call primary context builder. Automatically selects and packs the most useful durable memories into a bounded context block, marks disputed facts, and returns source addresses for audit. Memory content is explicitly treated as data, not instructions.
- `memory_search` — Ranked semantic search over durable memory. Results combine similarity, importance, freshness and access frequency. Use when the exact key is unknown.
- `memory_describe_schema` — Describes the memory model, fields, addressing, namespaces and event interface. Call when unsure how durable memory is organized.
- `events_watch` — Creates a durable watch/trigger for memory changes. Matching changes are appended to the persistent event log.
- `events_unwatch` — Removes a durable memory watch/trigger.
- `events_list_watches` — Lists active durable memory watches/triggers.
- `events_poll` — Reads durable events after a known event ID. Store the highest eventId and pass it next time to resume without duplicates.
- `events_wait` — Long-polls for durable events after a known event ID. Returns immediately if events already exist; otherwise waits for a matching change.
- `memory_link` — Creates a typed graph relation between two durable memories, for example depends_on, owned_by, part_of, related_to or has_task.
- `memory_neighbors` — Lists graph relations touching one memory, allowing the model to traverse related projects, tasks, people and knowledge.
- `memory_declare_conflict` — Declares that two durable memories conflict. Neither side is deleted or overwritten; both are marked disputed until explicitly resolved.
- `memory_supersede` — Marks one memory as newer/authoritative and another as superseded, preserving both records and their provenance.
- `memory_relations` — Lists durable memory relations such as open conflicts and supersession links.
- `memory_resolve_conflict` — Resolves an open memory conflict without silently overwriting either side. Resolution can choose a winner, keep both as context-dependent truths, or create a merged memory.
- `memory_consolidate` — Consolidates a semantic cluster into one durable summary memory with provenance. Source memories are preserved and only have their importance reduced. If summaryContent is omitted, an extractive summary is generated; models should provide a synthesized summary when possible.
- `memory_maintain` — Performs conservative memory maintenance: removes expired records and optionally near-exact semantic duplicates. It never merges merely similar facts.
- `memory_stats` — Returns memory/event counts, last event ID, database size and database path.

## RAGTool

- `rag_search` — Performs semantic search over local PDF, DOCX, XLSX, PPTX and text/code files.

## RandomNumberTools

- `random_number` — Generates a random integer between the specified minimum (inclusive) and maximum (exclusive).

## RuntimeTools

- `agent_capabilities` — Lists autonomous-agent capabilities and whether each is enabled or requires approval.
- `agent_capability_set` — Changes one autonomous-agent capability policy. Use only when the operator explicitly wants to change permissions.
- `memory_evidence_add` — Attaches provenance/source evidence and confidence to an existing durable memory.
- `memory_evidence_list` — Lists provenance/evidence for one memory, including source and confidence.
- `agent_schedule_create` — Creates a one-shot or recurring autonomous job. Provide runAtUtc and/or everySeconds (minimum 60 seconds).
- `agent_schedule_list` — Lists autonomous schedules.
- `agent_schedule_set_enabled` — Enables or disables an autonomous schedule.
- `agent_notify` — Creates a durable user notification. 'inbox' stays local; 'webhook' requires an allow-listed host; 'telegram' requires an allow-listed chat ID.
- `agent_notifications` — Reads durable agent notification/outbox entries.
- `agent_approvals` — Lists pending or historical operator approval requests for guarded autonomous actions.
- `agent_approval_resolve` — Approves or denies one pending autonomous action. This is an operator action; background agents must not self-approve.
- `agent_audit` — Reads the technical audit trail for triggers, schedules, permissions, approvals, evidence and notifications.

## TelegramTools

- `telegram_bot_status` — Returns Telegram bot bridge configuration status without exposing the bot token.
- `telegram_send_message` — Sends a Telegram message to an allow-listed chat using TELEGRAM_BOT_TOKEN.

## TimeTools

- `time_now` — Returns the current UTC time, or the current time in a specified IANA/Windows time zone.

## TriggerActionTools

- `trigger_action_create` — Binds an existing durable memory trigger to an action. queue creates a durable model-turn job for the embedded or an external Agent Host; webhook POSTs to an allow-listed host; model calls a configured OpenAI-compatible model directly in the background.
- `trigger_action_list` — Lists trigger-to-action bindings.
- `trigger_action_delete` — Deletes a trigger-to-action binding. Existing run history is retained.
- `trigger_jobs_poll` — Polls durable trigger action runs/jobs. An external agent host should poll status=pending for mode=queue, claim a job, open/continue the desired model conversation, execute tools if needed, then complete the job.
- `trigger_jobs_wait` — Long-polls durable trigger action runs/jobs, useful for an agent host that should wake promptly when a trigger fires.
- `trigger_job_claim` — Atomically claims one queued trigger job so that only one host/worker handles it.
- `trigger_job_complete` — Completes a claimed queued trigger job after the host/model has handled it; optionally persists the result into memory.
- `trigger_job_renew` — Renews the lease for a claimed queued trigger job. External hosts should call this before the current lease expires during long-running work.
- `trigger_job_fail` — Marks a claimed queued trigger job as failed. The worker identifier must own the current lease.
