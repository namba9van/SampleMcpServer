using System.ComponentModel;
using ModelContextProtocol.Server;

public sealed class RandomNumberTools
{
    [McpServerTool(Name = "random_number")]
    [Description("Generates a random integer between the specified minimum (inclusive) and maximum (exclusive).")]
    public int GetRandomNumber(
        [Description("Minimum value, inclusive.")] int min = 0,
        [Description("Maximum value, exclusive.")] int max = 100)
    {
        if (max <= min)
            throw new ArgumentOutOfRangeException(nameof(max), "max must be greater than min.");
        return Random.Shared.Next(min, max);
    }
}
