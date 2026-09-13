using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Services;

public sealed class RuntimeTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "agent_capabilities")]
    [Description("Lists autonomous-agent capabilities and whether each is enabled or requires approval.")]
    public async Task<string> AgentCapabilities(
        RuntimeGovernanceService runtime = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await runtime.ListCapabilitiesAsync(cancellationToken), JsonOptions);

    [McpServerTool(Name = "agent_capability_set")]
    [Description("Changes one autonomous-agent capability policy. Use only when the operator explicitly wants to change permissions.")]
    public async Task<string> AgentCapabilitySet(
        [Description("Capability name, for example memory.read, http, github, filesystem or shell.")] string capability,
        [Description("Whether the capability is enabled.")] bool enabled,
        [Description("Whether each matching autonomous action requires one-shot operator approval.")] bool requiresApproval = false,
        [Description("Optional operator note describing the policy.")] string? note = null,
        RuntimeGovernanceService runtime = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await runtime.SetCapabilityAsync(capability, enabled, requiresApproval, note, cancellationToken),
            JsonOptions);

    [McpServerTool(Name = "memory_evidence_add")]
    [Description("Attaches provenance/source evidence and confidence to an existing durable memory.")]
    public async Task<string> MemoryEvidenceAdd(
        string memoryNamespace,
        string key,
        string sourceType,
        string? sourceRef = null,
        string? sourceLabel = null,
        double confidence = 0.7,
        string? evidenceText = null,
        [Description("Optional ISO-8601 observation timestamp. Defaults to now.")] string? observedUtc = null,
        RuntimeGovernanceService runtime = null!,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset? observed = null;
        if (!string.IsNullOrWhiteSpace(observedUtc))
            observed = DateTimeOffset.Parse(observedUtc);

        return JsonSerializer.Serialize(
            await runtime.AddEvidenceAsync(
                memoryNamespace,
                key,
                sourceType,
                sourceRef,
                sourceLabel,
                confidence,
                evidenceText,
                observed,
                cancellationToken),
            JsonOptions);
    }

    [McpServerTool(Name = "memory_evidence_list")]
    [Description("Lists provenance/evidence for one memory, including source and confidence.")]
    public async Task<string> MemoryEvidenceList(
        string memoryNamespace,
        string key,
        int limit = 50,
        RuntimeGovernanceService runtime = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await runtime.ListEvidenceAsync(memoryNamespace, key, limit, cancellationToken),
            JsonOptions);

    [McpServerTool(Name = "agent_schedule_create")]
    [Description("Creates a one-shot or recurring autonomous job. Provide runAtUtc and/or everySeconds (minimum 60 seconds).")]
    public async Task<string> AgentScheduleCreate(
        string name,
        string prompt,
        [Description("Optional ISO-8601 timestamp with UTC offset.")] string? runAtUtc = null,
        [Description("Optional recurrence period in seconds; minimum 60.")] int? everySeconds = null,
        int contextTokenBudget = 1800,
        RuntimeGovernanceService runtime = null!,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset? runAt = null;
        if (!string.IsNullOrWhiteSpace(runAtUtc))
            runAt = DateTimeOffset.Parse(runAtUtc);

        var arguments = new { name, prompt, runAtUtc, everySeconds, contextTokenBudget };
        var decision = await runtime.CheckCapabilityAsync(
            "scheduler",
            "agent_schedule_create",
            arguments,
            createApproval: true,
            ct: cancellationToken);
        if (!decision.Allowed)
            return JsonSerializer.Serialize(decision, JsonOptions);

        return JsonSerializer.Serialize(
            await runtime.CreateScheduleAsync(name, prompt, runAt, everySeconds, contextTokenBudget, cancellationToken),
            JsonOptions);
    }

    [McpServerTool(Name = "agent_schedule_list")]
    [Description("Lists autonomous schedules.")]
    public async Task<string> AgentScheduleList(
        RuntimeGovernanceService runtime = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await runtime.ListSchedulesAsync(cancellationToken), JsonOptions);

    [McpServerTool(Name = "agent_schedule_set_enabled")]
    [Description("Enables or disables an autonomous schedule.")]
    public async Task<string> AgentScheduleSetEnabled(
        string scheduleId,
        bool enabled,
        RuntimeGovernanceService runtime = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            new
            {
                scheduleId,
                enabled,
                updated = await runtime.SetScheduleEnabledAsync(scheduleId, enabled, cancellationToken)
            },
            JsonOptions);

    [McpServerTool(Name = "agent_notify")]
    [Description("Creates a durable user notification. 'inbox' stays local; 'webhook' requires an allow-listed host; 'telegram' requires an allow-listed chat ID.")]
    public async Task<string> AgentNotify(
        string title,
        string body,
        string channel = "inbox",
        string? destination = null,
        RuntimeGovernanceService runtime = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await runtime.NotifyAsync(title, body, channel, destination, cancellationToken),
            JsonOptions);

    [McpServerTool(Name = "agent_notifications")]
    [Description("Reads durable agent notification/outbox entries.")]
    public async Task<string> AgentNotifications(
        string? status = null,
        long afterId = 0,
        int limit = 100,
        RuntimeGovernanceService runtime = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await runtime.ListNotificationsAsync(status, afterId, limit, cancellationToken),
            JsonOptions);

    [McpServerTool(Name = "agent_approvals")]
    [Description("Lists pending or historical operator approval requests for guarded autonomous actions.")]
    public async Task<string> AgentApprovals(
        string? status = "pending",
        int limit = 100,
        RuntimeGovernanceService runtime = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await runtime.ListApprovalsAsync(status, limit, cancellationToken),
            JsonOptions);

    [McpServerTool(Name = "agent_approval_resolve")]
    [Description("Approves or denies one pending autonomous action. This is an operator action; background agents must not self-approve.")]
    public async Task<string> AgentApprovalResolve(
        string approvalId,
        bool approve,
        string? note = null,
        RuntimeGovernanceService runtime = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await runtime.ResolveApprovalAsync(approvalId, approve, note, cancellationToken),
            JsonOptions);

    [McpServerTool(Name = "agent_audit")]
    [Description("Reads the technical audit trail for triggers, schedules, permissions, approvals, evidence and notifications.")]
    public async Task<string> AgentAudit(
        string? category = null,
        int limit = 100,
        RuntimeGovernanceService runtime = null!,
        CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(
            await runtime.ReadAuditAsync(category, limit, cancellationToken),
            JsonOptions);
}
