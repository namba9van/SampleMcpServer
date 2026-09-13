using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services;

/// <summary>
/// Persistent policy and operations layer for the autonomous agent runtime.
/// It shares the memory SQLite database so capabilities, approvals, schedules,
/// notifications, evidence and audit records survive process restarts.
/// </summary>
public sealed class RuntimeGovernanceService : BackgroundService
{
    private readonly ModelMemoryService _memory;
    private readonly TriggerAutomationService _automation;
    private readonly ILogger<RuntimeGovernanceService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private volatile bool _initialized;

    public RuntimeGovernanceService(
        ModelMemoryService memory,
        TriggerAutomationService automation,
        ILogger<RuntimeGovernanceService> logger)
    {
        _memory = memory;
        _automation = automation;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            await _memory.InitializeAsync(ct);
            await using var db = Open();
            await db.OpenAsync(ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """
                PRAGMA busy_timeout=5000;

                CREATE TABLE IF NOT EXISTS agent_capabilities (
                    capability TEXT PRIMARY KEY,
                    enabled INTEGER NOT NULL,
                    requires_approval INTEGER NOT NULL DEFAULT 0,
                    note TEXT NULL,
                    updated_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS memory_evidence (
                    evidence_id TEXT PRIMARY KEY,
                    namespace TEXT NOT NULL,
                    key TEXT NOT NULL,
                    memory_version INTEGER NOT NULL,
                    source_type TEXT NOT NULL,
                    source_ref TEXT NULL,
                    source_label TEXT NULL,
                    confidence REAL NOT NULL DEFAULT 0.5,
                    observed_utc TEXT NOT NULL,
                    evidence_text TEXT NULL,
                    created_utc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_memory_evidence_memory
                    ON memory_evidence(namespace, key, created_utc DESC);

                CREATE TABLE IF NOT EXISTS agent_schedules (
                    schedule_id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    prompt TEXT NOT NULL,
                    run_at_utc TEXT NULL,
                    every_seconds INTEGER NULL,
                    next_run_utc TEXT NOT NULL,
                    enabled INTEGER NOT NULL DEFAULT 1,
                    context_token_budget INTEGER NOT NULL DEFAULT 1800,
                    created_utc TEXT NOT NULL,
                    last_run_utc TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_agent_schedules_due
                    ON agent_schedules(enabled, next_run_utc);

                CREATE TABLE IF NOT EXISTS agent_notifications (
                    notification_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    channel TEXT NOT NULL,
                    destination TEXT NULL,
                    title TEXT NOT NULL,
                    body TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'pending',
                    attempts INTEGER NOT NULL DEFAULT 0,
                    error_text TEXT NULL,
                    created_utc TEXT NOT NULL,
                    delivered_utc TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_agent_notifications_status
                    ON agent_notifications(status, notification_id);

                CREATE TABLE IF NOT EXISTS agent_approvals (
                    approval_id TEXT PRIMARY KEY,
                    capability TEXT NOT NULL,
                    action_name TEXT NOT NULL,
                    arguments_json TEXT NOT NULL,
                    reason TEXT NULL,
                    status TEXT NOT NULL DEFAULT 'pending',
                    created_utc TEXT NOT NULL,
                    resolved_utc TEXT NULL,
                    resolution_note TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_agent_approvals_status
                    ON agent_approvals(status, created_utc);

                CREATE TABLE IF NOT EXISTS agent_audit (
                    audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    category TEXT NOT NULL,
                    action TEXT NOT NULL,
                    outcome TEXT NOT NULL,
                    details_json TEXT NOT NULL,
                    created_utc TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_agent_audit_created
                    ON agent_audit(created_utc DESC);
                """;
            await cmd.ExecuteNonQueryAsync(ct);

            await SeedCapabilitiesAsync(db, ct);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<IReadOnlyList<AgentCapability>> ListCapabilitiesAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT capability, enabled, requires_approval, note, updated_utc
            FROM agent_capabilities
            ORDER BY capability;
            """;

        var result = new List<AgentCapability>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new AgentCapability(
                reader.GetString(0),
                reader.GetInt64(1) != 0,
                reader.GetInt64(2) != 0,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4))));
        }

        return result;
    }

    public async Task<AgentCapability> SetCapabilityAsync(
        string capability,
        bool enabled,
        bool requiresApproval = false,
        string? note = null,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        capability = NormalizeCapability(capability);
        var now = DateTimeOffset.UtcNow;

        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agent_capabilities(capability, enabled, requires_approval, note, updated_utc)
            VALUES($capability, $enabled, $approval, $note, $updated)
            ON CONFLICT(capability) DO UPDATE SET
                enabled=excluded.enabled,
                requires_approval=excluded.requires_approval,
                note=excluded.note,
                updated_utc=excluded.updated_utc;
            """;
        cmd.Parameters.AddWithValue("$capability", capability);
        cmd.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$approval", requiresApproval ? 1 : 0);
        cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updated", now.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        await AuditAsync("capability", "set", "ok", new { capability, enabled, requiresApproval, note }, ct);
        return new AgentCapability(capability, enabled, requiresApproval, note, now);
    }

    public async Task<CapabilityDecision> CheckCapabilityAsync(
        string capability,
        string actionName,
        object? arguments = null,
        bool createApproval = true,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        capability = NormalizeCapability(capability);

        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT enabled, requires_approval FROM agent_capabilities WHERE capability=$capability;";
        cmd.Parameters.AddWithValue("$capability", capability);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new CapabilityDecision(false, false, null, $"Capability '{capability}' is not configured.");

        var enabled = reader.GetInt64(0) != 0;
        var requiresApproval = reader.GetInt64(1) != 0;
        await reader.DisposeAsync();

        if (!enabled)
        {
            await AuditAsync("capability", actionName, "denied", new { capability }, ct);
            return new CapabilityDecision(false, false, null, $"Capability '{capability}' is disabled.");
        }

        if (!requiresApproval)
            return new CapabilityDecision(true, false, null, null);

        // Canonical JSON makes one-shot approvals stable even if a model changes object property order.
        var argumentsJson = CanonicalizeJson(JsonSerializer.Serialize(arguments ?? new { }));
        await using (var approved = db.CreateCommand())
        {
            approved.CommandText = """
                SELECT approval_id
                FROM agent_approvals
                WHERE capability=$capability
                  AND action_name=$action
                  AND arguments_json=$arguments
                  AND status='approved'
                ORDER BY resolved_utc ASC
                LIMIT 1;
                """;
            approved.Parameters.AddWithValue("$capability", capability);
            approved.Parameters.AddWithValue("$action", actionName);
            approved.Parameters.AddWithValue("$arguments", argumentsJson);

            var approvedId = await approved.ExecuteScalarAsync(ct) as string;
            if (!string.IsNullOrWhiteSpace(approvedId))
            {
                await using var consume = db.CreateCommand();
                consume.CommandText = """
                    UPDATE agent_approvals
                    SET status='consumed'
                    WHERE approval_id=$id AND status='approved';
                    """;
                consume.Parameters.AddWithValue("$id", approvedId);
                await consume.ExecuteNonQueryAsync(ct);

                await AuditAsync(
                    "approval",
                    "consume",
                    "ok",
                    new { approvalId = approvedId, capability, actionName },
                    ct);
                return new CapabilityDecision(true, false, approvedId, null);
            }
        }

        if (!createApproval)
            return new CapabilityDecision(false, true, null, "Approval required.");

        var request = await RequestApprovalAsync(
            capability,
            actionName,
            argumentsJson,
            "Capability policy requires explicit approval.",
            ct);
        return new CapabilityDecision(false, true, request.ApprovalId, "Approval required before this action can run.");
    }

    public async Task<MemoryEvidence> AddEvidenceAsync(
        string memoryNamespace,
        string key,
        string sourceType,
        string? sourceRef = null,
        string? sourceLabel = null,
        double confidence = 0.7,
        string? evidenceText = null,
        DateTimeOffset? observedUtc = null,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var record = await _memory.GetAsync(memoryNamespace, key, ct)
                     ?? throw new KeyNotFoundException($"Memory '{memoryNamespace}/{key}' not found.");

        sourceType = (sourceType ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(sourceType))
            throw new ArgumentException("sourceType is required.", nameof(sourceType));

        confidence = Math.Clamp(confidence, 0, 1);
        var item = new MemoryEvidence(
            Guid.NewGuid().ToString("N"),
            memoryNamespace,
            key,
            record.Version,
            sourceType,
            sourceRef,
            sourceLabel,
            confidence,
            observedUtc ?? DateTimeOffset.UtcNow,
            evidenceText,
            DateTimeOffset.UtcNow);

        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO memory_evidence(
                evidence_id, namespace, key, memory_version, source_type,
                source_ref, source_label, confidence, observed_utc, evidence_text, created_utc)
            VALUES($id,$namespace,$key,$version,$type,$ref,$label,$confidence,$observed,$text,$created);
            """;
        cmd.Parameters.AddWithValue("$id", item.EvidenceId);
        cmd.Parameters.AddWithValue("$namespace", item.Namespace);
        cmd.Parameters.AddWithValue("$key", item.Key);
        cmd.Parameters.AddWithValue("$version", item.MemoryVersion);
        cmd.Parameters.AddWithValue("$type", item.SourceType);
        cmd.Parameters.AddWithValue("$ref", (object?)item.SourceRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$label", (object?)item.SourceLabel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$confidence", item.Confidence);
        cmd.Parameters.AddWithValue("$observed", item.ObservedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$text", (object?)item.EvidenceText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", item.CreatedUtc.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        await AuditAsync(
            "evidence",
            "add",
            "ok",
            new { memoryNamespace, key, sourceType, confidence, item.EvidenceId },
            ct);
        return item;
    }

    public async Task<IReadOnlyList<MemoryEvidence>> ListEvidenceAsync(
        string memoryNamespace,
        string key,
        int limit = 50,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        limit = Math.Clamp(limit, 1, 200);

        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT evidence_id, namespace, key, memory_version, source_type,
                   source_ref, source_label, confidence, observed_utc, evidence_text, created_utc
            FROM memory_evidence
            WHERE namespace=$namespace AND key=$key
            ORDER BY created_utc DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$namespace", memoryNamespace);
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$limit", limit);

        var result = new List<MemoryEvidence>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(ReadEvidence(reader));
        return result;
    }

    public async Task<AgentSchedule> CreateScheduleAsync(
        string name,
        string prompt,
        DateTimeOffset? runAtUtc = null,
        int? everySeconds = null,
        int contextTokenBudget = 1800,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("prompt is required.", nameof(prompt));
        if (runAtUtc is null && everySeconds is null)
            throw new ArgumentException("runAtUtc or everySeconds is required.");

        if (everySeconds is not null)
            everySeconds = Math.Clamp(everySeconds.Value, 60, 31_536_000);

        var now = DateTimeOffset.UtcNow;
        var nextRun = runAtUtc ?? now.AddSeconds(everySeconds!.Value);
        if (nextRun < now) nextRun = now;

        var schedule = new AgentSchedule(
            Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(name) ? "scheduled agent task" : name.Trim(),
            prompt.Trim(),
            runAtUtc,
            everySeconds,
            nextRun,
            true,
            Math.Clamp(contextTokenBudget, 300, 12_000),
            now,
            null);

        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agent_schedules(
                schedule_id, name, prompt, run_at_utc, every_seconds, next_run_utc,
                enabled, context_token_budget, created_utc, last_run_utc)
            VALUES($id,$name,$prompt,$runAt,$every,$next,1,$budget,$created,NULL);
            """;
        cmd.Parameters.AddWithValue("$id", schedule.ScheduleId);
        cmd.Parameters.AddWithValue("$name", schedule.Name);
        cmd.Parameters.AddWithValue("$prompt", schedule.Prompt);
        cmd.Parameters.AddWithValue("$runAt", (object?)schedule.RunAtUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$every", (object?)schedule.EverySeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$next", schedule.NextRunUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$budget", schedule.ContextTokenBudget);
        cmd.Parameters.AddWithValue("$created", schedule.CreatedUtc.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        await AuditAsync("scheduler", "create", "ok", schedule, ct);
        return schedule;
    }

    public async Task<IReadOnlyList<AgentSchedule>> ListSchedulesAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT schedule_id, name, prompt, run_at_utc, every_seconds, next_run_utc,
                   enabled, context_token_budget, created_utc, last_run_utc
            FROM agent_schedules
            ORDER BY next_run_utc;
            """;

        var result = new List<AgentSchedule>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(ReadSchedule(reader));
        return result;
    }

    public async Task<bool> SetScheduleEnabledAsync(string scheduleId, bool enabled, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE agent_schedules SET enabled=$enabled WHERE schedule_id=$id;";
        cmd.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", scheduleId);
        var updated = await cmd.ExecuteNonQueryAsync(ct) > 0;

        await AuditAsync(
            "scheduler",
            enabled ? "enable" : "disable",
            updated ? "ok" : "not_found",
            new { scheduleId },
            ct);
        return updated;
    }

    public async Task<AgentNotification> NotifyAsync(
        string title,
        string body,
        string channel = "inbox",
        string? destination = null,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        channel = (channel ?? "inbox").Trim().ToLowerInvariant();
        if (channel is not ("inbox" or "webhook" or "telegram"))
            throw new ArgumentException("channel must be inbox, webhook or telegram.", nameof(channel));
        if (channel == "webhook") ValidateNotifyWebhook(destination);
        if (channel == "telegram") ValidateTelegramDestination(destination);

        var safeTitle = string.IsNullOrWhiteSpace(title) ? "Agent notification" : title;
        var safeBody = body ?? string.Empty;
        var now = DateTimeOffset.UtcNow;

        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agent_notifications(channel, destination, title, body, status, created_utc)
            VALUES($channel,$destination,$title,$body,'pending',$created);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$channel", channel);
        cmd.Parameters.AddWithValue("$destination", (object?)destination ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", safeTitle);
        cmd.Parameters.AddWithValue("$body", safeBody);
        cmd.Parameters.AddWithValue("$created", now.ToString("O"));
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));

        await AuditAsync("notification", "enqueue", "ok", new { id, channel, title = safeTitle }, ct);
        return new AgentNotification(id, channel, destination, safeTitle, safeBody, "pending", 0, null, now, null);
    }

    public async Task<IReadOnlyList<AgentNotification>> ListNotificationsAsync(
        string? status = null,
        long afterId = 0,
        int limit = 100,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        limit = Math.Clamp(limit, 1, 500);

        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT notification_id, channel, destination, title, body, status,
                   attempts, error_text, created_utc, delivered_utc
            FROM agent_notifications
            WHERE notification_id>$after
              AND ($status IS NULL OR status=$status)
            ORDER BY notification_id ASC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$after", Math.Max(0, afterId));
        cmd.Parameters.AddWithValue("$status", string.IsNullOrWhiteSpace(status) ? DBNull.Value : status.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$limit", limit);

        var result = new List<AgentNotification>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(ReadNotification(reader));
        return result;
    }

    public async Task<ApprovalRequest> RequestApprovalAsync(
        string capability,
        string actionName,
        string argumentsJson,
        string? reason = null,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var canonicalArguments = CanonicalizeJson(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        var request = new ApprovalRequest(
            Guid.NewGuid().ToString("N"),
            NormalizeCapability(capability),
            actionName,
            canonicalArguments,
            reason,
            "pending",
            DateTimeOffset.UtcNow,
            null,
            null);

        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agent_approvals(
                approval_id, capability, action_name, arguments_json, reason, status, created_utc)
            VALUES($id,$capability,$action,$arguments,$reason,'pending',$created);
            """;
        cmd.Parameters.AddWithValue("$id", request.ApprovalId);
        cmd.Parameters.AddWithValue("$capability", request.Capability);
        cmd.Parameters.AddWithValue("$action", request.ActionName);
        cmd.Parameters.AddWithValue("$arguments", request.ArgumentsJson);
        cmd.Parameters.AddWithValue("$reason", (object?)request.Reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", request.CreatedUtc.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        await AuditAsync(
            "approval",
            "request",
            "pending",
            new { request.ApprovalId, request.Capability, request.ActionName },
            ct);
        return request;
    }

    public async Task<IReadOnlyList<ApprovalRequest>> ListApprovalsAsync(
        string? status = "pending",
        int limit = 100,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        limit = Math.Clamp(limit, 1, 500);

        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT approval_id, capability, action_name, arguments_json, reason,
                   status, created_utc, resolved_utc, resolution_note
            FROM agent_approvals
            WHERE ($status IS NULL OR status=$status)
            ORDER BY created_utc ASC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$status", string.IsNullOrWhiteSpace(status) ? DBNull.Value : status.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$limit", limit);

        var result = new List<ApprovalRequest>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(ReadApproval(reader));
        return result;
    }

    public async Task<ApprovalRequest?> ResolveApprovalAsync(
        string approvalId,
        bool approve,
        string? note = null,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        var now = DateTimeOffset.UtcNow;

        await using var db = Open();
        await db.OpenAsync(ct);
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                UPDATE agent_approvals
                SET status=$status, resolved_utc=$resolved, resolution_note=$note
                WHERE approval_id=$id AND status='pending';
                """;
            cmd.Parameters.AddWithValue("$status", approve ? "approved" : "denied");
            cmd.Parameters.AddWithValue("$resolved", now.ToString("O"));
            cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", approvalId);
            if (await cmd.ExecuteNonQueryAsync(ct) == 0) return null;
        }

        await AuditAsync("approval", "resolve", approve ? "approved" : "denied", new { approvalId, note }, ct);

        ApprovalRequest? resolved;
        await using (var query = db.CreateCommand())
        {
            query.CommandText = """
                SELECT approval_id, capability, action_name, arguments_json, reason,
                       status, created_utc, resolved_utc, resolution_note
                FROM agent_approvals
                WHERE approval_id=$id;
                """;
            query.Parameters.AddWithValue("$id", approvalId);
            await using var reader = await query.ExecuteReaderAsync(ct);
            resolved = await reader.ReadAsync(ct) ? ReadApproval(reader) : null;
        }

        if (approve && resolved is not null)
        {
            await _automation.EnqueueAgentJobAsync(
                $"approval:{approvalId}",
                $"Operator approved guarded action '{resolved.ActionName}'. Retry that action using exactly these approved arguments, then continue. Arguments: {resolved.ArgumentsJson}",
                "{}",
                ct);
        }

        return resolved;
    }

    public async Task AuditAsync(
        string category,
        string action,
        string outcome,
        object details,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agent_audit(category, action, outcome, details_json, created_utc)
            VALUES($category,$action,$outcome,$details,$created);
            """;
        cmd.Parameters.AddWithValue("$category", category);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$outcome", outcome);
        cmd.Parameters.AddWithValue("$details", JsonSerializer.Serialize(details));
        cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AuditEntry>> ReadAuditAsync(
        string? category = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        limit = Math.Clamp(limit, 1, 500);

        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT audit_id, category, action, outcome, details_json, created_utc
            FROM agent_audit
            WHERE ($category IS NULL OR category=$category)
            ORDER BY audit_id DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$category", string.IsNullOrWhiteSpace(category) ? DBNull.Value : category);
        cmd.Parameters.AddWithValue("$limit", limit);

        var result = new List<AuditEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new AuditEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5))));
        }

        return result;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeAsync(stoppingToken);
        var interval = TimeSpan.FromMilliseconds(ReadInt("AGENT_RUNTIME_TICK_MS", 1000, 250, 60_000));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDueSchedulesAsync(stoppingToken);
                await DeliverNotificationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent runtime background cycle failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RunDueSchedulesAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var db = Open();
        await db.OpenAsync(ct);

        var due = new List<AgentSchedule>();
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT schedule_id, name, prompt, run_at_utc, every_seconds, next_run_utc,
                       enabled, context_token_budget, created_utc, last_run_utc
                FROM agent_schedules
                WHERE enabled=1 AND next_run_utc<=$now
                ORDER BY next_run_utc
                LIMIT 20;
                """;
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) due.Add(ReadSchedule(reader));
        }

        foreach (var schedule in due)
        {
            var context = await _memory.BuildContextAsync(
                schedule.Prompt,
                namespaceHints: null,
                candidateLimit: 18,
                tokenBudget: schedule.ContextTokenBudget,
                maxItems: 7,
                minSimilarity: 0.15,
                includeMetadata: false,
                includeSuperseded: false,
                ct: ct);

            await _automation.EnqueueAgentJobAsync(
                $"schedule:{schedule.ScheduleId}",
                schedule.Prompt,
                JsonSerializer.Serialize(context),
                ct);

            DateTimeOffset? nextRun = null;
            if (schedule.EverySeconds is { } everySeconds)
            {
                // Keep recurrence anchored to the schedule rather than drifting from the time the worker happened to run.
                nextRun = schedule.NextRunUtc;
                do
                {
                    nextRun = nextRun.Value.AddSeconds(everySeconds);
                } while (nextRun <= now);
            }

            await using var update = db.CreateCommand();
            update.CommandText = """
                UPDATE agent_schedules
                SET last_run_utc=$now,
                    next_run_utc=COALESCE($next, next_run_utc),
                    enabled=$enabled
                WHERE schedule_id=$id;
                """;
            update.Parameters.AddWithValue("$now", now.ToString("O"));
            update.Parameters.AddWithValue("$next", (object?)nextRun?.ToString("O") ?? DBNull.Value);
            update.Parameters.AddWithValue("$enabled", nextRun is null ? 0 : 1);
            update.Parameters.AddWithValue("$id", schedule.ScheduleId);
            await update.ExecuteNonQueryAsync(ct);

            await AuditAsync("scheduler", "fire", "queued", new { schedule.ScheduleId, schedule.Name }, ct);
        }
    }

    private async Task DeliverNotificationsAsync(CancellationToken ct)
    {
        var pending = await ListNotificationsAsync("pending", afterId: 0, limit: 20, ct: ct);
        var maxAttempts = ReadInt("AGENT_NOTIFICATION_MAX_ATTEMPTS", 5, 1, 100);

        foreach (var notification in pending)
        {
            try
            {
                if (notification.Channel == "webhook")
                {
                    ValidateNotifyWebhook(notification.Destination);
                    using var request = new HttpRequestMessage(HttpMethod.Post, notification.Destination)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(new
                            {
                                notificationId = notification.NotificationId,
                                notification.Title,
                                notification.Body,
                                notification.CreatedUtc
                            }),
                            Encoding.UTF8,
                            "application/json")
                    };

                    var bearer = Environment.GetEnvironmentVariable("AGENT_NOTIFICATION_WEBHOOK_BEARER");
                    if (!string.IsNullOrWhiteSpace(bearer))
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

                    using var response = await _http.SendAsync(request, ct);
                    response.EnsureSuccessStatusCode();
                }
                else if (notification.Channel == "telegram")
                {
                    await SendTelegramNotificationAsync(notification, ct);
                }

                await SetNotificationStatusAsync(notification.NotificationId, "delivered", null, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var nextStatus = notification.Attempts + 1 >= maxAttempts ? "failed" : "pending";
                await SetNotificationStatusAsync(notification.NotificationId, nextStatus, ex.Message, ct);
            }
        }
    }

    private async Task SetNotificationStatusAsync(
        long notificationId,
        string status,
        string? error,
        CancellationToken ct)
    {
        await using var db = Open();
        await db.OpenAsync(ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            UPDATE agent_notifications
            SET status=$status,
                attempts=attempts+1,
                error_text=$error,
                delivered_utc=CASE WHEN $status='delivered' THEN $delivered ELSE delivered_utc END
            WHERE notification_id=$id;
            """;
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$delivered", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$id", notificationId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task SeedCapabilitiesAsync(SqliteConnection db, CancellationToken ct)
    {
        var updated = DateTimeOffset.UtcNow.ToString("O");
        var defaults = new[]
        {
            (Capability: "memory.read", Enabled: 1, Approval: 0, Note: "Read durable memory"),
            (Capability: "memory.write", Enabled: 1, Approval: 0, Note: "Write durable memory"),
            (Capability: "scheduler", Enabled: 1, Approval: 1, Note: "Create autonomous future work"),
            (Capability: "notifications", Enabled: 1, Approval: 0, Note: "Create inbox notifications"),
            (Capability: "http", Enabled: 0, Approval: 1, Note: "External HTTP GET access"),
            (Capability: "github", Enabled: 0, Approval: 1, Note: "GitHub REST read access"),
            (Capability: "filesystem", Enabled: 0, Approval: 1, Note: "Read/write dedicated agent workspace"),
            (Capability: "shell", Enabled: 0, Approval: 1, Note: "Shell/process execution in agent workspace"),
            (Capability: "email.send", Enabled: 0, Approval: 1, Note: "Sending email via an external host/connector")
        };

        foreach (var item in defaults)
        {
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO agent_capabilities(
                    capability, enabled, requires_approval, note, updated_utc)
                VALUES($capability,$enabled,$approval,$note,$updated);
                """;
            cmd.Parameters.AddWithValue("$capability", item.Capability);
            cmd.Parameters.AddWithValue("$enabled", item.Enabled);
            cmd.Parameters.AddWithValue("$approval", item.Approval);
            cmd.Parameters.AddWithValue("$note", item.Note);
            cmd.Parameters.AddWithValue("$updated", updated);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private SqliteConnection Open() => new(
        $"Data Source={_memory.DatabasePath};Cache=Shared;Mode=ReadWriteCreate;Pooling=True");

    private static string NormalizeCapability(string value)
    {
        value = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            throw new ArgumentException("Invalid capability.", nameof(value));
        return value;
    }

    private static int ReadInt(string name, int fallback, int min, int max) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value)
            ? Math.Clamp(value, min, max)
            : fallback;

    private static void ValidateNotifyWebhook(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) ||
            !Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Valid webhook destination required.", nameof(raw));
        }

        var allowlist = (Environment.GetEnvironmentVariable("AGENT_NOTIFICATION_WEBHOOK_ALLOWLIST") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowlist.Length == 0 ||
            !allowlist.Any(host =>
                uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Notification webhook host is not allow-listed.");
        }
    }

    private async Task SendTelegramNotificationAsync(AgentNotification notification, CancellationToken ct)
    {
        var token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("TELEGRAM_BOT_TOKEN is required for telegram notifications.");

        var chatId = ValidateTelegramDestination(notification.Destination);
        var text = string.IsNullOrWhiteSpace(notification.Title)
            ? notification.Body
            : $"{notification.Title}\n\n{notification.Body}";

        foreach (var chunk in SplitTelegramText(text))
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri($"https://api.telegram.org/bot{token}/sendMessage"))
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        chat_id = chatId,
                        text = chunk,
                        disable_web_page_preview = true
                    }),
                    Encoding.UTF8,
                    "application/json")
            };

            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
        }
    }

    private static string ValidateTelegramDestination(string? raw)
    {
        var chatId = string.IsNullOrWhiteSpace(raw)
            ? Environment.GetEnvironmentVariable("TELEGRAM_DEFAULT_CHAT_ID")
            : raw;
        chatId = (chatId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(chatId))
            throw new ArgumentException("Telegram chat ID destination is required.", nameof(raw));

        var allowed = (Environment.GetEnvironmentVariable("TELEGRAM_ALLOWED_CHAT_IDS") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        if (!allowed.Contains(chatId))
            throw new InvalidOperationException("Telegram destination chat is not allow-listed.");

        return chatId;
    }

    private static IReadOnlyList<string> SplitTelegramText(string text)
    {
        const int max = 3900;
        text = string.IsNullOrWhiteSpace(text) ? "(empty response)" : text.Trim();
        var chunks = new List<string>();
        for (var i = 0; i < text.Length; i += max)
            chunks.Add(text.Substring(i, Math.Min(max, text.Length - i)));
        return chunks;
    }

    private static string CanonicalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonicalJson(document.RootElement, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalJson(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(property.Value, writer);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonicalJson(item, writer);
                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static MemoryEvidence ReadEvidence(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetInt64(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.GetDouble(7),
        DateTimeOffset.Parse(reader.GetString(8)),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        DateTimeOffset.Parse(reader.GetString(10)));

    private static AgentSchedule ReadSchedule(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3)),
        reader.IsDBNull(4) ? null : reader.GetInt32(4),
        DateTimeOffset.Parse(reader.GetString(5)),
        reader.GetInt64(6) != 0,
        reader.GetInt32(7),
        DateTimeOffset.Parse(reader.GetString(8)),
        reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)));

    private static AgentNotification ReadNotification(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetInt32(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        DateTimeOffset.Parse(reader.GetString(8)),
        reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)));

    private static ApprovalRequest ReadApproval(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetString(5),
        DateTimeOffset.Parse(reader.GetString(6)),
        reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)),
        reader.IsDBNull(8) ? null : reader.GetString(8));
}

public sealed record AgentCapability(
    string Capability,
    bool Enabled,
    bool RequiresApproval,
    string? Note,
    DateTimeOffset UpdatedUtc);

public sealed record CapabilityDecision(
    bool Allowed,
    bool RequiresApproval,
    string? ApprovalId,
    string? Reason);

public sealed record MemoryEvidence(
    string EvidenceId,
    string Namespace,
    string Key,
    long MemoryVersion,
    string SourceType,
    string? SourceRef,
    string? SourceLabel,
    double Confidence,
    DateTimeOffset ObservedUtc,
    string? EvidenceText,
    DateTimeOffset CreatedUtc);

public sealed record AgentSchedule(
    string ScheduleId,
    string Name,
    string Prompt,
    DateTimeOffset? RunAtUtc,
    int? EverySeconds,
    DateTimeOffset NextRunUtc,
    bool Enabled,
    int ContextTokenBudget,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastRunUtc);

public sealed record AgentNotification(
    long NotificationId,
    string Channel,
    string? Destination,
    string Title,
    string Body,
    string Status,
    int Attempts,
    string? ErrorText,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? DeliveredUtc);

public sealed record ApprovalRequest(
    string ApprovalId,
    string Capability,
    string ActionName,
    string ArgumentsJson,
    string? Reason,
    string Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ResolvedUtc,
    string? ResolutionNote);

public sealed record AuditEntry(
    long AuditId,
    string Category,
    string Action,
    string Outcome,
    string DetailsJson,
    DateTimeOffset CreatedUtc);
