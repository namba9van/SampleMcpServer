using System.Security.Cryptography;
using System.Text;

namespace Services;

/// <summary>
/// Создаёт стабильный отпечаток, используемый для обнаружения изменений в индексированных файлах.
/// </summary>
public static class RagFileFingerprint
{
    /// <summary>
    /// Создаёт SHA-256-отпечаток из пути файла, его размера и метки времени последнего изменения (UTC).
    /// </summary>
    /// <param name="filePath">Путь к файлу, для которого вычисляется отпечаток.</param>
    /// <returns>Шестнадцатеричный SHA-256-отпечаток.</returns>
    /// <exception cref="FileNotFoundException">Возникает, когда файл не существует.</exception>
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
