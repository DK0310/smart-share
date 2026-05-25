---
name: database-schema
description: SQL Server schema and EF Core entity definitions for File & Image Sharing Service. Use when creating migrations, writing queries, or defining entities.
---

# Database Schema — File & Image Sharing Service

## Tech
- **Database**: SQL Server (LocalDB for dev, SQL Server 2022 for prod/Docker)
- **ORM**: EF Core 8 with `Microsoft.EntityFrameworkCore.SqlServer`
- **ID strategy**: `UNIQUEIDENTIFIER` (Guid) with `NEWID()` default — standard .NET enterprise pattern

---

## Tables Overview

| Table | Purpose |
|---|---|
| `Users` | Optional auth (uploader accounts) |
| `Files` | File metadata — core table |

---

## SQL Schema

```sql
CREATE TABLE Users (
    Id            UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    Email         NVARCHAR(255)    NOT NULL,
    PasswordHash  NVARCHAR(MAX)    NOT NULL,
    CreatedAt     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_Users_Email UNIQUE (Email)
);

CREATE TABLE Files (
    Id                UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
    Code              NVARCHAR(10)     NOT NULL,
    OriginalFilename  NVARCHAR(260)    NOT NULL,
    MimeType          NVARCHAR(127)    NOT NULL,
    SizeBytes         BIGINT           NOT NULL,
    StoragePath       NVARCHAR(MAX)    NOT NULL,
    MaxDownloads      INT              NULL,
    DownloadCount     INT              NOT NULL DEFAULT 0,
    ExpiresAt         DATETIME2        NULL,
    PasswordHash      NVARCHAR(MAX)    NULL,         -- Distinction: password-protected files
    ThumbnailPath     NVARCHAR(MAX)    NULL,         -- Distinction: server-side thumbnails
    UploaderId        UNIQUEIDENTIFIER NULL REFERENCES Users(Id) ON DELETE SET NULL,
    CreatedAt         DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    CONSTRAINT UQ_Files_Code UNIQUE (Code)
);

CREATE INDEX IX_Files_Code       ON Files(Code);
CREATE INDEX IX_Files_ExpiresAt  ON Files(ExpiresAt) WHERE ExpiresAt IS NOT NULL;
CREATE INDEX IX_Files_UploaderId ON Files(UploaderId) WHERE UploaderId IS NOT NULL;
```

---

## EF Core Entities

### FileRecord.cs

```csharp
public class FileRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string OriginalFilename { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = "";
    public int? MaxDownloads { get; set; }
    public int DownloadCount { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? PasswordHash { get; set; }    // Distinction
    public string? ThumbnailPath { get; set; }   // Distinction
    public Guid? UploaderId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? Uploader { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt < DateTime.UtcNow;
    public bool IsOverLimit => MaxDownloads.HasValue && DownloadCount >= MaxDownloads;
    public bool IsAvailable => !IsExpired && !IsOverLimit;
    public bool IsImage => MimeType.StartsWith("image/");
}
```

### User.cs

```csharp
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<FileRecord> Files { get; set; } = [];
}
```

---

## AppDbContext

```csharp
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<FileRecord> Files => Set<FileRecord>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<FileRecord>(e =>
        {
            e.ToTable("Files");
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).HasDefaultValueSql("NEWID()");
            e.HasIndex(f => f.Code).IsUnique();
            e.Property(f => f.Code).HasMaxLength(10).IsRequired();
            e.Property(f => f.OriginalFilename).HasMaxLength(260).IsRequired();
            e.Property(f => f.MimeType).HasMaxLength(127).IsRequired();
            e.Property(f => f.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            e.HasOne(f => f.Uploader)
             .WithMany(u => u.Files)
             .HasForeignKey(f => f.UploaderId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasDefaultValueSql("NEWID()");
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(255).IsRequired();
            e.Property(u => u.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });
    }
}
```

---

## Migrations

```bash
# From backend/ directory
dotnet ef migrations add InitialCreate --project FileShare.Infrastructure --startup-project FileShare.API
dotnet ef database update --project FileShare.Infrastructure --startup-project FileShare.API
```

---

## Key Queries

```csharp
// Get expired OR over-limit files for cleanup
var stale = await _db.Files
    .Where(f => (f.ExpiresAt != null && f.ExpiresAt < DateTime.UtcNow)
             || (f.MaxDownloads != null && f.DownloadCount >= f.MaxDownloads))
    .ToListAsync();

// Get files by uploader for history page
var userFiles = await _db.Files
    .Where(f => f.UploaderId == userId)
    .OrderByDescending(f => f.CreatedAt)
    .Take(50)
    .ToListAsync();

// Increment download count (atomic)
await _db.Files
    .Where(f => f.Code == code)
    .ExecuteUpdateAsync(s => s.SetProperty(f => f.DownloadCount, f => f.DownloadCount + 1));
```

---

## NuGet Package

```bash
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
dotnet add package Microsoft.EntityFrameworkCore.Tools
```

Register in `Program.cs`:
```csharp
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
```

---

## Connection Strings

**Local dev (LocalDB):**
```
Server=(localdb)\mssqllocaldb;Database=FileShare;Trusted_Connection=True;
```

**Local dev (SQL Server Express):**
```
Server=localhost,1433;Database=FileShare;User Id=sa;Password=YourPassword123!;TrustServerCertificate=True;
```

**Docker (SQL Server container):**
```
Server=db,1433;Database=FileShare;User Id=sa;Password=YourPassword123!;TrustServerCertificate=True;
```

**docker-compose db service:**
```yaml
db:
  image: mcr.microsoft.com/mssql/server:2022-latest
  environment:
    SA_PASSWORD: "YourPassword123!"
    ACCEPT_EULA: "Y"
  ports: ["1433:1433"]
```
