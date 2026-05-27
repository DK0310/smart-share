namespace FileShare.Domain.Entities;

public class FileRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";           // 5-char, unique, indexed
    public string OriginalFilename { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = "";    // relative path in storage provider
    public int? MaxDownloads { get; set; }           // null = unlimited
    public int DownloadCount { get; set; }
    public DateTime? ExpiresAt { get; set; }         // null = never expires
    public string? PasswordHash { get; set; }        // Distinction feature
    public string? ThumbnailPath { get; set; }       // Distinction feature
    public Guid? UploaderId { get; set; }            // FK → Users.Id, SET NULL on delete
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? Uploader { get; set; }

    // Computed — NOT stored in DB
    public bool IsExpired    => ExpiresAt.HasValue && ExpiresAt < DateTime.UtcNow;
    public bool IsOverLimit  => MaxDownloads.HasValue && DownloadCount >= MaxDownloads;
    public bool IsAvailable  => !IsExpired && !IsOverLimit;
    public bool IsImage      => MimeType.StartsWith("image/");
}
