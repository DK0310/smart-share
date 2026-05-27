using System.Security.Cryptography;
using FileShare.Application.DTOs;
using FileShare.Application.Interfaces;
using FileShare.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace FileShare.Application.Services;

public class FileService : IFileService
{
    private readonly IFileRepository _repo;
    private readonly IStorageProvider _storage;
    private readonly ILogger<FileService> _logger;

    // 10 MB max file size
    private const long MaxFileSize = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        "image/jpeg", "image/png", "image/gif", "image/webp", "image/svg+xml", "image/bmp",
        // Documents
        "application/pdf", "text/plain", "text/csv",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        // Archives
        "application/zip", "application/x-rar-compressed", "application/x-7z-compressed",
        "application/gzip", "application/x-tar",
        // Code / text
        "application/json", "application/xml", "text/html", "text/css",
        "application/javascript", "text/javascript",
        // Media
        "audio/mpeg", "audio/wav", "video/mp4", "video/webm",
        // Other
        "application/octet-stream"
    };

    public FileService(IFileRepository repo, IStorageProvider storage, ILogger<FileService> logger)
    {
        _repo = repo;
        _storage = storage;
        _logger = logger;
    }

    public async Task<Result<FileResponse>> UploadAsync(UploadFileRequest request, Guid? userId = null)
    {
        // 1. Validate
        if (request.File.Length == 0)
            return Result<FileResponse>.Failure("File is empty.");

        if (request.File.Length > MaxFileSize)
            return Result<FileResponse>.Failure("File exceeds 10 MB limit.");

        if (!AllowedMimeTypes.Contains(request.File.ContentType))
            return Result<FileResponse>.Failure($"File type '{request.File.ContentType}' is not allowed.");

        // 2. Generate unique short code
        var code = await GenerateUniqueCodeAsync();

        // 3. Save file bytes to storage
        string storagePath;
        try
        {
            storagePath = await _storage.SaveAsync(request.File, code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save file to storage for code {Code}", code);
            throw;
        }

        // 4. Build entity
        var entity = new FileRecord
        {
            Code = code,
            OriginalFilename = request.File.FileName,
            MimeType = request.File.ContentType,
            SizeBytes = request.File.Length,
            StoragePath = storagePath,
            MaxDownloads = request.MaxDownloads,
            ExpiresAt = request.ExpiresAt,
            UploaderId = userId,
            PasswordHash = request.Password is not null
                ? BCrypt.Net.BCrypt.HashPassword(request.Password)
                : null
        };

        // 5. Save metadata to DB — rollback storage on failure
        try
        {
            await _repo.AddAsync(entity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save file metadata for code {Code}, rolling back storage", code);
            await _storage.DeleteAsync(storagePath);
            throw;
        }

        _logger.LogInformation("File uploaded successfully: {Code} ({OriginalFilename}, {SizeBytes} bytes)",
            code, entity.OriginalFilename, entity.SizeBytes);

        return Result<FileResponse>.Success(FileResponse.From(entity));
    }

    public async Task<Result<FileResponse>> GetFileMetaAsync(string code)
    {
        var file = await _repo.GetByCodeAsync(code);
        if (file is null)
            return Result<FileResponse>.Failure("File not found.");

        return Result<FileResponse>.Success(FileResponse.From(file));
    }

    public async Task<Result<(Stream Stream, string ContentType, string FileName)>> DownloadAsync(string code)
    {
        var file = await _repo.GetByCodeAsync(code);
        if (file is null)
            return Result<(Stream, string, string)>.Failure("File not found.");

        if (!file.IsAvailable)
            return Result<(Stream, string, string)>.Failure("File is no longer available.");

        // Increment download count
        await _repo.IncrementDownloadCountAsync(code);

        var stream = await _storage.GetStreamAsync(file.StoragePath);
        return Result<(Stream, string, string)>.Success((stream, file.MimeType, file.OriginalFilename));
    }

    public async Task<Result<bool>> DeleteAsync(string code, Guid userId)
    {
        var file = await _repo.GetByCodeAsync(code);
        if (file is null)
            return Result<bool>.Failure("File not found.");

        if (file.UploaderId != userId)
            return Result<bool>.Failure("You do not have permission to delete this file.");

        await _storage.DeleteAsync(file.StoragePath);
        await _repo.DeleteAsync(file);

        _logger.LogInformation("File deleted: {Code} by user {UserId}", code, userId);
        return Result<bool>.Success(true);
    }

    public async Task<Result<IEnumerable<FileResponse>>> GetUserFilesAsync(Guid userId)
    {
        var files = await _repo.GetByUploaderAsync(userId);
        var responses = files.Select(FileResponse.From);
        return Result<IEnumerable<FileResponse>>.Success(responses);
    }

    public async Task CleanupExpiredAsync()
    {
        var expired = await _repo.GetExpiredAsync();
        var count = 0;

        foreach (var file in expired)
        {
            try
            {
                await _storage.DeleteAsync(file.StoragePath);
                await _repo.DeleteAsync(file);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup expired file {Code}", file.Code);
            }
        }

        if (count > 0)
            _logger.LogInformation("Cleaned up {Count} expired files", count);
    }

    /// <summary>
    /// Generate a unique 5-character alphanumeric code.
    /// Uses cryptographic randomness. Retries on collision (62^5 ≈ 916M possible codes).
    /// </summary>
    private async Task<string> GenerateUniqueCodeAsync()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        string code;
        do
        {
            code = new string(Enumerable.Range(0, 5)
                .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)])
                .ToArray());
        }
        while (await _repo.GetByCodeAsync(code) is not null);
        return code;
    }
}
