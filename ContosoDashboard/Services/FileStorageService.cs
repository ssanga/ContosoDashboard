namespace ContosoDashboard.Services;

/// <summary>
/// Storage abstraction. The caller supplies a relative path of the form
/// {uploaderId}/{projectId|personal}/{guid}.{ext}; a user-supplied file name never reaches storage.
/// A cloud implementation (e.g. Azure Blob Storage) can replace the local one through DI.
/// </summary>
public interface IFileStorageService
{
    Task<string> UploadAsync(Stream content, string relativePath, string contentType);
    Task<Stream> DownloadAsync(string relativePath);
    Task DeleteAsync(string relativePath);
    Task<string> GetUrlAsync(string relativePath, TimeSpan expiration);
}

/// <summary>Stores files under DocumentStorage:RootPath, outside wwwroot.</summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public LocalFileStorageService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["DocumentStorage:RootPath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine("AppData", "uploads");

        _rootPath = Path.GetFullPath(
            Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured));
    }

    public async Task<string> UploadAsync(Stream content, string relativePath, string contentType)
    {
        var fullPath = ResolveSafePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // CreateNew: never overwrite an existing file
        await using var target = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(target);
        return relativePath;
    }

    public Task<Stream> DownloadAsync(string relativePath)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Stored file not found.");

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath)
    {
        var fullPath = ResolveSafePath(relativePath);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<string> GetUrlAsync(string relativePath, TimeSpan expiration)
    {
        // Local files have no direct URL: they are served only through the authorized
        // /documents/{id}/download and /documents/{id}/preview endpoints.
        throw new NotSupportedException("Local storage has no direct URLs; use the authorized document endpoints.");
    }

    private string ResolveSafePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("Invalid storage path.", nameof(relativePath));

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        var rootWithSeparator = _rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid storage path.", nameof(relativePath));

        return fullPath;
    }
}
