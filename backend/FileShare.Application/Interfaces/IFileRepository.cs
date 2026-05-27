using FileShare.Domain.Entities;

namespace FileShare.Application.Interfaces;

public interface IFileRepository
{
    Task<FileRecord?> GetByCodeAsync(string code);
    Task AddAsync(FileRecord file);
    Task DeleteAsync(FileRecord file);
    Task<IEnumerable<FileRecord>> GetExpiredAsync();
    Task IncrementDownloadCountAsync(string code);
    Task<IEnumerable<FileRecord>> GetByUploaderAsync(Guid userId, int limit = 50);
}
