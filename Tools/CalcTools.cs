using System.ComponentModel;
using ModelContextProtocol.Server;
/// <summary>
/// Описывает назначение элемента.
/// </summary>
public class CalcTools
{
    /// <summary>
    /// Добавляет два numbers.
    /// </summary>
    /// <param name="a">первый addend.</param>
    /// <param name="b">второй addend.</param>
    /// <returns>sum из два operands.</returns>
    [McpServerTool, Description("Складывает два числа и возвращает их сумму.")]

    public static double Add(
        [Description("Первое число.")] double a,
        [Description("Второе число.")] double b)
    {
        return a + b;
    }
    /// <summary>
    /// Subtracts второй число из первый.
    /// </summary>
    /// <param name="a">minuend.</param>
    /// <param name="b">subtrahend.</param>
    /// <returns>difference between два operands.</returns>
    [McpServerTool, Description("Вычитает второе число из первого и возвращает разность.")]

    public static double Subtract(
        [Description("Первое число.")] double a,
        [Description("Второе число.")] double b)
    {
        return a - b;
    }
    /// <summary>
    /// Описывает назначение элемента.
    /// </summary>
    /// <param name="a">первый factor.</param>
    /// <param name="b">второй factor.</param>
    /// <returns>product из два operands.</returns>
    [McpServerTool, Description("Умножает два числа и возвращает произведение.")]

    public static double Multiply(
        [Description("Первое число.")] double a,
        [Description("Второе число.")] double b)
    {
        return a * b;
    }
    /// <summary>
    /// Divides первый число по второй.
    /// </summary>
    /// <param name="a">dividend.</param>
    /// <param name="b">делитель. It должен быть non-zero.</param>
    /// <returns>quotient из два operands.</returns>
    /// <exception cref="ArgumentException">возникает когда <paramref name="b"/> является ноль.</exception>
    [McpServerTool, Description("Делит первое число на второе. Делитель не должен быть равен нулю.")]

    public static double Divide(
        [Description("Делимое.")] double a,
        [Description("Делитель. Не должен быть равен нулю.")] double b)
    {
        if (b == 0)
            throw new ArgumentException("Cannot divide by zero");

        return a / b;
    }
    /// <summary>
    /// повышает один число для specified exponent.
    /// </summary>
    /// <param name="baseNumber">base значение.</param>
    /// <param name="exponent">exponent.</param>
    /// <returns>calculated power.</returns>
    [McpServerTool, Description("Возводит основание в указанную степень.")]

    public static double Power(
        [Description("Основание.")] double baseNumber,
        [Description("Показатель степени.")] double exponent)
    {
        return Math.Pow(baseNumber, exponent);
    }
    /// <summary>
    /// Calculates неотрицательный squявляются корень из один число.
    /// </summary>
    /// <param name="number">число для которого squявляются корень является требуемый.</param>
    /// <returns>squявляются корень из <paramref name="number"/>.</returns>
    /// <exception cref="ArgumentException">возникает когда <paramref name="number"/> является отрицательный.</exception>
    [McpServerTool, Description("Вычисляет квадратный корень неотрицательного числа.")]

    public static double SquareRoot([Description("Неотрицательное число, для которого требуется вычислить квадратный корень.")] double number)
    {
        if (number < 0)
            throw new ArgumentException("Cannot calculate square root of negative number");

        return Math.Sqrt(number);
    }

}
