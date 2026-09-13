using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Services;

public sealed class TriggerActionTools
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "trigger_action_create")]
    [Description("Binds an existing durable memory trigger to an action. queue creates a durable model-turn job for the embedded or an external Agent Host; webhook POSTs to an allow-listed host; model calls a configured OpenAI-compatible model directly in the background.")]
    public async Task<string> TriggerActionCreate(
        [Description("Existing events_watch trigger ID.")] string triggerId,
        [Description("Action mode: queue, webhook, or model. Prefer queue when a real chat/agent host should execute tools and post a reply.")] string mode = "queue",
        [Description("Prompt template. Supports {{event}}, {{eventId}}, {{operation}}, {{namespace}}, {{key}}, {{content}}.")] string? promptTemplate = null,
        [Description("Only for webhook mode. Host must be listed in TRIGGER_WEBHOOK_ALLOWLIST.")] string? webhookUrl = null,
        [Description("Approximate memory_context budget attached to the action.")] int contextTokenBudget = 1800,
        [Description("Maximum model output for direct model mode.")] int maxOutputTokens = 700,
        [Description("Persist successful action output back into durable memory.")] bool rememberResult = true,
        [Description("Namespace for persisted action results.")] string resultNamespace = "agent_actions",
        TriggerAutomationService automation = null!, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await automation.CreateActionAsync(triggerId, mode, promptTemplate, webhookUrl,
            contextTokenBudget, maxOutputTokens, rememberResult, resultNamespace, cancellationToken), JsonOptions);

    [McpServerTool(Name = "trigger_action_list")]
    [Description("Lists trigger-to-action bindings.")]
    public async Task<string> TriggerActionList(TriggerAutomationService automation = null!, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await automation.ListActionsAsync(cancellationToken), JsonOptions);

    [McpServerTool(Name = "trigger_action_delete")]
    [Description("Deletes a trigger-to-action binding. Existing run history is retained.")]
    public async Task<string> TriggerActionDelete(
        [Description("Action ID.")] string actionId,
        TriggerAutomationService automation = null!, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(new { actionId, deleted = await automation.DeleteActionAsync(actionId, cancellationToken) }, JsonOptions);

    [McpServerTool(Name = "trigger_jobs_poll")]
    [Description("Polls durable trigger action runs/jobs. An external agent host should poll status=pending for mode=queue, claim a job, open/continue the desired model conversation, execute tools if needed, then complete the job.")]
    public async Task<string> TriggerJobsPoll(
        [Description("Return runs with runId greater than this value.")] long afterRunId = 0,
        [Description("Optional status filter: pending, claimed, running, completed, failed.")] string? status = null,
        [Description("Maximum jobs, 1..500.")] int limit = 100,
        TriggerAutomationService automation = null!, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await automation.PollJobsAsync(afterRunId, status, limit, cancellationToken), JsonOptions);

    [McpServerTool(Name = "trigger_jobs_wait")]
    [Description("Long-polls durable trigger action runs/jobs, useful for an agent host that should wake promptly when a trigger fires.")]
    public async Task<string> TriggerJobsWait(
        [Description("Return runs with runId greater than this value.")] long afterRunId = 0,
        [Description("Optional status filter.")] string? status = "pending",
        [Description("Maximum wait, 1..300 seconds.")] int timeoutSeconds = 30,
        [Description("Maximum jobs returned.")] int limit = 100,
        TriggerAutomationService automation = null!, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await automation.WaitJobsAsync(afterRunId, status, timeoutSeconds, limit, cancellationToken), JsonOptions);

    [McpServerTool(Name = "trigger_job_claim")]
    [Description("Atomically claims one queued trigger job so that only one host/worker handles it.")]
    public async Task<string> TriggerJobClaim(
        [Description("Queued run ID.")] long runId,
        [Description("Stable worker/host identifier used for lease ownership.")] string workerId = "external",
        [Description("Lease duration in seconds, 30..3600.")] int leaseSeconds = 180,
        TriggerAutomationService automation = null!, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(await automation.ClaimQueuedJobAsync(runId, workerId, leaseSeconds, cancellationToken), JsonOptions);

    [McpServerTool(Name = "trigger_job_complete")]
    [Description("Completes a claimed queued trigger job after the host/model has handled it; optionally persists the result into memory.")]
    public async Task<string> TriggerJobComplete(
        [Description("Run ID.")] long runId,
        [Description("Final model/agent result or delivery summary.")] string resultText,
        [Description("Persist the result if the action binding also permits remembering results.")] bool rememberResult = true,
        [Description("Worker identifier that owns the current lease. Use the same value passed to trigger_job_claim.")] string workerId = "external",
        TriggerAutomationService automation = null!, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(new { runId, completed = await automation.CompleteQueuedJobAsync(runId, resultText, rememberResult, workerId, cancellationToken) }, JsonOptions);
    [McpServerTool(Name = "trigger_job_renew")]
    [Description("Renews the lease for a claimed queued trigger job. External hosts should call this before the current lease expires during long-running work.")]
    public async Task<string> TriggerJobRenew(
        [Description("Run ID.")] long runId,
        [Description("Worker identifier that owns the current lease.")] string workerId = "external",
        [Description("New lease duration in seconds, 30..3600.")] int leaseSeconds = 180,
        TriggerAutomationService automation = null!, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(new { runId, renewed = await automation.RenewQueuedJobLeaseAsync(runId, workerId, leaseSeconds, cancellationToken) }, JsonOptions);

    [McpServerTool(Name = "trigger_job_fail")]
    [Description("Marks a claimed queued trigger job as failed. The worker identifier must own the current lease.")]
    public async Task<string> TriggerJobFail(
        [Description("Run ID.")] long runId,
        [Description("Failure summary suitable for logs and diagnostics.")] string error,
        [Description("Worker identifier that owns the current lease.")] string workerId = "external",
        TriggerAutomationService automation = null!, CancellationToken cancellationToken = default)
        => JsonSerializer.Serialize(new { runId, failed = await automation.FailQueuedJobAsync(runId, error, workerId, cancellationToken) }, JsonOptions);

}
