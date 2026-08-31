using System.ComponentModel;
using ModelContextProtocol.Server;
/// <summary>
/// Предоставляет random число generation as один MCP инструмент.
/// </summary>
public class RandomNumberTools
{
    /// <summary>
    /// Описывает назначение элемента.
    /// </summary>
    /// <param name="min">включающий lower bound.</param>
    /// <param name="max">исключающий upper bound.</param>
    /// <returns>pseudo-random integer в запрошенный range.</returns>
    [McpServerTool]
    [Description("Генерирует случайное целое число от включительной нижней границы до исключительной верхней границы.")]

    public int GetRandomNumber(
        [Description("Включительная нижняя граница.")] int min = 0,
        [Description("Исключительная верхняя граница.")] int max = 100)
    {
        return Random.Shared.Next(min, max);
    }
}
