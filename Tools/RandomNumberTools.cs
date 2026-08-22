using System.ComponentModel;
using ModelContextProtocol.Server;
/// <summary>
/// Provides random number generation as an MCP tool.
/// </summary>
public class RandomNumberTools
{
    /// <summary>
    /// Generates a random integer in the half-open interval [<paramref name="min"/>, <paramref name="max"/>).
    /// </summary>
    /// <param name="min">The inclusive lower bound.</param>
    /// <param name="max">The exclusive upper bound.</param>
    /// <returns>A pseudo-random integer in the requested range.</returns>
    [McpServerTool]
    [Description("Generates a random integer from the inclusive minimum to the exclusive maximum.")]

    public int GetRandomNumber(
        [Description("Inclusive lower bound.")] int min = 0,
        [Description("Exclusive upper bound.")] int max = 100)
    {
        return Random.Shared.Next(min, max);
    }
}
