/// <summary>
/// Предоставляет вспомогательные методы определения формата файла по его сигнатуре.
/// </summary>
public class FileUtils
{
    private static readonly Dictionary<string, List<byte[]>> fileSignatures = new()
    {
        { "txt", new List<byte[]>
            {
                new byte[] { 0xEF, 0xBB, 0xBF },
                new byte[] { 0xFF, 0xFE },
                new byte[] { 0xFE, 0xFF },
                new byte[] { 0x00, 0x00, 0xFE, 0xFF },
            }
        },
        { "gif", new List<byte[]> { new byte[] { 0x47, 0x49, 0x46, 0x38 } } },
        { "png", new List<byte[]> { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A } } },
        { "jpeg", new List<byte[]>
            {
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE2 },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE3 },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xEE },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xDB },
            }
        },
        { "jpeg2000", new List<byte[]> { new byte[] { 0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A } } },
        { "jpg", new List<byte[]>
            {
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE8 },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xEE },
                new byte[] { 0xFF, 0xD8, 0xFF, 0xDB },
            }
        },
        { "zip", new List<byte[]>
            {
                new byte[] { 0x50, 0x4B, 0x03, 0x04 },
                new byte[] { 0x50, 0x4B, 0x4C, 0x49, 0x54, 0x45 },
                new byte[] { 0x50, 0x4B, 0x53, 0x70, 0x58 },
                new byte[] { 0x50, 0x4B, 0x05, 0x06 },
                new byte[] { 0x50, 0x4B, 0x07, 0x08 },
                new byte[] { 0x57, 0x69, 0x6E, 0x5A, 0x69, 0x70 },
            }
        },
        { "pdf", new List<byte[]> { new byte[] { 0x25, 0x50, 0x44, 0x46 } } },
        { "z", new List<byte[]>
            {
                new byte[] { 0x1F, 0x9D },
                new byte[] { 0x1F, 0xA0 }
            }
        },
        { "tar", new List<byte[]>
            {
                new byte[] { 0x75, 0x73, 0x74, 0x61, 0x72, 0x00, 0x30, 0x30 },
                new byte[] { 0x75, 0x73, 0x74, 0x61, 0x72, 0x20, 0x20, 0x00 },
            }
        },
        { "tar.z", new List<byte[]>
            {
                new byte[] { 0x1F, 0x9D },
                new byte[] { 0x1F, 0xA0 }
            }
        },
        { "tif", new List<byte[]>
            {
                new byte[] { 0x49, 0x49, 0x2A, 0x00 },
                new byte[] { 0x4D, 0x4D, 0x00, 0x2A }
            }
        },
        { "tiff", new List<byte[]>
            {
                new byte[] { 0x49, 0x49, 0x2A, 0x00 },
                new byte[] { 0x4D, 0x4D, 0x00, 0x2A }
            }
        },
        { "rar", new List<byte[]>
            {
                new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 },
                new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 },
            }
        },
        { "7z", new List<byte[]>
            {
                new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C },
            }
        },
        { "mp3", new List<byte[]>
            {
                new byte[] { 0xFF, 0xFB },
                new byte[] { 0xFF, 0xF3 },
                new byte[] { 0xFF, 0xF2 },
                new byte[] { 0x49, 0x44, 0x43 },
            }
        },
    };
    /// <summary>
    /// Определяет известный тип файла по начальным байтам потока и восстанавливает позицию потока.
    /// </summary>
    /// <param name="reader">binary reader positioned в beginning из файл.</param>
    /// <returns>Определённое расширение без начальной точки или <see langword="null"/>, если сигнатура не распознана.</returns>
    public static string? GetFileExtensionFromHeader(BinaryReader reader)
    {
        int maxSignatureLength = fileSignatures.Values.Max(list => list.Max(arr => arr.Length));

        byte[] headerBytes = reader.ReadBytes(maxSignatureLength);

        reader.BaseStream.Seek(0, SeekOrigin.Begin);

        foreach (var signatureEntry in fileSignatures)
        {
            foreach (var signature in signatureEntry.Value)
            {
                if (headerBytes.Length >= signature.Length)
                {
                    if (headerBytes.Take(signature.Length).SequenceEqual(signature))
                    {
                        return signatureEntry.Key;
                    }
                }
            }
        }

        return null;
    }
    /// <summary>
    /// Определяет ли один файл starts с один распознаваемая сигнатура текстовой кодировки.
    /// </summary>
    /// <param name="fileName">путь из файл для проверить.</param>
    /// <returns><see langword="true"/> когда файл заголовок является recognized as текст; otherwise, <see langword="false"/>.</returns>
    public static bool IsPlainText(string fileName)
    {
        using (FileStream stream = File.Open(fileName, FileMode.Open))
        using (var reader = new BinaryReader(stream))
        {
            return GetFileExtensionFromHeader(reader) == "txt";
        }
    }
}
