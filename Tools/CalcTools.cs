using System.ComponentModel;
using ModelContextProtocol.Server;

public sealed class CalcTools
{
    [McpServerTool(Name = "calc_add")]
    [Description("Adds two numbers.")]
    public double Add(double a, double b) => a + b;

    [McpServerTool(Name = "calc_subtract")]
    [Description("Subtracts b from a.")]
    public double Subtract(double a, double b) => a - b;

    [McpServerTool(Name = "calc_multiply")]
    [Description("Multiplies two numbers.")]
    public double Multiply(double a, double b) => a * b;

    [McpServerTool(Name = "calc_divide")]
    [Description("Divides a by b.")]
    public double Divide(double a, double b)
    {
        if (b == 0) throw new DivideByZeroException();
        return a / b;
    }

    [McpServerTool(Name = "calc_power")]
    [Description("Raises a number to a power.")]
    public double Power(double value, double power) => Math.Pow(value, power);

    [McpServerTool(Name = "calc_sqrt")]
    [Description("Returns the square root of a non-negative number.")]
    public double SquareRoot(double value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        return Math.Sqrt(value);
    }
}
