using System.Security.Cryptography;
using System.Text;

namespace Services;

/// <summary>
/// Creates stable fingerprints used to detect changes to indexed files.
/// </summary>
public static class RagFileFingerprint
{
    /// <summary>
    /// Creates a SHA-256 fingerprint from the file path, length, and UTC modification timestamp.
    /// </summary>
    /// <param name="filePath">The path of the file to fingerprint.</param>
    /// <returns>The hexadecimal SHA-256 fingerprint.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
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
