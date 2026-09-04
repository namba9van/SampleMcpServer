using System.ComponentModel;
using ModelContextProtocol.Server;
/// <summary>
/// Предоставляет базовые арифметические операции как MCP-инструменты.
/// </summary>
public class CalcTools
{
    /// <summary>
    /// Складывает два числа.
    /// </summary>
    /// <param name="a">Первое слагаемое.</param>
    /// <param name="b">Второе слагаемое.</param>
    /// <returns>Сумма двух чисел.</returns>
    [McpServerTool, Description("Складывает два числа и возвращает их сумму.")]

    public static double Add(
        [Description("Первое число.")] double a,
        [Description("Второе число.")] double b)
    {
        return a + b;
    }
    /// <summary>
    /// Вычитает второе число из первого.
    /// </summary>
    /// <param name="a">Уменьшаемое.</param>
    /// <param name="b">Вычитаемое.</param>
    /// <returns>Разность двух чисел.</returns>
    [McpServerTool, Description("Вычитает второе число из первого и возвращает разность.")]

    public static double Subtract(
        [Description("Первое число.")] double a,
        [Description("Второе число.")] double b)
    {
        return a - b;
    }
    /// <summary>
    /// Умножает два числа.
    /// </summary>
    /// <param name="a">Первый множитель.</param>
    /// <param name="b">Второй множитель.</param>
    /// <returns>Произведение двух чисел.</returns>
    [McpServerTool, Description("Умножает два числа и возвращает произведение.")]

    public static double Multiply(
        [Description("Первое число.")] double a,
        [Description("Второе число.")] double b)
    {
        return a * b;
    }
    /// <summary>
    /// Делит первое число на второе.
    /// </summary>
    /// <param name="a">Делимое.</param>
    /// <param name="b">Делитель. Должен быть отличен от нуля.</param>
    /// <returns>Частное двух чисел.</returns>
    /// <exception cref="ArgumentException">Возникает, когда <paramref name="b"/> равен нулю.</exception>
    [McpServerTool, Description("Делит первое число на второе. Делитель не должен быть равен нулю.")]

    public static double Divide(
        [Description("Делимое.")] double a,
        [Description("Делитель. Не должен быть равен нулю.")] double b)
    {
        if (b == 0)
            throw new ArgumentException("Нельзя делить на ноль.");

        return a / b;
    }
    /// <summary>
    /// Возводит число в указанную степень.
    /// </summary>
    /// <param name="baseNumber">Основание.</param>
    /// <param name="exponent">Показатель степени.</param>
    /// <returns>Результат возведения в степень.</returns>
    [McpServerTool, Description("Возводит основание в указанную степень.")]

    public static double Power(
        [Description("Основание.")] double baseNumber,
        [Description("Показатель степени.")] double exponent)
    {
        return Math.Pow(baseNumber, exponent);
    }
    /// <summary>
    /// Вычисляет неотрицательный квадратный корень из числа.
    /// </summary>
    /// <param name="number">Число, для которого требуется вычислить квадратный корень.</param>
    /// <returns>Квадратный корень из <paramref name="number"/>.</returns>
    /// <exception cref="ArgumentException">Возникает, когда <paramref name="number"/> отрицательное.</exception>
    [McpServerTool, Description("Вычисляет квадратный корень неотрицательного числа.")]

    public static double SquareRoot([Description("Неотрицательное число, для которого требуется вычислить квадратный корень.")] double number)
    {
        if (number < 0)
            throw new ArgumentException("Нельзя вычислить квадратный корень из отрицательного числа.");

        return Math.Sqrt(number);
    }

}
