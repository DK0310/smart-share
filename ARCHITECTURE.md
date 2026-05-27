---
name: fileshare-architecture
description: Read FIRST before writing any code. Defines folder structure, layer responsibilities, naming conventions, request flow, DI registration, and where every piece of logic belongs in the File & Image Sharing Service.
---

# FileShare — Architecture

## Stack

| Layer | Technology | Purpose |
|---|---|---|
| Frontend | React 18 + TypeScript + Vite | SPA |
| Backend | ASP.NET Core 8 Web API | REST API + business logic |
| ORM | EF Core 8 + `Microsoft.EntityFrameworkCore.SqlServer` | DB abstraction |
| Database | SQL Server 2022 (LocalDB dev / Docker prod) | Persistence |
| Storage (dev) | Local disk (`wwwroot/uploads`) | File bytes |
| Storage (prod) | Azure Blob / AWS S3 | File bytes |
| Auth | JWT Bearer, 7-day expiry | Identity |
| Containers | Docker multi-stage | Build + runtime |
| Orchestration | docker-compose | Local dev |
| CI/CD | GitHub Actions → Docker Hub → Render | Lint→Test→Build→Push→Deploy |

---

## Folder Structure

```
/
├── backend/
│   ├── FileShare.API/
│   │   ├── Controllers/          # FilesController, AuthController
│   │   ├── Middleware/           # ErrorHandlingMiddleware
│   │   └── Program.cs            # DI wiring + middleware pipeline
│   │
│   ├── FileShare.Application/
│   │   ├── Services/             # FileService, AuthService, CleanupService
│   │   ├── DTOs/                 # UploadFileRequest, FileResponse, LoginRequest
│   │   └── Interfaces/           # IFileService, IFileRepository, IStorageProvider
│   │
│   ├── FileShare.Domain/
│   │   └── Entities/             # FileRecord, User  (zero dependencies)
│   │
│   └── FileShare.Infrastructure/
│       ├── Persistence/          # AppDbContext, Migrations/
│       ├── Storage/              # LocalStorageProvider, BlobStorageProvider
│       └── Repositories/         # FileRepository
│
├── frontend/
│   └── src/
│       ├── api/                  # api.ts  — axios instance + interceptors
│       ├── types/                # file.types.ts
│       ├── hooks/                # useUpload.ts, useFileInfo.ts
│       ├── components/           # DropZone, ProgressBar, ImagePreview
│       └── pages/                # UploadPage, FilePage, HistoryPage
│
├── .github/workflows/ci-cd.yml
└── docker-compose.yml
```

### Dependency Direction (arrows = "depends on")

```
FileShare.API ──► FileShare.Application ──► FileShare.Domain
                                          ▲
FileShare.Infrastructure ─────────────────┘

Rule: Domain has ZERO external dependencies.
      Infrastructure implements interfaces defined in Application.
      API registers Infrastructure via DI — nothing else references it directly.
```

---

## Where Does This Logic Belong?

Answer this before writing a single line:

```
Parsing an HTTP request / binding route params?
  → Controller   ([FromForm], [FromBody], [FromRoute])

Returning an HTTP status code?
  → Controller   (Ok(), CreatedAtAction(), BadRequest(), NotFound(), Forbid())

Validating a business rule (size, MIME, ownership, expiry)?
  → Service      — return Result<T>.Failure("message"), never throw

Coordinating multiple operations (save file + save metadata)?
  → Service      — orchestrate storage + repository calls

Generating derived data (short code, password hash, expiry time)?
  → Service

Reading or writing the database?
  → Repository   — EF Core queries, SaveChangesAsync()

Reading or writing files (disk / cloud)?
  → StorageProvider — SaveAsync(), GetStreamAsync(), DeleteAsync()

Defining API request/response shape?
  → DTO          (Application/DTOs/)

Defining persistent data shape?
  → Entity       (Domain/Entities/)

Rendering UI?
  → React Component  (frontend/src/components/ or pages/)

Making API calls or managing async state?
  → React Hook       (frontend/src/hooks/)

TypeScript types for API responses?
  → Type file        (frontend/src/types/)
```

---

## Request Flow (end-to-end)

```
React component
  → Custom hook calls api.post('/files', formData)
  → axios interceptor injects JWT header
  ↓ HTTP multipart/form-data
FilesController.Upload([FromForm] UploadFileRequest)
  → _fileService.UploadAsync(request)
  ↓
FileService
  1. Validate (size, MIME, empty)   — return Failure() on violation
  2. GenerateUniqueCodeAsync()      — retry loop until no collision
  3. _storage.SaveAsync(file, code) — write bytes to disk/cloud
  4. _repo.AddAsync(entity)         — write metadata to SQL Server
     └─ if DB fails: _storage.DeleteAsync() then rethrow
  5. return Success(FileResponse.From(entity))
  ↓
Controller checks result.IsSuccess
  → 201 Created + FileResponse body   (success)
  → 400 BadRequest + { error }        (failure)
  ↓ JSON
React hook receives response
  → component renders shareable link
```

---

## Naming Conventions

| What | Pattern | Example |
|---|---|---|
| Controller | `{Resource}Controller` | `FilesController` |
| Service interface | `I{Resource}Service` | `IFileService` |
| Service class | `{Resource}Service` | `FileService` |
| Repository interface | `I{Resource}Repository` | `IFileRepository` |
| Repository class | `{Resource}Repository` | `FileRepository` |
| Request DTO | `{Action}{Resource}Request` | `UploadFileRequest` |
| Response DTO | `{Resource}Response` | `FileResponse` |
| Entity | PascalCase noun | `FileRecord`, `User` |
| React page | `{Purpose}Page` | `UploadPage`, `FilePage` |
| React component | PascalCase | `DropZone`, `ProgressBar` |
| React hook | `use{What}` | `useUpload`, `useFileInfo` |
| API route | lowercase plural | `/api/files` |
| Backend env var | `Section__Key` | `Jwt__Secret`, `ConnectionStrings__Default` |
| Frontend env var | `VITE_*` | `VITE_API_URL` |

---

## Core Design Patterns

### 1. Result\<T\> — No exceptions for business logic

```csharp
// Definition (Application layer)
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

// Service — returns Result, never throws for expected failures
public async Task<Result<FileResponse>> UploadAsync(UploadFileRequest request)
{
    if (request.File.Length == 0)
        return Result<FileResponse>.Failure("File is empty");
    if (request.File.Length > 10 * 1024 * 1024)
        return Result<FileResponse>.Failure("File exceeds 10 MB limit");
    // ...
    return Result<FileResponse>.Success(FileResponse.From(entity));
}

// Controller — maps Result to HTTP
var result = await _fileService.UploadAsync(request);
return result.IsSuccess
    ? CreatedAtAction(nameof(GetFile), new { code = result.Value!.Code }, result.Value)
    : BadRequest(new { error = result.Error });
```

**When to return Failure vs throw:**
- `Failure()` → validation failed, not found, permission denied (expected)
- `throw` → DB down, disk full, unrecoverable (caught by `ErrorHandlingMiddleware` → 500)

### 2. IStorageProvider — swappable file backends

```csharp
// Interface (Application/Interfaces/)
public interface IStorageProvider
{
    Task<string> SaveAsync(IFormFile file, string code);   // returns storage path
    Task<Stream> GetStreamAsync(string storagePath);
    Task DeleteAsync(string storagePath);
}

// Swap in Program.cs based on config:
var provider = builder.Configuration["Storage:Provider"];
if (provider == "azure")
    builder.Services.AddScoped<IStorageProvider, BlobStorageProvider>();
else
    builder.Services.AddScoped<IStorageProvider, LocalStorageProvider>();
```

### 3. IFileRepository — EF Core hidden behind interface

```csharp
// Interface (Application/Interfaces/)
public interface IFileRepository
{
    Task<FileRecord?> GetByCodeAsync(string code);
    Task AddAsync(FileRecord file);
    Task DeleteAsync(FileRecord file);
    Task<IEnumerable<FileRecord>> GetExpiredAsync();
    Task IncrementDownloadCountAsync(string code);     // ExecuteUpdateAsync — 1 round trip
    Task<IEnumerable<FileRecord>> GetByUploaderAsync(Guid userId, int limit = 50);
}
```

### 4. Short Code Generation

```csharp
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
// 62^5 = ~916M possible codes. Use RandomNumberGenerator, NOT new Random().
```

---

## DI Registration (Program.cs — complete)

```csharp
// Database
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Services
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddHostedService<CleanupService>();

// Repositories
builder.Services.AddScoped<IFileRepository, FileRepository>();

// Storage (swap implementation based on config)
builder.Services.AddScoped<IStorageProvider, LocalStorageProvider>();

// JWT Auth
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

// CORS
builder.Services.AddCors(opt => opt.AddPolicy("Frontend", p =>
    p.WithOrigins(builder.Configuration["Frontend:Url"]!)
     .AllowAnyMethod().AllowAnyHeader()));

// Middleware pipeline — ORDER MATTERS
app.UseMiddleware<ErrorHandlingMiddleware>();  // 1. catch all unhandled exceptions
app.UseCors("Frontend");                       // 2. CORS headers
app.UseAuthentication();                       // 3. validate JWT
app.UseAuthorization();                        // 4. enforce [Authorize]
app.MapControllers();                          // 5. route to controllers
```

---

## Entities

### FileRecord (Domain/Entities/FileRecord.cs)

```csharp
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
```

### User (Domain/Entities/User.cs)

```csharp
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";           // unique
    public string PasswordHash { get; set; } = "";    // BCrypt — NEVER plain text
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<FileRecord> Files { get; set; } = [];
}
```

---

## Permission Model

| Endpoint | Auth required | Who |
|---|---|---|
| `POST /api/files` | No | Public |
| `GET /api/files/{code}` | No | Anyone with link |
| `GET /api/files/{code}/meta` | No | Anyone with link |
| `GET /api/files/my-uploads` | Yes | Authenticated user |
| `DELETE /api/files/{code}` | Yes | Owner (`UploaderId == userId`) |
| `POST /api/auth/register` | No | Public |
| `POST /api/auth/login` | No | Public |

### JWT extraction

```csharp
// Extension (API layer)
public static Guid GetUserId(this ClaimsPrincipal user)
    => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

// In controller:
[HttpDelete("{code}")]
[Authorize]
public async Task<IActionResult> Delete(string code)
{
    var userId = User.GetUserId();
    var result = await _fileService.DeleteAsync(code, userId);
    return result.IsSuccess ? NoContent() : Forbid();
}
```

---

## Environment Variables

### Backend

| Variable | Dev value | Prod value |
|---|---|---|
| `ConnectionStrings__Default` | `Server=db,1433;Database=FileShare;User Id=sa;Password=YourPassword123!;TrustServerCertificate=True;` | Real SQL Server connection string |
| `Jwt__Secret` | `dev-secret-min-32-characters-here!` | Real 32+ char secret (GitHub Secret) |
| `Storage__Provider` | `local` | `azure` or `s3` |
| `Storage__Local__Path` | `/app/uploads` | — |
| `Frontend__Url` | `http://localhost:5173` | `https://your-app.onrender.com` |

### Frontend

| Variable | Dev value | Prod value |
|---|---|---|
| `VITE_API_URL` | `http://localhost:5000/api` | `https://api.your-app.onrender.com/api` |

**Rule:** Never commit real secrets. Dev secrets in docker-compose are OK (local only). Prod secrets → GitHub Secrets + Render dashboard.

---

## Adding a New Feature (checklist)

```
1. DESIGN
   □ What data does it need?
   □ Who can use it? (public / authenticated / owner-only)
   □ What are the error cases?
   □ Does it need a new entity / migration?

2. DATABASE (if new entity)
   □ Create entity in Domain/Entities/
   □ Add DbSet<> to AppDbContext
   □ Configure in OnModelCreating
   □ dotnet ef migrations add {Name} --project FileShare.Infrastructure --startup-project FileShare.API
   □ dotnet ef database update (same flags)

3. BACKEND
   □ Create Response DTO in Application/DTOs/
   □ Add method signature to I{Resource}Service interface
   □ Implement in {Resource}Service — return Result<T>
   □ Add repository method if needed
   □ Register new service in Program.cs (if new service)
   □ Add controller action — return correct HTTP status

4. FRONTEND
   □ Add TypeScript interface to src/types/file.types.ts
   □ Create custom hook in src/hooks/use{Name}.ts
   □ Create/modify component or page
   □ Add route in App.tsx if new page

5. TEST
   □ Unit tests: success path + every failure branch (xUnit + Moq)
   □ Integration test: HTTP endpoint end-to-end (WebApplicationFactory)
   □ dotnet test FileShare.sln — all green before committing
```

---

## Hard Rules

```
❌ Business logic in controllers           → belongs in services
❌ throw for expected failures             → return Result<T>.Failure()
❌ EF Core queries in controllers/services → belongs in repositories
❌ File I/O in services                    → belongs in IStorageProvider
❌ Hard-coded connection strings           → environment variables
❌ new Random() for code generation        → RandomNumberGenerator
❌ Plain text passwords                    → BCrypt.HashPassword()
❌ Schema change without migration         → breaks other environments
❌ Returning entity directly from API      → use DTO (FileResponse.From())
❌ StoragePath, PasswordHash in response   → excluded from DTO by design
```

---

## Cross-References

- **Endpoint implementation patterns** → `fileshare-backend/SKILL.md`
- **Schema + EF Core queries** → `fileshare-database/SKILL.md`
- **React components + hooks** → `fileshare-frontend/SKILL.md`
- **Unit + integration tests** → `fileshare-testing/SKILL.md`
- **Docker + CI/CD** → `fileshare-devops/SKILL.md`
