---
name: fileshare-backend
description: Use when implementing any backend endpoint, service, repository, DTO, or unit test in ASP.NET Core 8
---

# Smart Share — Backend Patterns Skill

## Overview

The backend is an ASP.NET Core 8 Web API organized into four projects. Every endpoint follows the same layered pattern: Controller → Service → Repository/Storage. Services return `Result<T>` instead of throwing exceptions. All business logic lives in services, never in controllers.

---

## Project Structure

```
backend/
├── FileShare.API/                        ← HTTP entry point
│   ├── Controllers/
│   │   ├── FilesController.cs            ← File upload/download/delete
│   │   └── AuthController.cs             ← Register/login
│   ├── Middleware/
│   │   └── ErrorHandlingMiddleware.cs     ← Global exception → 500 JSON
│   └── Program.cs                        ← DI registration, middleware pipeline
│
├── FileShare.Application/                ← Business logic
│   ├── Services/
│   │   ├── FileService.cs                ← Upload, download, delete, cleanup
│   │   ├── AuthService.cs                ← Register, login, JWT generation
│   │   └── CleanupService.cs             ← BackgroundService for expired files
│   ├── DTOs/
│   │   ├── UploadFileRequest.cs
│   │   ├── FileResponse.cs
│   │   ├── RegisterRequest.cs
│   │   └── LoginRequest.cs
│   └── Interfaces/
│       ├── IFileService.cs
│       ├── IAuthService.cs
│       ├── IFileRepository.cs
│       └── IStorageProvider.cs
│
├── FileShare.Domain/                     ← Entities (zero dependencies)
│   └── Entities/
│       ├── FileRecord.cs
│       └── User.cs
│
└── FileShare.Infrastructure/             ← Data access, external services
    ├── Persistence/
    │   ├── AppDbContext.cs
    │   └── Migrations/
    ├── Storage/
    │   ├── LocalStorageProvider.cs
    │   └── BlobStorageProvider.cs
    └── Repositories/
        └── FileRepository.cs
```

---

## Result\<T\> Pattern

**Every service method returns `Result<T>`. No exceptions for business logic.**

```csharp
// Location: FileShare.Application/ (shared utility)
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

**Usage contract:**
- Service returns `Result<T>.Failure("user-friendly message")` for ALL expected errors
- Service returns `Result<T>.Success(value)` for successful operations
- Controller checks `result.IsSuccess` and maps to HTTP status
- Unexpected errors (DB down, disk full) are NOT caught by services — they propagate to `ErrorHandlingMiddleware`

---

## Controller Pattern

**Location:** `backend/FileShare.API/Controllers/`

Controllers do exactly THREE things:
1. Accept the HTTP request (binding, routing)
2. Call ONE service method
3. Map `Result<T>` to an HTTP status code

```csharp
[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;

    public FilesController(IFileService fileService)
        => _fileService = fileService;

    // ── POST /api/files ─────────────────────────────────────────
    // Creates a new file resource. Public (no auth required).
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB hard limit at HTTP level
    public async Task<IActionResult> Upload([FromForm] UploadFileRequest request)
    {
        var result = await _fileService.UploadAsync(request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetFile), new { code = result.Value.Code }, result.Value)
            : BadRequest(new { error = result.Error });
    }

    // ── GET /api/files/{code} ───────────────────────────────────
    // Downloads the file. Public.
    [HttpGet("{code}")]
    public async Task<IActionResult> GetFile(string code)
    {
        var result = await _fileService.GetFileAsync(code);
        if (!result.IsSuccess) return NotFound(new { error = result.Error });

        var file = result.Value;
        var stream = await _fileService.GetStreamAsync(file.StoragePath);
        return File(stream, file.MimeType, file.OriginalFilename);
    }

    // ── GET /api/files/{code}/meta ──────────────────────────────
    // Returns file metadata (no download). Public.
    [HttpGet("{code}/meta")]
    public async Task<IActionResult> GetFileMeta(string code)
    {
        var result = await _fileService.GetFileAsync(code);
        return result.IsSuccess
            ? Ok(result.Value)
            : NotFound(new { error = result.Error });
    }

    // ── DELETE /api/files/{code} ────────────────────────────────
    // Deletes a file. Owner only.
    [HttpDelete("{code}")]
    [Authorize]
    public async Task<IActionResult> Delete(string code)
    {
        var userId = User.GetUserId();
        var result = await _fileService.DeleteAsync(code, userId);
        return result.IsSuccess ? NoContent() : Forbid();
    }

    // ── GET /api/files/my-uploads ───────────────────────────────
    // Lists authenticated user's uploads.
    [HttpGet("my-uploads")]
    [Authorize]
    public async Task<IActionResult> MyUploads()
    {
        var userId = User.GetUserId();
        var result = await _fileService.GetUserFilesAsync(userId);
        return Ok(result);
    }
}
```

### HTTP Status Code Decision Tree

```
SERVICE RETURNED Success:
├─ POST (created resource)     → 201 Created + Location header
├─ GET (returned data)         → 200 OK + body
├─ PUT/PATCH (updated)         → 200 OK + updated body
└─ DELETE (removed)            → 204 No Content

SERVICE RETURNED Failure:
├─ Validation failed           → 400 Bad Request + { error: "message" }
├─ Resource not found          → 404 Not Found + { error: "message" }
├─ Not authenticated           → 401 Unauthorized (handled by [Authorize])
├─ Not authorized (not owner)  → 403 Forbid
└─ Conflict (duplicate)        → 409 Conflict + { error: "message" }

UNHANDLED EXCEPTION:
└─ ErrorHandlingMiddleware     → 500 Internal Server Error + { error: "message" }
```

---

## Service Pattern

**Location:** `backend/FileShare.Application/Services/`

Services contain ALL business logic. They validate, coordinate, and return `Result<T>`.

### Complete FileService Implementation

```csharp
public class FileService : IFileService
{
    private readonly IFileRepository _repo;
    private readonly IStorageProvider _storage;
    private readonly IConfiguration _config;

    public FileService(IFileRepository repo, IStorageProvider storage, IConfiguration config)
    {
        _repo = repo;
        _storage = storage;
        _config = config;
    }

    // ── Upload ──────────────────────────────────────────────────
    public async Task<Result<FileResponse>> UploadAsync(UploadFileRequest request)
    {
        // STEP 1: Validate (fail fast, cheapest checks first)
        if (request.File.Length == 0)
            return Result<FileResponse>.Failure("File is empty");

        if (request.File.Length > 10 * 1024 * 1024)
            return Result<FileResponse>.Failure("File exceeds 10 MB limit");

        var allowedMimes = new[]
        {
            "image/jpeg", "image/png", "image/gif", "image/webp",
            "application/pdf", "text/plain", "application/zip"
        };
        if (!allowedMimes.Contains(request.File.ContentType))
            return Result<FileResponse>.Failure("File type not allowed");

        // STEP 2: Generate unique code (retry on collision)
        var code = await GenerateUniqueCodeAsync();

        // STEP 3: Save to storage FIRST (if this fails, nothing is persisted)
        var storagePath = await _storage.SaveAsync(request.File, code);

        // STEP 4: Save metadata to database
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

        try
        {
            await _repo.AddAsync(entity);
        }
        catch
        {
            // DB save failed — clean up the stored file
            await _storage.DeleteAsync(storagePath);
            throw; // Let ErrorHandlingMiddleware handle it
        }

        // STEP 5: Return success
        return Result<FileResponse>.Success(FileResponse.From(entity));
    }

    // ── Get File ────────────────────────────────────────────────
    public async Task<Result<FileResponse>> GetFileAsync(string code)
    {
        var file = await _repo.GetByCodeAsync(code);
        if (file is null)
            return Result<FileResponse>.Failure("File not found");

        if (!file.IsAvailable)
            return Result<FileResponse>.Failure("File has expired or reached download limit");

        await _repo.IncrementDownloadCountAsync(code);
        return Result<FileResponse>.Success(FileResponse.From(file));
    }

    // ── Delete ──────────────────────────────────────────────────
    public async Task<Result<bool>> DeleteAsync(string code, Guid userId)
    {
        var file = await _repo.GetByCodeAsync(code);
        if (file is null)
            return Result<bool>.Failure("File not found");

        if (file.UploaderId != userId)
            return Result<bool>.Failure("You don't own this file");

        await _storage.DeleteAsync(file.StoragePath);
        await _repo.DeleteAsync(file);
        return Result<bool>.Success(true);
    }

    // ── Unique Code Generation ──────────────────────────────────
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
```

### Validation Order (always follow this sequence)

```
1. Null/empty checks          (no I/O, instant)
2. Size limits                (no I/O, instant)
3. Format/type checks         (no I/O, instant)
4. Business rule checks       (may need DB read)
5. Permission checks          (may need DB read)
6. External operations        (storage write, then DB write)
```

**Rationale:** Cheapest checks first. If step 1 fails, we never hit the database. If storage write fails, we never touch the DB. If DB write fails, we clean up storage.

---

## Repository Pattern

**Location:** `backend/FileShare.Infrastructure/Repositories/`

Repositories ONLY do database operations. No business logic, no HTTP concerns.

```csharp
public interface IFileRepository
{
    Task<FileRecord?> GetByCodeAsync(string code);
    Task AddAsync(FileRecord file);
    Task DeleteAsync(FileRecord file);
    Task<IEnumerable<FileRecord>> GetExpiredAsync();
    Task IncrementDownloadCountAsync(string code);
    Task<IEnumerable<FileRecord>> GetByUploaderAsync(Guid userId, int limit = 50);
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

    public async Task DeleteAsync(FileRecord file)
    {
        _db.Files.Remove(file);
        await _db.SaveChangesAsync();
    }

    public async Task<IEnumerable<FileRecord>> GetExpiredAsync()
        => await _db.Files
            .Where(f => (f.ExpiresAt != null && f.ExpiresAt < DateTime.UtcNow)
                     || (f.MaxDownloads != null && f.DownloadCount >= f.MaxDownloads))
            .ToListAsync();

    // Atomic increment — single DB round trip, no load-modify-save
    public async Task IncrementDownloadCountAsync(string code)
        => await _db.Files
            .Where(f => f.Code == code)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.DownloadCount, f => f.DownloadCount + 1));

    public async Task<IEnumerable<FileRecord>> GetByUploaderAsync(Guid userId, int limit = 50)
        => await _db.Files
            .Where(f => f.UploaderId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Take(limit)
            .ToListAsync();
}
```

### Query Efficiency Rules

```
READING a single entity to return it:
  → FirstOrDefaultAsync() — returns null if not found, service checks

READING a single entity to UPDATE it:
  → Use ExecuteUpdateAsync() if possible (1 round trip)
  → Only load + modify + SaveChanges if you need computed values

READING a list:
  → Where() + OrderBy() + Take() + ToListAsync()
  → ALWAYS limit with Take() to prevent unbounded queries

DELETING:
  → Load entity, _db.Remove(), SaveChangesAsync()
  → Or ExecuteDeleteAsync() for bulk without loading

COUNTING:
  → CountAsync() — never load entities just to count
```

---

## DTO Pattern

**Location:** `backend/FileShare.Application/DTOs/`

### Request DTOs (Client → Server)

```csharp
public record UploadFileRequest
{
    [Required] public IFormFile File { get; init; } = null!;
    public int? MaxDownloads { get; init; }
    public int? ExpiryHours { get; init; }
    public string? Password { get; init; }
}

public record RegisterRequest
{
    [Required] public string Email { get; init; } = "";
    [Required] [MinLength(6)] public string Password { get; init; } = "";
}

public record LoginRequest
{
    [Required] public string Email { get; init; } = "";
    [Required] public string Password { get; init; } = "";
}
```

### Response DTOs (Server → Client)

```csharp
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

    // Computed — shareable URL
    public string Url => $"/f/{Code}";

    // Factory method — entity → response
    public static FileResponse From(FileRecord e) => new()
    {
        Code = e.Code,
        OriginalFilename = e.OriginalFilename,
        MimeType = e.MimeType,
        SizeBytes = e.SizeBytes,
        CreatedAt = e.CreatedAt,
        ExpiresAt = e.ExpiresAt,
        MaxDownloads = e.MaxDownloads,
        DownloadCount = e.DownloadCount
    };
}
```

### What to include vs exclude in responses

| Include | Exclude |
|---|---|
| Code (public identifier) | StoragePath (internal) |
| OriginalFilename | PasswordHash (security) |
| MimeType, SizeBytes | UploaderId (user privacy) |
| DownloadCount, MaxDownloads | Database Id (internal) |
| ExpiresAt, CreatedAt | |
| Computed Url | |

---

## Storage Provider Pattern

**Location:** `backend/FileShare.Infrastructure/Storage/`

```csharp
public interface IStorageProvider
{
    Task<string> SaveAsync(IFormFile file, string code);
    Task<Stream> GetStreamAsync(string storagePath);
    Task DeleteAsync(string storagePath);
}

// ── Local disk (development) ────────────────────────────────
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
        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream);
        return filename; // Return relative path, not absolute
    }

    public Task<Stream> GetStreamAsync(string storagePath)
        => Task.FromResult<Stream>(
            System.IO.File.OpenRead(Path.Combine(_basePath, storagePath)));

    public Task DeleteAsync(string storagePath)
    {
        var path = Path.Combine(_basePath, storagePath);
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        return Task.CompletedTask;
    }
}
```

**To add a new storage provider:**
1. Create class in `Infrastructure/Storage/` implementing `IStorageProvider`
2. Register in `Program.cs` conditionally based on `Storage:Provider` config
3. Do NOT modify the interface

---

## Authentication Implementation

**Location:** `backend/FileShare.Application/Services/AuthService.cs`

```csharp
public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(AppDbContext db, IConfiguration config)
    { _db = db; _config = config; }

    public async Task<Result<string>> RegisterAsync(RegisterRequest request)
    {
        if (await _db.Users.AnyAsync(u => u.Email == request.Email))
            return Result<string>.Failure("Email already registered");

        var user = new User
        {
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return Result<string>.Success(GenerateToken(user));
    }

    public async Task<Result<string>> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Result<string>.Failure("Invalid email or password");

        return Result<string>.Success(GenerateToken(user));
    }

    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())],
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

// Extension method for extracting user ID from JWT claims
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
```

### Using [Authorize] in Controllers

```csharp
// Require valid JWT token
[HttpDelete("{code}")]
[Authorize]
public async Task<IActionResult> Delete(string code)
{
    var userId = User.GetUserId();  // Extract from JWT claims
    var result = await _fileService.DeleteAsync(code, userId);
    return result.IsSuccess ? NoContent() : Forbid();
}
```

---

## Error Handling Middleware

**Location:** `backend/FileShare.API/Middleware/ErrorHandlingMiddleware.cs`

```csharp
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    { _next = next; _logger = logger; }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred" });
        }
    }
}

// Register in Program.cs (BEFORE other middleware):
app.UseMiddleware<ErrorHandlingMiddleware>();
```

**Important:** In production, do NOT return `ex.Message` — it can leak internal details. Return a generic message and log the real error.

---

## Background Cleanup Service

**Location:** `backend/FileShare.Application/Services/CleanupService.cs`

```csharp
public class CleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CleanupService> _logger;

    public CleanupService(IServiceScopeFactory scopeFactory, ILogger<CleanupService> logger)
    { _scopeFactory = scopeFactory; _logger = logger; }

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
                try
                {
                    await storage.DeleteAsync(file.StoragePath);
                    await repo.DeleteAsync(file);
                    _logger.LogInformation("Cleaned up expired file {Code}", file.Code);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to clean up file {Code}", file.Code);
                }
            }
        }
    }
}
```

**Why `IServiceScopeFactory`:** BackgroundService is a singleton, but `AppDbContext` is scoped. You must create a new scope for each cleanup cycle.

---

## DI Registration (Program.cs)

```csharp
var builder = WebApplication.CreateBuilder(args);

// ── Database ────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// ── Services ────────────────────────────────────────────────
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddHostedService<CleanupService>();

// ── Repositories ────────────────────────────────────────────
builder.Services.AddScoped<IFileRepository, FileRepository>();

// ── Storage ─────────────────────────────────────────────────
builder.Services.AddScoped<IStorageProvider, LocalStorageProvider>();

// ── Auth ────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!)),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });

// ── CORS ────────────────────────────────────────────────────
builder.Services.AddCors(opt => opt.AddPolicy("Frontend", p =>
    p.WithOrigins(builder.Configuration["Frontend:Url"]!)
     .AllowAnyMethod().AllowAnyHeader()));

builder.Services.AddControllers();

var app = builder.Build();

// ── Middleware pipeline (ORDER MATTERS) ─────────────────────
app.UseMiddleware<ErrorHandlingMiddleware>();  // 1st: catch all exceptions
app.UseCors("Frontend");                       // 2nd: CORS headers
app.UseAuthentication();                       // 3rd: validate JWT
app.UseAuthorization();                        // 4th: check [Authorize]
app.MapControllers();                          // 5th: route to controllers

app.Run();
```

---

## Unit Testing

**Location:** `backend/FileShare.Tests/` (xUnit + Moq)

### Test Structure

```csharp
public class FileServiceTests
{
    // ── Arrange: Create mocks and instantiate SUT ───────────
    private readonly Mock<IFileRepository> _repoMock = new();
    private readonly Mock<IStorageProvider> _storageMock = new();
    private readonly IConfiguration _config;
    private readonly FileService _sut;

    public FileServiceTests()
    {
        _config = new ConfigurationBuilder().Build();
        _sut = new FileService(_repoMock.Object, _storageMock.Object, _config);
    }

    // ── Test: Success path ──────────────────────────────────
    [Fact]
    public async Task Upload_ReturnsSuccess_WhenFileIsValid()
    {
        var file = CreateMockFile(size: 1024, contentType: "image/jpeg");
        var request = new UploadFileRequest { File = file };

        _repoMock.Setup(r => r.GetByCodeAsync(It.IsAny<string>()))
            .ReturnsAsync((FileRecord?)null); // No collision
        _storageMock.Setup(s => s.SaveAsync(It.IsAny<IFormFile>(), It.IsAny<string>()))
            .ReturnsAsync("test.jpg");

        var result = await _sut.UploadAsync(request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("image/jpeg", result.Value.MimeType);
    }

    // ── Test: Validation failure ────────────────────────────
    [Fact]
    public async Task Upload_ReturnsFailure_WhenFileTooLarge()
    {
        var file = CreateMockFile(size: 11 * 1024 * 1024);
        var request = new UploadFileRequest { File = file };

        var result = await _sut.UploadAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Contains("10 MB", result.Error);
    }

    [Fact]
    public async Task Upload_ReturnsFailure_WhenFileEmpty()
    {
        var file = CreateMockFile(size: 0);
        var request = new UploadFileRequest { File = file };

        var result = await _sut.UploadAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Contains("empty", result.Error);
    }

    [Fact]
    public async Task Upload_ReturnsFailure_WhenMimeTypeNotAllowed()
    {
        var file = CreateMockFile(size: 1024, contentType: "application/exe");
        var request = new UploadFileRequest { File = file };

        var result = await _sut.UploadAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Contains("not allowed", result.Error);
    }

    [Fact]
    public async Task Delete_ReturnsFailure_WhenNotOwner()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        _repoMock.Setup(r => r.GetByCodeAsync("abc12"))
            .ReturnsAsync(new FileRecord { Code = "abc12", UploaderId = ownerId });

        var result = await _sut.DeleteAsync("abc12", otherUserId);

        Assert.False(result.IsSuccess);
        _storageMock.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    // ── Helper ──────────────────────────────────────────────
    private static IFormFile CreateMockFile(
        long size, string contentType = "image/jpeg", string fileName = "test.jpg")
    {
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.Length).Returns(size);
        mock.Setup(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.FileName).Returns(fileName);
        return mock.Object;
    }
}
```

### Test Checklist for Every Service Method

```
□ Success path works (happy case)
□ Each validation failure returns correct error message
□ Permission denied returns failure (not exception)
□ Not-found returns failure
□ Side effects happen in correct order (storage before DB)
□ Side effects DON'T happen when validation fails
□ Error messages are user-friendly, not technical
```

---

## End-to-End: Adding a New Endpoint

Example: `GET /api/files/{code}/stats` — returns download statistics.

### Step 1: Create Response DTO

File: `backend/FileShare.Application/DTOs/FileStatsResponse.cs`

```csharp
public record FileStatsResponse
{
    public string Code { get; init; } = "";
    public int DownloadCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public bool IsAvailable { get; init; }

    public static FileStatsResponse From(FileRecord e) => new()
    {
        Code = e.Code,
        DownloadCount = e.DownloadCount,
        CreatedAt = e.CreatedAt,
        ExpiresAt = e.ExpiresAt,
        IsAvailable = e.IsAvailable
    };
}
```

### Step 2: Add Service Method

File: `backend/FileShare.Application/Interfaces/IFileService.cs`

```csharp
Task<Result<FileStatsResponse>> GetStatsAsync(string code);
```

File: `backend/FileShare.Application/Services/FileService.cs`

```csharp
public async Task<Result<FileStatsResponse>> GetStatsAsync(string code)
{
    var file = await _repo.GetByCodeAsync(code);
    if (file is null)
        return Result<FileStatsResponse>.Failure("File not found");

    return Result<FileStatsResponse>.Success(FileStatsResponse.From(file));
}
```

### Step 3: Add Controller Action

File: `backend/FileShare.API/Controllers/FilesController.cs`

```csharp
[HttpGet("{code}/stats")]
public async Task<IActionResult> GetStats(string code)
{
    var result = await _fileService.GetStatsAsync(code);
    return result.IsSuccess ? Ok(result.Value) : NotFound(new { error = result.Error });
}
```

### Step 4: Register (if new service)

Already registered — `IFileService` is in DI. Skip this step.

### Step 5: Write Tests

```csharp
[Fact]
public async Task GetStats_ReturnsSuccess_WhenFileExists()
{
    _repoMock.Setup(r => r.GetByCodeAsync("abc12"))
        .ReturnsAsync(new FileRecord { Code = "abc12", DownloadCount = 5 });

    var result = await _sut.GetStatsAsync("abc12");

    Assert.True(result.IsSuccess);
    Assert.Equal(5, result.Value.DownloadCount);
}

[Fact]
public async Task GetStats_ReturnsFailure_WhenFileNotFound()
{
    _repoMock.Setup(r => r.GetByCodeAsync("nope"))
        .ReturnsAsync((FileRecord?)null);

    var result = await _sut.GetStatsAsync("nope");

    Assert.False(result.IsSuccess);
}
```

---

## Common Mistakes

| ❌ Don't | ✅ Do | Why |
|---|---|---|
| `throw new Exception("File too large")` | `return Result<T>.Failure("File too large")` | No exceptions for business logic |
| Put MIME check in controller | Put MIME check in service | Business rules live in services |
| `_db.Files.First(...)` in controller | Call `_repo.GetByCodeAsync()` from service | Never skip layers |
| Return `ex.Message` in 500 responses | Return generic message, log real error | Don't leak internals |
| Multiple `SaveChangesAsync()` per request | Single `SaveChangesAsync()` after all mutations | Atomic operations |
| `new FileService(...)` in controller | Constructor injection via DI | Testability, lifetime management |
| Forget `[Authorize]` on owner-only endpoints | Always add `[Authorize]` + ownership check | Security |
| Return `Ok()` for POST creation | Return `CreatedAtAction()` with 201 | REST conventions |
| Hardcode `10485760` | Use `10 * 1024 * 1024` | Readable intent |
| Return entity directly from controller | Return DTO via `FileResponse.From(entity)` | Never expose internal fields |
