# SmartShare — Development TODO

> **Project:** File & Image Sharing Service (AMD201 — Topic 03)
> **Stack:** ASP.NET Core 8 · React 18 + TypeScript + Vite · SQL Server · Docker · GitHub Actions

---

## Phase 1 — Project Scaffolding & Foundation

> Goal: Get the solution structure, dependency wiring, and database running so every future task has a place to land.

### Backend Solution Setup

- [x] Create `backend/` directory with the .NET solution file (`FileShare.slnx`)
- [x] Create `FileShare.API` project (ASP.NET Core Web API)
- [x] Create `FileShare.Application` class library
- [x] Create `FileShare.Domain` class library (zero external dependencies)
- [x] Create `FileShare.Infrastructure` class library
- [x] Wire project references following dependency direction:
  - `API → Application → Domain`
  - `Infrastructure → Domain` (implements Application interfaces)
  - `API` registers Infrastructure via DI

### Domain Entities

- [x] Create `FileRecord` entity in `Domain/Entities/FileRecord.cs`
  - All properties: Id, Code, OriginalFilename, MimeType, SizeBytes, StoragePath, MaxDownloads, DownloadCount, ExpiresAt, PasswordHash, ThumbnailPath, UploaderId, CreatedAt
  - Computed properties: IsExpired, IsOverLimit, IsAvailable, IsImage
- [x] Create `User` entity in `Domain/Entities/User.cs`
  - Properties: Id, Email, PasswordHash, CreatedAt
  - Navigation: `ICollection<FileRecord> Files`

### Database & EF Core

- [x] Install `Microsoft.EntityFrameworkCore.SqlServer` in Infrastructure
- [x] Create `AppDbContext` in `Infrastructure/Persistence/`
- [x] Add `DbSet<FileRecord>` and `DbSet<User>`
- [x] Configure `OnModelCreating` — unique index on `Code`, FK for `UploaderId` with SET NULL
- [x] Create initial migration
- [ ] Verify database creation with `dotnet ef database update`

### Application Layer Interfaces

- [x] Create `Result<T>` class in Application (Success / Failure pattern)
- [x] Create `IFileService` interface
- [x] Create `IFileRepository` interface (GetByCodeAsync, AddAsync, DeleteAsync, GetExpiredAsync, IncrementDownloadCountAsync, GetByUploaderAsync)
- [x] Create `IStorageProvider` interface (SaveAsync, GetStreamAsync, DeleteAsync)
- [x] Create `IAuthService` interface

### DI Registration (Program.cs)

- [x] Register `AppDbContext` with SQL Server connection string
- [x] Register services: `IFileService`, `IAuthService`
- [x] Register repository: `IFileRepository`
- [x] Register storage: `IStorageProvider → LocalStorageProvider`
- [x] Configure JWT Bearer authentication
- [x] Configure CORS policy for frontend origin
- [x] Set up middleware pipeline in correct order: ErrorHandling → CORS → Auth → Authorization → MapControllers

### Frontend Setup

- [x] Scaffold React 18 + TypeScript project with Vite in `frontend/`
- [x] Configure `VITE_API_URL` environment variable
- [x] Set up axios instance with JWT interceptor in `src/api/api.ts`
- [x] Create folder structure: `api/`, `types/`, `hooks/`, `components/`, `pages/`
- [x] Install routing library (React Router)


---

## Phase 2 — Core Upload & Download Flow (Pass-Level)

> Goal: A user can upload a file, get a short link, and anyone with the link can download it. This is the critical end-to-end path.

### Backend — File Upload

- [ ] Create `UploadFileRequest` DTO (File, MaxDownloads?, ExpiresAt?, Password?)
- [ ] Create `FileResponse` DTO (Code, OriginalFilename, MimeType, SizeBytes, DownloadCount, MaxDownloads, ExpiresAt, CreatedAt, IsImage, ThumbnailUrl)
  - Exclude StoragePath and PasswordHash from response
- [ ] Implement `LocalStorageProvider` in `Infrastructure/Storage/`
  - `SaveAsync` — write file to `wwwroot/uploads/{code}/{filename}`
  - `GetStreamAsync` — read file from disk
  - `DeleteAsync` — remove file from disk
- [ ] Implement `FileRepository` in `Infrastructure/Repositories/`
  - All methods from `IFileRepository`
- [ ] Implement `FileService.UploadAsync()`
  1. Validate file (not empty, ≤ 10 MB, valid MIME)
  2. Generate unique 5-char short code (retry loop)
  3. Save file bytes via `IStorageProvider`
  4. Save metadata via `IFileRepository`
  5. Rollback storage if DB save fails
  6. Return `Result<FileResponse>`
- [ ] Create `FilesController` with `POST /api/files` endpoint

### Backend — File Download / Preview

- [ ] Implement `FileService.GetFileAsync(code)`
  - Check existence, expiry, download limit
  - Increment download count
  - Return file stream + metadata
- [ ] Implement `GET /api/files/{code}` — stream file with correct Content-Type
- [ ] Implement `GET /api/files/{code}/meta` — return metadata only (FileResponse)

### Backend — Error Handling

- [ ] Create `ErrorHandlingMiddleware` in `API/Middleware/`
  - Catch unhandled exceptions → 500 with generic error JSON
  - Log full exception details

### Frontend — Upload Page

- [ ] Create `FileTypes` TypeScript interface in `src/types/file.types.ts`
- [ ] Create `UploadPage` component
  - Simple file input (drag-and-drop comes later)
  - Optional fields: max downloads, expiry time
  - Upload button → calls API
  - On success: display shareable link, copy-to-clipboard button
- [ ] Create `useUpload` hook — manages upload state, calls `POST /api/files`

### Frontend — File Download / Preview Page

- [ ] Create `FilePage` component
  - Fetch file metadata from `/api/files/{code}/meta`
  - If image: render inline preview
  - Download button for all file types
  - Show file info: name, size, type, downloads remaining
- [ ] Create `useFileInfo` hook — fetches file metadata by code

### Frontend — Routing

- [ ] Set up React Router with routes:
  - `/` → `UploadPage`
  - `/f/:code` → `FilePage`
- [ ] Create basic app layout / navigation

### End-to-End Verification

- [ ] Test: Upload a file → receive short code → open link → download file
- [ ] Test: Upload an image → preview renders in browser
- [ ] Test: Upload empty file → 400 error
- [ ] Test: Upload >10 MB file → 400 error
- [ ] Test: Access expired file → appropriate error

---

## Phase 3 — Authentication & User Features (Pass-Level)

> Goal: Users can register, log in, see their upload history, and delete their own files.

### Backend — Auth

- [ ] Create `RegisterRequest` DTO (Email, Password)
- [ ] Create `LoginRequest` DTO (Email, Password)
- [ ] Create `AuthResponse` DTO (Token, Email)
- [ ] Implement `AuthService`
  - `RegisterAsync` — hash password with BCrypt, save user, return JWT
  - `LoginAsync` — verify credentials, return JWT
  - JWT generation with `ClaimTypes.NameIdentifier` = UserId, 7-day expiry
- [ ] Create `AuthController`
  - `POST /api/auth/register`
  - `POST /api/auth/login`
- [ ] Create `GetUserId()` ClaimsPrincipal extension method

### Backend — User-Scoped Endpoints

- [ ] Update `FileService.UploadAsync` to optionally accept `userId` (set `UploaderId`)
- [ ] Implement `FileService.GetUserFilesAsync(userId)`
- [ ] Implement `FileService.DeleteAsync(code, userId)` — owner-only check
- [ ] Add `GET /api/files/my-uploads` endpoint `[Authorize]`
- [ ] Add `DELETE /api/files/{code}` endpoint `[Authorize]` — owner only

### Frontend — Auth

- [ ] Create `LoginPage` component
- [ ] Create `RegisterPage` component
- [ ] Store JWT in localStorage
- [ ] Update axios interceptor to attach `Authorization: Bearer <token>` header
- [ ] Add auth state management (context or simple state)
- [ ] Add login/logout/register navigation links

### Frontend — Upload History

- [ ] Create `HistoryPage` component
  - List user's uploaded files
  - Show file info, download count, expiry status
  - Delete button for each file
- [ ] Create `useHistory` hook — fetches `GET /api/files/my-uploads`
- [ ] Add `/history` route (protected — redirect to login if unauthenticated)

### Verification

- [ ] Test: Register → login → upload → file appears in history
- [ ] Test: Delete own file → removed from history & storage
- [ ] Test: Delete another user's file → 403 Forbidden
- [ ] Test: Access `/my-uploads` without JWT → 401

---

## Phase 4 — Cleanup Service & Input Validation Hardening

> Goal: Expired files are automatically cleaned up. All edge cases are handled gracefully.

### Backend — Cleanup Background Service

- [ ] Implement `CleanupService` as `BackgroundService` / `IHostedService`
  - Run on a timer (e.g., every hour or daily)
  - Query `IFileRepository.GetExpiredAsync()` — files past ExpiresAt or over MaxDownloads
  - Delete file bytes via `IStorageProvider`
  - Delete metadata via `IFileRepository`
  - Log cleanup results

### Input Validation Hardening

- [ ] Validate MIME types — allow a configurable whitelist
- [ ] Validate filenames — sanitize or reject malicious names
- [ ] Rate limiting on upload endpoint (optional but recommended)
- [ ] Ensure short code uniqueness is robust under concurrent uploads

### Frontend — Error Handling & UX

- [ ] Display user-friendly error messages for all API errors
- [ ] Show loading states for upload, download, history fetch
- [ ] Handle network errors gracefully
- [ ] Add toast notifications for success/error feedback

---

## Phase 5 — Merit Features

> Goal: Achieve Merit grade (7–8.5) by adding two Merit-level features plus CI/CD enhancements.

### Merit Feature 1: Real-Time Upload Progress Bar

- [ ] Create `ProgressBar` component with animated fill
- [ ] Update `useUpload` hook to use `XMLHttpRequest` upload progress event (or `axios` `onUploadProgress`)
- [ ] Display percentage, uploaded/total bytes, estimated time remaining
- [ ] Smooth animation on progress updates

### Merit Feature 2: Cloud Storage Integration (Azure Blob / AWS S3)

- [ ] Implement `BlobStorageProvider` (Azure) or `S3StorageProvider` (AWS) in `Infrastructure/Storage/`
  - `SaveAsync` — upload to cloud, return storage key
  - `GetStreamAsync` — generate signed/temporary URL or stream
  - `DeleteAsync` — remove from cloud
- [ ] Update `Program.cs` to swap provider based on `Storage:Provider` config
- [ ] Serve files via signed/temporary URLs (not direct public links)
- [ ] Update environment variables for cloud credentials

### CI/CD Enhancements (Merit Requirement)

- [ ] Add linting / static analysis step to GitHub Actions pipeline (e.g., `dotnet format --verify-no-changes`)
- [ ] Use multi-stage Docker build to reduce image size

### Unit Tests (Pass/Merit Requirement)

- [ ] Create test project `FileShare.Tests`
- [ ] Unit tests for `FileService` — success path + every failure branch
  - Empty file, oversized file, invalid MIME, expired file, over-limit file
- [ ] Unit tests for `AuthService` — register, login, duplicate email, wrong password
- [ ] Unit tests for short code generation
- [ ] Use xUnit + Moq for mocking interfaces

### Integration Tests (Merit Requirement)

- [ ] Set up `WebApplicationFactory` for integration tests
- [ ] Test `POST /api/files` end-to-end (HTTP → DB → storage)
- [ ] Test `GET /api/files/{code}` end-to-end
- [ ] Test auth flow end-to-end (register → login → authorized endpoint)
- [ ] Ensure `dotnet test FileShare.sln` passes all green

---

## Phase 6 — Distinction Features

> Goal: Achieve Distinction grade (9–10) by adding at least one Distinction-level feature.

### Distinction Feature 1: Password-Protected Files

- [ ] Update `UploadFileRequest` to accept optional `Password` field
- [ ] In `FileService.UploadAsync`: if password provided, hash with BCrypt and store in `FileRecord.PasswordHash`
- [ ] In `FileService.GetFileAsync`: if file has PasswordHash, require password verification before allowing download
- [ ] Create `UnlockFileRequest` DTO
- [ ] Add `POST /api/files/{code}/unlock` endpoint — verify password, return download token or stream
- [ ] Frontend: on `FilePage`, if file is password-protected, show password input modal before download
- [ ] Test: upload with password → download without password fails → download with correct password succeeds

### Distinction Feature 2: Server-Side Image Thumbnails

- [ ] Install `SixLabors.ImageSharp` NuGet package
- [ ] In `FileService.UploadAsync`: if file is an image, generate thumbnail (e.g., 200×200 max)
- [ ] Save thumbnail via `IStorageProvider`, store path in `FileRecord.ThumbnailPath`
- [ ] Add `GET /api/files/{code}/thumbnail` endpoint
- [ ] Frontend: on `FilePage`, display thumbnail instead of loading full image
- [ ] Cleanup service: delete thumbnails alongside expired files

---

## Phase 7 — DevOps & Deployment

> Goal: Application is containerized, CI/CD pipeline runs automatically, and the app is deployed to the cloud.

### Docker

- [ ] Create `backend/Dockerfile` — multi-stage build (restore → build → publish → runtime)
- [ ] Create `frontend/Dockerfile` — multi-stage build (install → build → nginx serve)
- [ ] Create `docker-compose.yml` for local development
  - Services: `api`, `frontend`, `db` (SQL Server 2022)
  - Volumes for database persistence and file uploads
  - Environment variables for dev config
- [ ] Test: `docker-compose up` → full stack runs locally

### GitHub Actions CI/CD

- [ ] Create `.github/workflows/ci-cd.yml`
- [ ] Pipeline stages:
  1. **Lint** — `dotnet format --verify-no-changes`
  2. **Test** — `dotnet test FileShare.sln`
  3. **Build** — Docker build for backend and frontend
  4. **Push** — Push images to Docker Hub
  5. **Deploy** — Auto-deploy to Render / Railway / Azure App Service
- [ ] Configure GitHub Secrets for:
  - Docker Hub credentials
  - Deployment platform credentials
  - Production `Jwt__Secret`
  - Production `ConnectionStrings__Default`
  - Cloud storage credentials (if using)
- [ ] Test: Push to `main` → pipeline runs → app deploys

### Production Environment

- [ ] Provision production SQL Server (or use hosted DB)
- [ ] Configure production environment variables on deployment platform
- [ ] Set up production CORS to allow only the production frontend URL
- [ ] Verify live deployment URL is accessible

---

## Phase 8 — Polish & Presentation Prep

> Goal: Clean code, great UX, thorough README, and a confident presentation.

### Frontend Polish

- [ ] Implement drag-and-drop upload with `DropZone` component
- [ ] Add responsive design — works on mobile and desktop
- [ ] Add micro-animations and transitions
- [ ] Dark mode or modern theme with polished typography
- [ ] File type icons for non-image files
- [ ] Copy-to-clipboard for shareable links with visual feedback

### Code Quality

- [ ] Review all code for Hard Rules compliance (no business logic in controllers, no raw EF in services, etc.)
- [ ] Ensure all DTOs exclude sensitive fields (StoragePath, PasswordHash)
- [ ] Add XML doc comments to public API methods
- [ ] Remove all `Console.WriteLine` — use proper `ILogger`
- [ ] Ensure no secrets are committed to git

### README.md

- [ ] Project description and overview
- [ ] Architecture diagram or description
- [ ] Setup / installation instructions (local dev with Docker)
- [ ] Environment variable documentation
- [ ] API endpoint reference
- [ ] Link to live deployment
- [ ] Team member contributions

### Presentation Prep

- [ ] Prepare slide deck covering:
  - Live demo of deployed application
  - Architecture overview diagram
  - CI/CD pipeline walkthrough (show a live push → deploy)
  - Code walkthrough of most interesting technical piece
  - Challenges faced and how they were solved
- [ ] Practice demo flow — upload → share → download → history → delete
- [ ] Ensure every team member has a speaking part
- [ ] Test live URL is working on presentation day

### Individual Report

- [ ] Each member writes 500+ word report covering:
  - Personal contributions
  - Technical challenge and resolution
  - Learnings from the project
  - Honest peer assessment of team members

---

## Quick Reference — Priority Order

| Priority | Phase | What | Grade Impact |
|----------|-------|------|-------------|
| 🔴 P0 | 1–2 | Scaffolding + core upload/download flow | **Must have** for Pass |
| 🔴 P0 | 3 | Auth + user features + history | **Must have** for Pass |
| 🟡 P1 | 7 | Docker + CI/CD pipeline + deployment | **Must have** for Pass |
| 🟡 P1 | 4 | Cleanup service + validation | Pass completeness |
| 🟢 P2 | 5 | Merit features (progress bar + cloud storage + tests) | Merit (7–8.5) |
| 🔵 P3 | 6 | Distinction features (password protect + thumbnails) | Distinction (9–10) |
| 🔵 P3 | 8 | Polish, README, presentation, report | Final grade |

> **Tip from the assignment:** *"Start with local disk storage and a simple file input. Get upload → short link → download working end-to-end. Only add cloud storage and drag-and-drop once the core pipeline is solid."*
