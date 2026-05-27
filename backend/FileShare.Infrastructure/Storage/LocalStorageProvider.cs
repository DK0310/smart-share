using FileShare.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace FileShare.Infrastructure.Storage;

/// <summary>
/// Stores files on local disk under a configurable base path.
/// Used for development; swap to BlobStorageProvider for production.
/// </summary>
public class LocalStorageProvider : IStorageProvider
{
    private readonly string _basePath;

    public LocalStorageProvider(IConfiguration config)
    {
        _basePath = config["Storage:Local:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
    }

    public async Task<string> SaveAsync(IFormFile file, string code)
    {
        var directory = Path.Combine(_basePath, code);
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, file.FileName);
        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);

        // Return relative path for storage in DB
        return Path.Combine(code, file.FileName);
    }

    public Task<Stream> GetStreamAsync(string storagePath)
    {
        var fullPath = Path.Combine(_basePath, storagePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("File not found in storage.", fullPath);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath)
    {
        var fullPath = Path.Combine(_basePath, storagePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);

            // Also remove the code directory if empty
            var directory = Path.GetDirectoryName(fullPath);
            if (directory is not null && Directory.Exists(directory) &&
                !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }

        return Task.CompletedTask;
    }
}
