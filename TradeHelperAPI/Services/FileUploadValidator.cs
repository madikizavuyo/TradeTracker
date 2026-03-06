namespace TradeHelper.Services;

/// <summary>
/// Validates file uploads for path traversal and content-type spoofing.
/// </summary>
public static class FileUploadValidator
{
    
    /// <summary>
    /// Rejects filenames containing path traversal or invalid characters.
    /// </summary>
    public static bool IsFileNameSafe(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        if (fileName.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
        if (fileName.IndexOfAny(new[] { ':', '\\', '/', '\0' }) >= 0) return false;
        var nameOnly = Path.GetFileName(fileName);
        return !string.IsNullOrEmpty(nameOnly) && nameOnly == fileName.Trim();
    }

    /// <summary>
    /// Validates file content matches expected format via magic bytes.
    /// </summary>
    public static async Task<bool> ValidateContentAsync(Stream stream, string extension, CancellationToken ct = default)
    {
        var buffer = new byte[8];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
        stream.Position = 0;
        if (read < 4) return false;

        return extension.ToLowerInvariant() switch
        {
            ".csv" => IsValidCsv(buffer, read),
            ".xlsx" or ".xls" => IsValidExcel(buffer, read),
            ".pdf" => IsValidPdf(buffer, read),
            _ => false
        };
    }

    private static bool IsValidCsv(byte[] buffer, int len)
    {
        // CSV is text - allow UTF-8 BOM or printable ASCII/UTF-8
        if (len >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
            return true;
        for (var i = 0; i < Math.Min(len, 4); i++)
        {
            var b = buffer[i];
            if (b < 0x20 && b != '\t' && b != '\n' && b != '\r') return false;
            if (b > 0x7E && b < 0xC0) return false; // Invalid UTF-8 lead
        }
        return true;
    }

    private static bool IsValidExcel(byte[] buffer, int len)
    {
        // .xlsx is ZIP: 50 4B 03 04 or 50 4B 05 06
        if (len >= 4 && buffer[0] == 0x50 && buffer[1] == 0x4B)
            return true;
        // .xls is OLE: D0 CF 11 E0
        if (len >= 4 && buffer[0] == 0xD0 && buffer[1] == 0xCF && buffer[2] == 0x11 && buffer[3] == 0xE0)
            return true;
        return false;
    }

    private static bool IsValidPdf(byte[] buffer, int len)
    {
        return len >= 4 && buffer[0] == 0x25 && buffer[1] == 0x50 && buffer[2] == 0x44 && buffer[3] == 0x46;
    }
}
