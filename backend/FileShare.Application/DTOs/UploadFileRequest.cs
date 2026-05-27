using Microsoft.AspNetCore.Http;

namespace FileShare.Application.DTOs;

public class UploadFileRequest
{
    public required IFormFile File { get; set; }
    public int? MaxDownloads { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Password { get; set; }
}
