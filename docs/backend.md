---
name: backend-patterns
description: ASP.NET Core 8 patterns for File & Image Sharing Service — controllers, services, repositories, JWT auth, error handling, unit tests. Use when writing any backend C# code.
---

# Backend Patterns — ASP.NET Core 8

## Controller Pattern

```csharp
[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;

    public FilesController(IFileService fileService)
        => _fileService = fileService;

    // POST /api/files
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
    public async Task<IActionResult> Upload([FromForm] UploadFileRequest request)
    {
        var result = await _fileService.UploadAsync(request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetFile), new { code = result.Value.Code }, result.Value)
            : BadRequest(result.Error);
    }

    // GET /api/files/{code}
    [HttpGet("{code}")]
    public async Task<IActionResult> GetFile(string code)
    {
        var result = await _fileService.GetFileAsync(code);
        if (!result.IsSuccess) return NotFound();

        var file = result.Value;
        var stream = await _fileService.GetStreamAsync(file.StoragePath);
        return File(stream, file.MimeType, file.OriginalFilename);
    }

    // DELETE /api/files/{code}
    [HttpDelete("{code}")]
    [Authorize]
    public async Task<IActionResult> Delete(string code)
    {
        var userId = User.GetUserId();
        var result = await _fileService.DeleteAsync(code, userId);
        return result.IsSuccess ? NoContent() : Forbid();
    }
}
```

---

## Result Pattern (no exceptions for control flow)

```csharp
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }

    private Result(T value) { IsSuccess = true; Value = value; }
    private Result(string error) { IsSuccess = false; Error = error; }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(string error) => new(error);
}
```

---

## Service Pattern

```csharp
public class FileService : IFileService
{
    private readonly IFileRepository _repo;
    private readonly IStorageProvider _storage;
    private readonly IConfiguration _config;

    public FileService(IFileRepository repo, IStorageProvider storage, IConfiguration config)
    {
        _repo = repo; _storage = storage; _config = config;
    }

    public async Task<Result<FileResponse>> UploadAsync(UploadFileRequest request)
    {
        if (request.File.Length == 0) return Result<FileResponse>.Failure("File is empty");
        if (request.File.Length > 10 * 1024 * 1024) return Result<FileResponse>.Failure("File exceeds 10 MB limit");

        var allowedMimes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp",
                                   "application/pdf", "text/plain", "application/zip" };
        if (!allowedMimes.Contains(request.File.ContentType))
            return Result<FileResponse>.Failure("File type not allowed");

        var code = await GenerateUniqueCodeAsync();
        var storagePath = await _storage.SaveAsync(request.File, code);

        var entity = new FileRecord
        {
            Code = code,
            OriginalFilename = request.File.FileName,
            MimeType = request.File.ContentType,
            SizeBytes = request.File.Length,
            StoragePath = storagePath,
            MaxDownloads = request.MaxDownloads,
            ExpiresAt = request.ExpiryHours.HasValue
                ? DateTime.UtcNow.AddHours(request.ExpiryHours.Value)
                : null
        };

        await _repo.AddAsync(entity);
        return Result<FileResponse>.Success(FileResponse.From(entity));
    }
}
```

---

## Repository Pattern

```csharp
public interface IFileRepository
{
    Task<FileRecord?> GetByCodeAsync(string code);
    Task AddAsync(FileRecord file);
    Task DeleteAsync(FileRecord file);
    Task<IEnumerable<FileRecord>> GetExpiredAsync();
    Task IncrementDownloadCountAsync(string code);
}

public class FileRepository : IFileRepository
{
    private readonly AppDbContext _db;
    public FileRepository(AppDbContext db) => _db = db;

    public Task<FileRecord?> GetByCodeAsync(string code)
        => _db.Files.FirstOrDefaultAsync(f => f.Code == code);

    public async Task AddAsync(FileRecord file)
    {
        _db.Files.Add(file);
        await _db.SaveChangesAsync();
    }

    public async Task IncrementDownloadCountAsync(string code)
        => await _db.Files
            .Where(f => f.Code == code)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.DownloadCount, f => f.DownloadCount + 1));
}
```

---

## DTOs

```csharp
public record UploadFileRequest
{
    [Required] public IFormFile File { get; init; } = null!;
    public int? MaxDownloads { get; init; }
    public int? ExpiryHours { get; init; }
    public string? Password { get; init; }       // Distinction feature
}

public record FileResponse
{
    public string Code { get; init; } = "";
    public string OriginalFilename { get; init; } = "";
    public string MimeType { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public int? MaxDownloads { get; init; }
    public int DownloadCount { get; init; }
    public string Url => $"/f/{Code}";

    public static FileResponse From(FileRecord e) => new()
    {
        Code = e.Code, OriginalFilename = e.OriginalFilename,
        MimeType = e.MimeType, SizeBytes = e.SizeBytes,
        CreatedAt = e.CreatedAt, ExpiresAt = e.ExpiresAt,
        MaxDownloads = e.MaxDownloads, DownloadCount = e.DownloadCount
    };
}
```

---

## Storage Provider Interface

```csharp
public interface IStorageProvider
{
    Task<string> SaveAsync(IFormFile file, string code);
    Task<Stream> GetStreamAsync(string storagePath);
    Task DeleteAsync(string storagePath);
}

public class LocalStorageProvider : IStorageProvider
{
    private readonly string _basePath;
    public LocalStorageProvider(IConfiguration config)
        => _basePath = config["Storage:Local:Path"]!;

    public async Task<string> SaveAsync(IFormFile file, string code)
    {
        var ext = Path.GetExtension(file.FileName);
        var filename = $"{code}{ext}";
        var fullPath = Path.Combine(_basePath, filename);
        Directory.CreateDirectory(_basePath);
        await using var stream = File.Create(fullPath);
        await file.CopyToAsync(stream);
        return filename;
    }

    public Task<Stream> GetStreamAsync(string storagePath)
        => Task.FromResult<Stream>(File.OpenRead(Path.Combine(_basePath, storagePath)));

    public Task DeleteAsync(string storagePath)
    {
        var path = Path.Combine(_basePath, storagePath);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
```

---

## Cleanup Background Service

```csharp
public class CleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), ct);
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IFileRepository>();
            var storage = scope.ServiceProvider.GetRequiredService<IStorageProvider>();

            var expired = await repo.GetExpiredAsync();
            foreach (var file in expired)
            {
                await storage.DeleteAsync(file.StoragePath);
                await repo.DeleteAsync(file);
            }
        }
    }
}
```
Register: `builder.Services.AddHostedService<CleanupService>();`

---

## Global Error Handling Middleware

```csharp
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    public ErrorHandlingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await _next(ctx); }
        catch (Exception ex)
        {
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
    }
}
```

---

## JWT Auth

```csharp
// Generate token
public string GenerateToken(User user)
{
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        claims: [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
        expires: DateTime.UtcNow.AddDays(7),
        signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

// Extension to read user ID from token
public static Guid GetUserId(this ClaimsPrincipal user)
    => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
```

---

## Unit Test Pattern (xUnit + Moq)

```csharp
public class FileServiceTests
{
    private readonly Mock<IFileRepository> _repoMock = new();
    private readonly Mock<IStorageProvider> _storageMock = new();
    private readonly FileService _sut;

    public FileServiceTests()
    {
        var config = new ConfigurationBuilder().Build();
        _sut = new FileService(_repoMock.Object, _storageMock.Object, config);
    }

    [Fact]
    public async Task Upload_ReturnsFailure_WhenFileTooLarge()
    {
        var file = CreateMockFile(size: 11 * 1024 * 1024);
        var request = new UploadFileRequest { File = file };

        var result = await _sut.UploadAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Contains("10 MB", result.Error);
    }

    private static IFormFile CreateMockFile(long size)
    {
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.Length).Returns(size);
        mock.Setup(f => f.ContentType).Returns("image/jpeg");
        mock.Setup(f => f.FileName).Returns("test.jpg");
        return mock.Object;
    }
}
```
