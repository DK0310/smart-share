using FileShare.Application.DTOs;

namespace FileShare.Application.Interfaces;

public interface IFileService
{
    Task<Result<FileResponse>> UploadAsync(UploadFileRequest request, Guid? userId = null);
    Task<Result<FileResponse>> GetFileMetaAsync(string code);
    Task<Result<(Stream Stream, string ContentType, string FileName)>> DownloadAsync(string code);
    Task<Result<bool>> DeleteAsync(string code, Guid userId);
    Task<Result<IEnumerable<FileResponse>>> GetUserFilesAsync(Guid userId);
    Task CleanupExpiredAsync();
}
