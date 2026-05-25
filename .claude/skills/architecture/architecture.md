---
name: architecture
description: Project structure, naming conventions, and data flow for AMD201 File & Image Sharing Service. Read this first before any other skill.
---

# Project Architecture — File & Image Sharing Service

## Tech Stack
- **Backend**: ASP.NET Core 8 Web API
- **Frontend**: React 18 + TypeScript + Vite
- **Database**: PostgreSQL + EF Core 8
- **Storage**: Local disk (dev) → Azure Blob / AWS S3 (merit)
- **Auth**: JWT Bearer tokens

---

## Folder Structure

```
/
├── backend/
│   ├── FileShare.API/
│   │   ├── Controllers/          # FilesController, AuthController
│   │   ├── Middleware/           # ErrorHandlingMiddleware, JwtMiddleware
│   │   └── Program.cs
│   ├── FileShare.Application/
│   │   ├── Services/             # FileService, AuthService, CleanupService
│   │   ├── DTOs/                 # Request/Response records
│   │   └── Interfaces/           # IFileService, IStorageProvider
│   ├── FileShare.Domain/
│   │   └── Entities/             # FileRecord, User
│   └── FileShare.Infrastructure/
│       ├── Persistence/          # AppDbContext, Migrations
│       ├── Storage/              # LocalStorageProvider, BlobStorageProvider
│       └── Repositories/         # FileRepository
│
├── frontend/
│   ├── src/
│   │   ├── components/           # DropZone, ProgressBar, ImagePreview
│   │   ├── pages/                # UploadPage, FilePage, HistoryPage
│   │   ├── api/                  # api.ts (axios wrapper)
│   │   ├── hooks/                # useUpload, useFileInfo
│   │   └── types/                # file.types.ts
│   └── vite.config.ts
│
├── .github/workflows/
│   └── ci-cd.yml
└── docker-compose.yml
```

---

## Naming Conventions

| Layer | Convention | Example |
|---|---|---|
| Controller | `{Resource}Controller` | `FilesController` |
| Service | `{Resource}Service` | `FileService` |
| Repository | `{Resource}Repository` | `FileRepository` |
| DTO (request) | `{Action}{Resource}Request` | `UploadFileRequest` |
| DTO (response) | `{Resource}Response` | `FileResponse` |
| Entity | PascalCase noun | `FileRecord` |
| React component | PascalCase | `DropZone` |
| React hook | `use{Name}` | `useUpload` |
| API route | kebab-case plural | `/api/files` |

---

## Request Flow

```
React (fetch/axios)
  → FilesController          [validate input, call service]
    → FileService            [business logic, generate short code]
      → IStorageProvider     [save bytes to disk/cloud]
      → FileRepository       [save metadata to DB]
    ← FileResponse           [code, url, expires_at]
  ← JSON response
```

---

## Dependency Injection Pattern

Register in `Program.cs`:
```csharp
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<IFileRepository, FileRepository>();
builder.Services.AddScoped<IStorageProvider, LocalStorageProvider>(); // swap for BlobStorageProvider in prod
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
```

---

## Short Code Generation

All resources use a 5-char alphanumeric code (e.g. `mK3pX`):
```csharp
public static string GenerateCode(int length = 5)
{
    const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    return new string(Enumerable.Range(0, length)
        .Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
}
```
Retry on collision (check DB before saving).

---

## Environment Variables

```env
# backend/.env
CONNECTIONSTRINGS__DEFAULT=Host=localhost;Database=fileshare;Username=postgres;Password=postgres
JWT__SECRET=your-secret-key-min-32-chars
STORAGE__PROVIDER=local                  # local | azure | s3
STORAGE__LOCAL__PATH=wwwroot/uploads
STORAGE__AZURE__CONNECTIONSTRING=...
FRONTEND__URL=http://localhost:5173      # for CORS
```

---

## CORS Policy

```csharp
builder.Services.AddCors(opt => opt.AddPolicy("Frontend", p =>
    p.WithOrigins(builder.Configuration["Frontend:Url"]!)
     .AllowAnyMethod()
     .AllowAnyHeader()));
```

---

## Cross-Skill References
- DB schema → see `DATABASE_SCHEMA/SKILL.md`
- Backend patterns → see `BACKEND_PATTERNS/SKILL.md`
- Frontend patterns → see `FRONTEND_PATTERNS/SKILL.md`
- Docker & CI/CD → see `DEVOPS/SKILL.md`
