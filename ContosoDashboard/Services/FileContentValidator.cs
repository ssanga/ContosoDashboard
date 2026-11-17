namespace ContosoDashboard.Services;

public record FileValidationResult(bool IsValid, string? ErrorMessage)
{
    public static FileValidationResult Ok() => new(true, null);
    public static FileValidationResult Fail(string message) => new(false, message);
}

/// <summary>
/// Local, offline content check (not a replacement for a production antivirus scanner):
/// allow-listed extension, size limit, magic number matching the extension, text without NUL
/// bytes, and rejection of executable signatures regardless of extension.
/// </summary>
public interface IFileContentValidator
{
    Task<FileValidationResult> ValidateAsync(string fileName, string declaredContentType, Stream content, long length);
}

public class FileContentValidator : IFileContentValidator
{
    public const long MaxFileSizeBytes = 26_214_400; // 25 MB

    public const string SupportedTypesText = "PDF, Word, Excel, PowerPoint, text (.txt), JPEG and PNG";

    private static readonly byte[] Pdf = { 0x25, 0x50, 0x44, 0x46 };                 // %PDF
    private static readonly byte[] Zip = { 0x50, 0x4B, 0x03, 0x04 };                 // PK..
    private static readonly byte[] OleCompound = { 0xD0, 0xCF, 0x11, 0xE0 };         // legacy Office
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47 };

    private static readonly Dictionary<string, byte[]?> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = Pdf,
        [".docx"] = Zip,
        [".xlsx"] = Zip,
        [".pptx"] = Zip,
        [".doc"] = OleCompound,
        [".xls"] = OleCompound,
        [".ppt"] = OleCompound,
        [".jpg"] = Jpeg,
        [".jpeg"] = Jpeg,
        [".png"] = Png,
        [".txt"] = null // text: no signature, checked for NUL bytes instead
    };

    // Dangerous signatures rejected whatever the extension
    private static readonly (string Name, byte[] Signature)[] DangerousSignatures =
    {
        ("Windows executable", new byte[] { 0x4D, 0x5A }),          // MZ
        ("Linux executable", new byte[] { 0x7F, 0x45, 0x4C, 0x46 }), // ELF
        ("script", new byte[] { 0x23, 0x21 })                        // #!
    };

    public static bool IsSupportedExtension(string extension) => AllowedExtensions.ContainsKey(extension);

    public async Task<FileValidationResult> ValidateAsync(string fileName, string declaredContentType, Stream content, long length)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        var extension = Path.GetExtension(name);

        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.TryGetValue(extension, out var expectedSignature))
            return FileValidationResult.Fail($"'{name}': file type not supported. Supported types: {SupportedTypesText}.");

        if (length <= 0)
            return FileValidationResult.Fail($"'{name}': the file is empty.");

        if (length > MaxFileSizeBytes)
            return FileValidationResult.Fail($"'{name}': the file exceeds the 25 MB size limit.");

        if (!content.CanSeek)
            return FileValidationResult.Fail($"'{name}': the file could not be checked.");

        var originalPosition = content.Position;
        try
        {
            content.Position = 0;

            var header = new byte[8];
            var read = await ReadAtLeastAsync(content, header);

            foreach (var (dangerName, signature) in DangerousSignatures)
            {
                if (StartsWith(header, read, signature))
                    return FileValidationResult.Fail($"'{name}': the file content looks like a {dangerName} and was rejected.");
            }

            if (expectedSignature != null)
            {
                if (!StartsWith(header, read, expectedSignature))
                    return FileValidationResult.Fail($"'{name}': the file content does not match its '{extension}' type.");
            }
            else if (await ContainsNulByteAsync(content))
            {
                return FileValidationResult.Fail($"'{name}': the text file contains binary content.");
            }

            return FileValidationResult.Ok();
        }
        finally
        {
            content.Position = originalPosition;
        }
    }

    private static async Task<int> ReadAtLeastAsync(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total));
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static bool StartsWith(byte[] data, int dataLength, byte[] signature)
    {
        if (dataLength < signature.Length) return false;
        for (var i = 0; i < signature.Length; i++)
            if (data[i] != signature[i]) return false;
        return true;
    }

    private static async Task<bool> ContainsNulByteAsync(Stream stream)
    {
        stream.Position = 0;
        var buffer = new byte[81920];
        int n;
        while ((n = await stream.ReadAsync(buffer)) > 0)
        {
            if (Array.IndexOf(buffer, (byte)0, 0, n) >= 0) return true;
        }
        return false;
    }
}
