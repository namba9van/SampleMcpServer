using System.Security.Cryptography;
using System.Text;

namespace Services;

public static class RagFileFingerprint
{
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