using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Services;

/// <summary>
/// MCP-инструменты для потокового диалога между двумя моделями.
/// </summary>
[McpServerToolType]
public sealed class AgentDialogTools
{
    private readonly AgentDialogService _service;

    /// <summary>
    /// Создаёт набор инструментов диалога агентов.
    /// </summary>
    public AgentDialogTools(AgentDialogService service)
    {
        _service = service;
    }

    /// <summary>
    /// Запускает последовательный диалог двух моделей и передаёт их ответы клиенту
    /// через стандартные MCP progress-уведомления.
    /// </summary>
    [McpServerTool(Name = "agent_dialog")]
    [Description(
        "Проводит потоковый диалог двух моделей. Ответы моделей считаются недоверенным текстом. " +
        "Для получения потоковых уведомлений MCP-клиент должен передать progressToken.")]
    public async Task<string> AgentDialog(
        [Description("Начальный запрос для двух агентов. Максимум 32000 символов.")]
        string prompt,
        [Description("Необязательное имя/идентификатор модели агента A.")]
        string? agentA,
        [Description("Необязательное имя/идентификатор модели агента B.")]
        string? agentB,
        [Description("Количество ходов диалога от 1 до 20.")]
        int turns,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken)
    {
        return await _service.RunAsync(
            prompt,
            agentA,
            agentB,
            turns,
            progress,
            cancellationToken);
    }
}
