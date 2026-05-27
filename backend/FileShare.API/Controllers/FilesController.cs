using FileShare.API.Extensions;
using FileShare.Application.DTOs;
using FileShare.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FileShare.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;

    public FilesController(IFileService fileService)
    {
        _fileService = fileService;
    }

    /// <summary>
    /// Upload a file. Public endpoint — authenticated users get ownership tracking.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Upload([FromForm] UploadFileRequest request)
    {
        Guid? userId = User.Identity?.IsAuthenticated == true
            ? User.GetUserId()
            : null;

        var result = await _fileService.UploadAsync(request, userId);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetMeta), new { code = result.Value!.Code }, result.Value)
            : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Download or preview a file by its short code.
    /// </summary>
    [HttpGet("{code}")]
    public async Task<IActionResult> Download(string code)
    {
        var result = await _fileService.DownloadAsync(code);

        if (!result.IsSuccess)
            return NotFound(new { error = result.Error });

        var (stream, contentType, fileName) = result.Value!;
        return File(stream, contentType, fileName);
    }

    /// <summary>
    /// Get file metadata without downloading.
    /// </summary>
    [HttpGet("{code}/meta")]
    public async Task<IActionResult> GetMeta(string code)
    {
        var result = await _fileService.GetFileMetaAsync(code);

        return result.IsSuccess
            ? Ok(result.Value)
            : NotFound(new { error = result.Error });
    }

    /// <summary>
    /// List files uploaded by the authenticated user.
    /// </summary>
    [HttpGet("my-uploads")]
    [Authorize]
    public async Task<IActionResult> MyUploads()
    {
        var userId = User.GetUserId();
        var result = await _fileService.GetUserFilesAsync(userId);

        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { error = result.Error });
    }

    /// <summary>
    /// Delete a file. Owner-only.
    /// </summary>
    [HttpDelete("{code}")]
    [Authorize]
    public async Task<IActionResult> Delete(string code)
    {
        var userId = User.GetUserId();
        var result = await _fileService.DeleteAsync(code, userId);

        if (!result.IsSuccess)
            return result.Error == "File not found."
                ? NotFound(new { error = result.Error })
                : StatusCode(403, new { error = result.Error });

        return NoContent();
    }
}
