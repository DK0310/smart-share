---
name: fileshare-architecture
description: Use when designing a new feature, understanding the system structure, or deciding where code belongs in the File & Image Sharing Service
---

# Smart Share — Architecture Skill

## System Overview

Smart Share is a full-stack file and image sharing service. Users upload files, receive a short shareable code, and anyone with the link can view or download the file. Files can optionally expire by time or download count, and can be password-protected.

**Architecture style:** Layered N-tier with clean separation — API → Application → Domain → Infrastructure.

---

## Tech Stack

| Layer | Technology | Version | Purpose |
|---|---|---|---|
| Frontend | React + TypeScript + Vite | React 18, Vite 5 | SPA user interface |
| Backend | ASP.NET Core Web API | .NET 8 | REST API, business logic |
| ORM | Entity Framework Core | EF Core 8 | Database abstraction |
| Database (dev) | SQL Server | 2022 / LocalDB | Local development |
| Database (prod) | SQL Server | 2022 | Production (container on Render / Railway) |
| Storage (dev) | Local disk | — | File persistence |
| Storage (prod) | Azure Blob / AWS S3 | — | Cloud file persistence |
| Auth | JWT Bearer tokens | — | 7-day expiration |
| Containers | Docker multi-stage | — | Build and runtime |
| Orchestration | docker-compose | — | Local multi-service |
| CI/CD | GitHub Actions | — | Lint → Test → Build → Push → Deploy |
| Registry | Docker Hub | — | Image storage |
| Hosting | Render | — | Production deployment |

---

## Folder Structure

```
/
├── backend/
│   ├── FileShare.API/                    ← HTTP layer (entry point)
│   │   ├── Controllers/                  ← FilesController, AuthController
│   │   ├── Middleware/                    ← ErrorHandlingMiddleware
│   │   └── Program.cs                    ← DI registration, pipeline config
│   │
│   ├── FileShare.Application/            ← Business logic layer
│   │   ├── Services/                     ← FileService, AuthService, CleanupService
│   │   ├── DTOs/                         ← UploadFileRequest, FileResponse
│   │   └── Interfaces/                   ← IFileService, IStorageProvider
│   │
│   ├── FileShare.Domain/                 ← Domain entities (no dependencies)
│   │   └── Entities/                     ← FileRecord, User
│   │
│   └── FileShare.Infrastructure/         ← Data access and external services
│       ├── Persistence/                  ← AppDbContext, Migrations/
│       ├── Storage/                      ← LocalStorageProvider, BlobStorageProvider
│       └── Repositories/                 ← FileRepository
│
├── frontend/
│   ├── src/
│   │   ├── api/                          ← api.ts (axios instance + interceptors)
│   │   ├── types/                        ← file.types.ts (TypeScript interfaces)
│   │   ├── hooks/                        ← useUpload.ts, useFileInfo.ts
│   │   ├── components/                   ← DropZone, ProgressBar, ImagePreview
│   │   ├── pages/                        ← UploadPage, FilePage, HistoryPage
│   │   └── App.tsx                       ← Router setup
│   ├── vite.config.ts
│   └── .env                              ← VITE_API_URL
│
├── .github/workflows/
│   └── ci-cd.yml                         ← Full CI/CD pipeline
├── docker-compose.yml                    ← Local dev orchestration
└── docs/                                 ← Project documentation
```

### Dependency Direction (inward only)

```
FileShare.API → FileShare.Application → FileShare.Domain
                                      ↘
FileShare.Infrastructure → FileShare.Domain
       ↑
FileShare.API (registers Infrastructure in DI)
```

**Rule:** Domain has ZERO dependencies. Application depends only on Domain. Infrastructure implements interfaces defined in Application. API wires everything together.

---

## Naming Conventions

| What | Pattern | Example | Location |
|---|---|---|---|
| Controller | `{Resource}Controller` | `FilesController` | `API/Controllers/` |
| Service interface | `I{Resource}Service` | `IFileService` | `Application/Interfaces/` |
| Service class | `{Resource}Service` | `FileService` | `Application/Services/` |
| Repository interface | `I{Resource}Repository` | `IFileRepository` | `Application/Interfaces/` |
| Repository class | `{Resource}Repository` | `FileRepository` | `Infrastructure/Repositories/` |
| Request DTO | `{Action}{Resource}Request` | `UploadFileRequest` | `Application/DTOs/` |
| Response DTO | `{Resource}Response` | `FileResponse` | `Application/DTOs/` |
| Entity | PascalCase noun | `FileRecord`, `User` | `Domain/Entities/` |
| React page | `{Purpose}Page` | `UploadPage` | `frontend/src/pages/` |
| React component | PascalCase | `DropZone` | `frontend/src/components/` |
| React hook | `use{What}` | `useUpload` | `frontend/src/hooks/` |
| API route | lowercase plural | `/api/files` | Controller attribute |
| Env variable (backend) | `Section__Key` | `Jwt__Secret` | docker-compose / Render |
| Env variable (frontend) | `VITE_*` | `VITE_API_URL` | `.env` |

---

## Request Flow

Every request follows the same path through exactly these layers:

```
┌─────────────────────────────────────────────────────────────────┐
│  FRONTEND (React)                                               │
│  Component → Hook → axios.post('/files', formData)              │
└─────────────────────────┬───────────────────────────────────────┘
                          │ HTTP (JSON / multipart)
┌─────────────────────────▼───────────────────────────────────────┐
│  CONTROLLER (FilesController)                                    │
│  1. Accept HTTP request ([FromForm], [FromBody], route params)   │
│  2. Call service method                                          │
│  3. Check Result<T>.IsSuccess                                    │
│  4. Return HTTP status code + response body                      │
└─────────────────────────┬───────────────────────────────────────┘
                          │ C# method call
┌─────────────────────────▼───────────────────────────────────────┐
│  SERVICE (FileService)                                           │
│  1. Validate business rules (size, MIME, ownership)              │
│  2. Generate unique code                                         │
│  3. Call IStorageProvider.SaveAsync() — save file bytes           │
│  4. Call IFileRepository.AddAsync() — save metadata              │
│  5. Return Result<FileResponse>.Success(response)                │
│     or Result<FileResponse>.Failure("error message")             │
└──────────┬──────────────────────────────────┬───────────────────┘
           │                                  │
┌──────────▼──────────┐          ┌────────────▼────────────────┐
│  STORAGE PROVIDER    │          │  REPOSITORY                  │
│  SaveAsync(file,code)│          │  AddAsync(entity)            │
│  GetStreamAsync(path)│          │  GetByCodeAsync(code)        │
│  DeleteAsync(path)   │          │  GetExpiredAsync()           │
└──────────┬──────────┘          └────────────┬────────────────┘
           │                                  │
    ┌──────▼──────┐                  ┌────────▼────────┐
    │  Disk/Blob   │                  │  SQL Server or   │
    │  /S3         │                  │  SQL Server      │
    └─────────────┘                  └─────────────────┘
```

---

## Where Does This Logic Go?

Use this decision tree for EVERY piece of logic you write:

```
IS IT...

├─ Accepting/parsing an HTTP request?
│  └─ CONTROLLER
│     [FromForm], [FromBody], route parameters, [Authorize]
│
├─ Returning an HTTP status code?
│  └─ CONTROLLER
│     Ok(), CreatedAtAction(), BadRequest(), NotFound(), NoContent(), Forbid()
│
├─ Validating a business rule?
│  └─ SERVICE
│     File size limits, MIME type checks, expiration logic,
│     ownership verification, duplicate detection
│
├─ Coordinating multiple operations?
│  └─ SERVICE
│     Save to storage THEN save to DB. Delete from storage THEN delete from DB.
│
├─ Generating derived data?
│  └─ SERVICE
│     Short code generation, password hashing, expiry calculation
│
├─ Reading/writing to the database?
│  └─ REPOSITORY
│     EF Core queries, SaveChangesAsync(), ExecuteUpdateAsync()
│
├─ Reading/writing files to disk or cloud?
│  └─ STORAGE PROVIDER
│     File.Create(), BlobClient.UploadAsync()
│
├─ Defining data shape for the API?
│  └─ DTO (Application/DTOs/)
│     UploadFileRequest, FileResponse
│
├─ Defining persistent data shape?
│  └─ ENTITY (Domain/Entities/)
│     FileRecord, User
│
├─ Rendering UI?
│  └─ REACT COMPONENT (frontend/src/components/ or pages/)
│
├─ Making API calls or managing async state?
│  └─ REACT HOOK (frontend/src/hooks/)
│
└─ Defining TypeScript types for API data?
   └─ TYPE FILE (frontend/src/types/)
```

---

## Design Patterns

### 1. Result\<T\> — No Exceptions for Control Flow

Services NEVER throw exceptions for expected failures. They return `Result<T>`.

```csharp
// Definition — in Application layer
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

// Service returns Result<T>
public async Task<Result<FileResponse>> UploadAsync(UploadFileRequest request)
{
    if (request.File.Length == 0)
        return Result<FileResponse>.Failure("File is empty");
    // ... success path
    return Result<FileResponse>.Success(FileResponse.From(entity));
}

// Controller checks IsSuccess and maps to HTTP status
var result = await _service.UploadAsync(request);
return result.IsSuccess
    ? CreatedAtAction(nameof(GetFile), new { code = result.Value.Code }, result.Value)
    : BadRequest(result.Error);
```

**When to use Success vs Failure:**

| Return | When |
|---|---|
| `Success(value)` | Operation completed, data is valid and persisted |
| `Failure("message")` | Validation failed, business rule violated, resource not found, permission denied |

**Exceptions are still used for:** Unexpected infrastructure failures (DB down, disk full). These are caught by `ErrorHandlingMiddleware` and returned as 500.

### 2. Storage Provider — Swappable File Backends

```csharp
// Interface — in Application/Interfaces/
public interface IStorageProvider
{
    Task<string> SaveAsync(IFormFile file, string code);   // Returns storage path
    Task<Stream> GetStreamAsync(string storagePath);       // Returns file stream
    Task DeleteAsync(string storagePath);                  // Removes file
}

// Implementations — in Infrastructure/Storage/
// LocalStorageProvider  → saves to disk (dev)
// BlobStorageProvider   → saves to Azure Blob (prod)
// S3StorageProvider     → saves to AWS S3 (alternative prod)
```

Swap via DI registration in `Program.cs`:
```csharp
// Development
builder.Services.AddScoped<IStorageProvider, LocalStorageProvider>();

// Production (read from config)
if (config["Storage:Provider"] == "azure")
    builder.Services.AddScoped<IStorageProvider, BlobStorageProvider>();
```

### 3. Repository — Data Access Abstraction

```csharp
// Interface — in Application/Interfaces/
public interface IFileRepository
{
    Task<FileRecord?> GetByCodeAsync(string code);
    Task AddAsync(FileRecord file);
    Task DeleteAsync(FileRecord file);
    Task<IEnumerable<FileRecord>> GetExpiredAsync();
    Task IncrementDownloadCountAsync(string code);
}

// Implementation — in Infrastructure/Repositories/
// Uses AppDbContext, EF Core queries, SaveChangesAsync()
```

### 4. Background Cleanup — Hosted Service

```csharp
// In Application/Services/
public class CleanupService : BackgroundService
{
    // Runs every hour
    // Queries for expired + over-limit files
    // Deletes from storage, then from database
}

// Registered in Program.cs:
builder.Services.AddHostedService<CleanupService>();
```

---

## Dependency Injection Registration

All wiring happens in `backend/FileShare.API/Program.cs`:

```csharp
// Services (business logic)
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddHostedService<CleanupService>();

// Repositories (data access)
builder.Services.AddScoped<IFileRepository, FileRepository>();

// Storage (file persistence)
builder.Services.AddScoped<IStorageProvider, LocalStorageProvider>();

// Database
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Auth
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt => { /* JWT config */ });

// CORS
builder.Services.AddCors(opt => opt.AddPolicy("Frontend", p =>
    p.WithOrigins(builder.Configuration["Frontend:Url"]!)
     .AllowAnyMethod().AllowAnyHeader()));
```

**When adding a new service:**
1. Define interface in `Application/Interfaces/I{Name}Service.cs`
2. Implement in `Application/Services/{Name}Service.cs`
3. Register in `Program.cs`: `builder.Services.AddScoped<I{Name}Service, {Name}Service>();`
4. Inject via constructor in Controller or other Service

---

## Authentication & Authorization

### Permission Model

| Endpoint | Method | Auth Required | Permission |
|---|---|---|---|
| `/api/files` | POST | No | Public upload |
| `/api/files/{code}` | GET | No | Anyone with the link |
| `/api/files/{code}/meta` | GET | No | Anyone with the link |
| `/api/files/my-uploads` | GET | Yes | Authenticated user |
| `/api/files/{code}` | DELETE | Yes | Owner only (UploaderId == userId) |
| `/api/auth/register` | POST | No | Public |
| `/api/auth/login` | POST | No | Public |

### JWT Flow

```
1. POST /api/auth/login { email, password }
   → AuthService validates credentials
   → AuthService.GenerateToken(user) returns JWT
   → JWT contains: ClaimTypes.NameIdentifier = user.Id, expires in 7 days

2. Frontend stores token: localStorage.setItem('token', jwt)

3. Every subsequent request:
   axios interceptor adds header → Authorization: Bearer <token>

4. Protected endpoints: [Authorize] attribute on controller action
   → JwtMiddleware validates token automatically
   → Controller extracts user: var userId = User.GetUserId();
   → Service checks ownership: if (file.UploaderId != userId) return Failure("Forbidden");
```

---

## Short Code Generation

Every uploaded file gets a 5-character alphanumeric code (e.g., `mK3pX`):

```csharp
public static string GenerateCode(int length = 5)
{
    const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    return new string(Enumerable.Range(0, length)
        .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
}
```

- Uses `RandomNumberGenerator` (cryptographic) not `Random` (predictable)
- Retry loop: generate → check DB for collision → regenerate if exists
- 62^5 = ~916 million possible codes

---

## Entity Overview

### FileRecord

| Property | Type | Nullable | Purpose |
|---|---|---|---|
| Id | Guid | No | Primary key (NEWID() default) |
| Code | string | No | 5-char shareable identifier (unique, indexed) |
| OriginalFilename | string | No | User's original filename |
| MimeType | string | No | Content type (image/jpeg, application/pdf) |
| SizeBytes | long | No | File size in bytes |
| StoragePath | string | No | Path in storage provider |
| MaxDownloads | int? | Yes | Optional download limit |
| DownloadCount | int | No | Current download count (default 0) |
| ExpiresAt | DateTime? | Yes | Optional expiration timestamp |
| PasswordHash | string? | Yes | BCrypt hash for password protection |
| ThumbnailPath | string? | Yes | Server-generated thumbnail path |
| UploaderId | Guid? | Yes | FK to Users (SET NULL on delete) |
| CreatedAt | DateTime | No | Upload timestamp (UTC) |

**Computed properties (C# only, not in DB):**
- `IsExpired` → `ExpiresAt.HasValue && ExpiresAt < DateTime.UtcNow`
- `IsOverLimit` → `MaxDownloads.HasValue && DownloadCount >= MaxDownloads`
- `IsAvailable` → `!IsExpired && !IsOverLimit`
- `IsImage` → `MimeType.StartsWith("image/")`

### User

| Property | Type | Nullable | Purpose |
|---|---|---|---|
| Id | Guid | No | Primary key |
| Email | string | No | Unique login identifier |
| PasswordHash | string | No | BCrypt hash (NEVER plain text) |
| CreatedAt | DateTime | No | Account creation (UTC) |
| Files | ICollection\<FileRecord\> | No | Navigation property (1-to-many) |

---

## Environment Configuration

### Backend Variables

| Variable | Dev Value | Prod Value | Purpose |
|---|---|---|---|
| `ConnectionStrings__Default` | `Server=db,1433;...` | `Server=db,1433;...` (SQL Server container) | Database connection |
| `Jwt__Secret` | `dev-secret-min-32-chars` | Real secret (GitHub Secret) | Token signing |
| `Storage__Provider` | `local` | `azure` or `s3` | Storage backend |
| `Storage__Local__Path` | `/app/uploads` | — | Local storage path |
| `Frontend__Url` | `http://localhost:5173` | `https://your-app.render.com` | CORS origin |

### Frontend Variables

| Variable | Dev Value | Prod Value | Purpose |
|---|---|---|---|
| `VITE_API_URL` | `http://localhost:5000/api` | `https://api.your-app.render.com` | Backend API base URL |

**Rule:** NEVER commit secrets. Use environment variables in docker-compose (dev) and Render dashboard / GitHub Secrets (prod).

---

## End-to-End: Adding a New Feature

Example: Adding a "report file" feature.

### Step 1 — Design

Answer these questions BEFORE writing code:
- What data does it need? (file code, reporter reason)
- Who can use it? (public — anyone can report)
- What are the error cases? (file not found, already reported)
- Does it need a new entity? (maybe a Report entity, or just a flag)
- What layers are affected? (all of them for a new endpoint)

### Step 2 — Database (if new entity needed)

1. Create entity in `Domain/Entities/Report.cs`
2. Add `DbSet<Report>` to `AppDbContext`
3. Configure in `OnModelCreating`
4. Create migration: `dotnet ef migrations add AddReportTable --project FileShare.Infrastructure --startup-project FileShare.API`
5. Apply: `dotnet ef database update --project FileShare.Infrastructure --startup-project FileShare.API`

### Step 3 — Backend

1. Create `ReportFileRequest` DTO in `Application/DTOs/`
2. Define `IReportService` interface in `Application/Interfaces/`
3. Implement `ReportService` in `Application/Services/`
4. Create `IReportRepository` + `ReportRepository` if needed
5. Register in `Program.cs`: `builder.Services.AddScoped<IReportService, ReportService>();`
6. Add controller action in appropriate controller

### Step 4 — Frontend

1. Add TypeScript types in `types/`
2. Create custom hook `useReport` in `hooks/`
3. Create/modify component or page
4. Add route if new page

### Step 5 — Test

1. Write unit tests for service (xUnit + Moq)
2. Test manually with Postman or frontend
3. Verify all existing tests still pass

---

## Common Mistakes

| ❌ Don't | ✅ Do | Why |
|---|---|---|
| Put business logic in controllers | Put it in services | Controllers only handle HTTP |
| Throw exceptions for business errors | Return `Result<T>.Failure()` | Explicit, no hidden control flow |
| Query DB from controllers | Call repository from service | Layer separation |
| Store files in database | Use IStorageProvider | DB is for metadata, storage for bytes |
| Hard-code connection strings | Use environment variables | Different per environment |
| Skip validation | Validate at service layer | Fail fast, clear error messages |
| Use `new Random()` for codes | Use `RandomNumberGenerator` | Cryptographic randomness |
| Commit secrets to git | Use GitHub Secrets / env vars | Security |
| Skip migrations | Always create migration for schema changes | Reproducible across environments |
| Add features without tests | Write tests for business logic | Catch regressions |

---

## Cross-References

- **Implementing backend endpoints** → `fileshare-backend/SKILL.md`
- **Modifying database schema** → `fileshare-database/SKILL.md`
- **Building React components** → `fileshare-frontend/SKILL.md`
- **Writing unit & integration tests** → `fileshare-testing/SKILL.md`
- **Deploying to production** → `fileshare-devops/SKILL.md`
