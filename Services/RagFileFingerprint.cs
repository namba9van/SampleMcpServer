using System.Security.Cryptography;
using System.Text;

namespace Services;

/// <summary>
/// Создаёт стабильный отпечатки используемый для обнаружить изменения для индексированный файлы.
/// </summary>
public static class RagFileFingerprint
{
    /// <summary>
    /// Создаёт один SHA-256 отпечаток из файл путь, размер, и UTC изменения временная метка.
    /// </summary>
    /// <param name="filePath">путь из файл для отпечаток.</param>
    /// <returns>шестнадцатеричный SHA-256 отпечаток.</returns>
    /// <exception cref="FileNotFoundException">возникает когда файл не существует.</exception>
    public static string Create(string filePath)
    {
        var fileInfo = new FileInfo(filePath);

        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException(
                "Файл не найден.",
                filePath);
        }

        var value =
            string.Join(
                "|",
                fileInfo.FullName,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc.Ticks);

        var hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(value));

        return Convert.ToHexString(hash);
    }
}
