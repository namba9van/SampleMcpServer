using System.ComponentModel;
using ModelContextProtocol.Server;
/// <summary>
/// Предоставляет генерацию случайных чисел как MCP-инструмент.
/// </summary>
public class RandomNumberTools
{
    /// <summary>
    /// Генерирует псевдослучайное целое число в заданном диапазоне.
    /// </summary>
    /// <param name="min">Включительная нижняя граница.</param>
    /// <param name="max">Исключительная верхняя граница.</param>
    /// <returns>Псевдослучайное целое число в запрошенном диапазоне.</returns>
    [McpServerTool]
    [Description("Генерирует случайное целое число от включительной нижней границы до исключительной верхней границы.")]

    public int GetRandomNumber(
        [Description("Включительная нижняя граница.")] int min = 0,
        [Description("Исключительная верхняя граница.")] int max = 100)
    {
        return Random.Shared.Next(min, max);
    }
}
