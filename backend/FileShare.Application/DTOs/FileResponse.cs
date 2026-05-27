using FileShare.Domain.Entities;

namespace FileShare.Application.DTOs;

public class FileResponse
{
    public string Code { get; set; } = "";
    public string OriginalFilename { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long SizeBytes { get; set; }
    public int DownloadCount { get; set; }
    public int? MaxDownloads { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsImage { get; set; }
    public bool IsAvailable { get; set; }
    public bool HasPassword { get; set; }
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Maps a domain entity to a response DTO.
    /// Excludes StoragePath and PasswordHash by design.
    /// </summary>
    public static FileResponse From(FileRecord entity) => new()
    {
        Code = entity.Code,
        OriginalFilename = entity.OriginalFilename,
        MimeType = entity.MimeType,
        SizeBytes = entity.SizeBytes,
        DownloadCount = entity.DownloadCount,
        MaxDownloads = entity.MaxDownloads,
        ExpiresAt = entity.ExpiresAt,
        CreatedAt = entity.CreatedAt,
        IsImage = entity.IsImage,
        IsAvailable = entity.IsAvailable,
        HasPassword = entity.PasswordHash is not null,
        ThumbnailUrl = entity.ThumbnailPath is not null ? $"/api/files/{entity.Code}/thumbnail" : null
    };
}
