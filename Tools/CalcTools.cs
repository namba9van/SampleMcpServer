using System.ComponentModel;
using ModelContextProtocol.Server;
/// <summary>
/// Provides arithmetic operations exposed as MCP tools.
/// </summary>
public class CalcTools
{
    /// <summary>
    /// Adds two numbers.
    /// </summary>
    /// <param name="a">The first addend.</param>
    /// <param name="b">The second addend.</param>
    /// <returns>The sum of the two operands.</returns>
    [McpServerTool, Description("Adds two numbers and returns their sum.")]

    public static double Add(
        [Description("The first number.")] double a,
        [Description("The second number.")] double b)
    {
        return a + b;
    }
    /// <summary>
    /// Subtracts the second number from the first.
    /// </summary>
    /// <param name="a">The minuend.</param>
    /// <param name="b">The subtrahend.</param>
    /// <returns>The difference between the two operands.</returns>
    [McpServerTool, Description("Subtracts the second number from the first and returns the difference.")]

    public static double Subtract(
        [Description("The first number.")] double a,
        [Description("The second number.")] double b)
    {
        return a - b;
    }
    /// <summary>
    /// Multiplies two numbers.
    /// </summary>
    /// <param name="a">The first factor.</param>
    /// <param name="b">The second factor.</param>
    /// <returns>The product of the two operands.</returns>
    [McpServerTool, Description("Multiplies two numbers and returns the product.")]

    public static double Multiply(
        [Description("The first number.")] double a,
        [Description("The second number.")] double b)
    {
        return a * b;
    }
    /// <summary>
    /// Divides the first number by the second.
    /// </summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor. It must be non-zero.</param>
    /// <returns>The quotient of the two operands.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="b"/> is zero.</exception>
    [McpServerTool, Description("Divides the first number by the second. The divisor must be non-zero.")]

    public static double Divide(
        [Description("The dividend.")] double a,
        [Description("The divisor. Must not be zero.")] double b)
    {
        if (b == 0)
            throw new ArgumentException("Cannot divide by zero");

        return a / b;
    }
    /// <summary>
    /// Raises a number to the specified exponent.
    /// </summary>
    /// <param name="baseNumber">The base value.</param>
    /// <param name="exponent">The exponent.</param>
    /// <returns>The calculated power.</returns>
    [McpServerTool, Description("Raises a base number to the specified exponent.")]

    public static double Power(
        [Description("The base number.")] double baseNumber,
        [Description("The exponent.")] double exponent)
    {
        return Math.Pow(baseNumber, exponent);
    }
    /// <summary>
    /// Calculates the non-negative square root of a number.
    /// </summary>
    /// <param name="number">The number whose square root is required.</param>
    /// <returns>The square root of <paramref name="number"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="number"/> is negative.</exception>
    [McpServerTool, Description("Calculates the square root of a non-negative number.")]

    public static double SquareRoot([Description("The non-negative number whose square root is required.")] double number)
    {
        if (number < 0)
            throw new ArgumentException("Cannot calculate square root of negative number");

        return Math.Sqrt(number);
    }

}
