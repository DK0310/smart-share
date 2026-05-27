using FileShare.Application.Interfaces;
using FileShare.Domain.Entities;
using FileShare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FileShare.Infrastructure.Repositories;

public class FileRepository : IFileRepository
{
    private readonly AppDbContext _db;

    public FileRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<FileRecord?> GetByCodeAsync(string code)
    {
        return await _db.Files
            .Include(f => f.Uploader)
            .FirstOrDefaultAsync(f => f.Code == code);
    }

    public async Task AddAsync(FileRecord file)
    {
        _db.Files.Add(file);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(FileRecord file)
    {
        _db.Files.Remove(file);
        await _db.SaveChangesAsync();
    }

    public async Task<IEnumerable<FileRecord>> GetExpiredAsync()
    {
        var now = DateTime.UtcNow;
        return await _db.Files
            .Where(f =>
                (f.ExpiresAt != null && f.ExpiresAt < now) ||
                (f.MaxDownloads != null && f.DownloadCount >= f.MaxDownloads))
            .ToListAsync();
    }

    /// <summary>
    /// Atomic increment using ExecuteUpdateAsync — single round trip, no concurrency issues.
    /// </summary>
    public async Task IncrementDownloadCountAsync(string code)
    {
        await _db.Files
            .Where(f => f.Code == code)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(f => f.DownloadCount, f => f.DownloadCount + 1));
    }

    public async Task<IEnumerable<FileRecord>> GetByUploaderAsync(Guid userId, int limit = 50)
    {
        return await _db.Files
            .Where(f => f.UploaderId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }
}
