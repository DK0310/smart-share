using Microsoft.AspNetCore.Http;

namespace FileShare.Application.Interfaces;

public interface IStorageProvider
{
    /// <summary>Save file bytes and return the storage path.</summary>
    Task<string> SaveAsync(IFormFile file, string code);

    /// <summary>Retrieve a readable stream for the stored file.</summary>
    Task<Stream> GetStreamAsync(string storagePath);

    /// <summary>Delete the stored file.</summary>
    Task DeleteAsync(string storagePath);
}
