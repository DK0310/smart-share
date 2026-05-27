---
name: fileshare-database
description: Use when modifying database schema, writing queries, or implementing migrations
---

# FileShare Database Skill

## Overview

The FileShare database uses SQL Server with Entity Framework Core 8. Data models are defined as C# entities that map to tables via EF Core configuration. Every schema change requires a migration to maintain consistency between code and database.

**Core principle:** Database structure should enforce data integrity at the database level. Migrations make schema changes reproducible across environments.

## When to Use

**Use this skill WHEN:**
- Adding a new table or entity
- Modifying table structure (add/rename/remove columns)
- Creating indexes for performance
- Writing complex queries
- Setting up database in new environment
- Rolling back schema changes

**Use ESPECIALLY when:**
- Unsure about data relationships
- Need to query related entities
- Performance issues with queries
- Adding multi-step transactions

**Don't skip when:**
- "Just adding one column" (migrations needed)
- Moving between environments (LocalDB → SQL Server)
- Changing primary key strategy (consistency matters)

## The Iron Law

```
MIGRATIONS FIRST, SCHEMA CHANGES SECOND
```

Write migration before deploying schema change. Never manually edit database and hope code matches.

---

## Tech Stack

| Component | Version | Purpose |
|---|---|---|
| **Engine** | SQL Server 2022 | Database management |
| **ORM** | EF Core 8 | C# to SQL mapping |
| **Dev Database** | LocalDB or Docker | Local development |
| **Prod Database** | SQL Server 2022 | Production (container on Render / Railway) |

---

## Entity Definition Workflow

### Phase 1: Design the Entity

**Before writing code:**

```
□ What data needs to persist?
□ What is the primary key? (UUID/Guid in FileShare)
□ What relationships exist? (1-to-many, many-to-many)
□ What constraints? (unique code, non-null fields)
□ What computed properties? (IsExpired, IsAvailable)
□ What timestamps? (CreatedAt, UpdatedAt)
```

### Phase 2: Create the Entity Class

```
1. Define properties (Id, Code, OriginalFilename, etc.)
2. Add relationships (Uploader navigation property)
3. Add computed properties (IsExpired, IsImage)
4. Add timestamps (CreatedAt)
```

### Phase 3: Configure in DbContext

```
1. Add DbSet<Entity> property
2. Configure in OnModelCreating():
   - Specify table name
   - Set primary key
   - Configure indexes
   - Set up relationships
```

### Phase 4: Create Migration

```
1. Run: dotnet ef migrations add DescriptiveName
2. Review generated migration file
3. Adjust if needed (EF might not infer correctly)
4. Run: dotnet ef database update
```

---

## Table Design Decision Tree

```
DESIGNING A NEW TABLE:

  ┌─ What identifies each row?
  │  └─ Primary Key: Guid with NEWID() default
  │
  ├─ Does this entity relate to others?
  │  └─ YES → Add foreign key column + navigation property
  │
  ├─ Need to find records quickly?
  │  └─ YES → Create index on search column
  │
  ├─ Should this field be unique?
  │  └─ YES → Add unique constraint (Code is unique)
  │
  ├─ Should this field be required?
  │  └─ YES → NOT NULL in database
  │
  ├─ Need to track when created?
  │  └─ YES → Add CreatedAt with GETUTCDATE() default
  │
  └─ Need to track modifications?
     └─ YES → Add UpdatedAt with trigger
```

---

## Core Entities

### FileRecord Entity

```
Purpose: Represents a shared file

Key properties:
  - Code: 5-char shareable identifier (unique, indexed)
  - OriginalFilename: What user uploaded
  - StoragePath: Where file is stored (disk/cloud)
  - MaxDownloads: Optional limit
  - DownloadCount: How many times viewed
  - ExpiresAt: Optional expiration date
  - UploaderId: Who uploaded (FK to Users)
  
Computed properties:
  - IsExpired: ExpiresAt < now
  - IsOverLimit: DownloadCount >= MaxDownloads
  - IsAvailable: !IsExpired && !IsOverLimit
  - IsImage: MIME type starts with "image/"
```

### User Entity

```
Purpose: Represents account holder

Key properties:
  - Email: Unique identifier
  - PasswordHash: BCrypt hash (never plain text)
  - CreatedAt: Account creation date
  
Relationships:
  - Files: One-to-many (user can upload many files)
```

---

## EF Core Configuration Workflow

### Step 1: Define Entity

```csharp
public class FileRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string OriginalFilename { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime? ExpiresAt { get; set; }
    
    // Computed property
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt < DateTime.UtcNow;
    
    // Foreign key
    public Guid? UploaderId { get; set; }
    
    // Navigation property
    public User? Uploader { get; set; }
}
```

### Step 2: Configure in DbContext

```csharp
protected override void OnModelCreating(ModelBuilder b)
{
    b.Entity<FileRecord>(e =>
    {
        // Table name
        e.ToTable("Files");
        
        // Primary key
        e.HasKey(f => f.Id);
        e.Property(f => f.Id).HasDefaultValueSql("NEWID()");
        
        // Unique constraint
        e.HasIndex(f => f.Code).IsUnique();
        
        // Timestamp
        e.Property(f => f.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");
        
        // Relationship
        e.HasOne(f => f.Uploader)
         .WithMany(u => u.Files)
         .HasForeignKey(f => f.UploaderId)
         .OnDelete(DeleteBehavior.SetNull);
    });
}
```

### Step 3: Add DbSet

```csharp
public class AppDbContext : DbContext
{
    public DbSet<FileRecord> Files => Set<FileRecord>();
    public DbSet<User> Users => Set<User>();
}
```

---

## Query Patterns

### Decision: What query do I need?

```
Getting a single record:
  - GetByCodeAsync(code) → FirstOrDefaultAsync()
  
Getting multiple records:
  - GetExpiredAsync() → Where() + ToListAsync()
  
Getting with relationships:
  - Include(f => f.Uploader) → loads related user
  
Filtering by criteria:
  - Where(f => f.UploaderId == userId) → ToListAsync()
```

### Performance tip: N+1 queries

```csharp
// BAD: N+1 problem (one query per item)
var files = await _db.Files.ToListAsync();
foreach (var f in files)
{
    var user = await _db.Users.FirstAsync(u => u.Id == f.UploaderId);
    // Now you have user
}

// GOOD: Include related data (one query)
var files = await _db.Files
    .Include(f => f.Uploader)
    .ToListAsync();
```

### Common queries:

```csharp
// Get by code
var file = await _db.Files
    .FirstOrDefaultAsync(f => f.Code == code);

// Get expired files
var expired = await _db.Files
    .Where(f => (f.ExpiresAt != null && f.ExpiresAt < DateTime.UtcNow)
             || (f.MaxDownloads != null && f.DownloadCount >= f.MaxDownloads))
    .ToListAsync();

// Get user's files
var userFiles = await _db.Files
    .Where(f => f.UploaderId == userId)
    .OrderByDescending(f => f.CreatedAt)
    .Take(50)
    .ToListAsync();

// Atomic increment
await _db.Files
    .Where(f => f.Code == code)
    .ExecuteUpdateAsync(s => s
        .SetProperty(f => f.DownloadCount, f => f.DownloadCount + 1));
```

---

## Migration Workflow

### Decision: When do I need a migration?

```
Add new table:
  - Create entity
  - Configure DbContext
  - Create migration: add InitialCreate
  
Add column:
  - Add property to entity
  - Create migration: add AddNewColumn
  
Remove column:
  - Remove property (or mark [NotMapped])
  - Create migration: add RemoveOldColumn
  
Change constraint:
  - Update configuration in OnModelCreating
  - Create migration: add ChangeConstraint
```

### Creating a migration:

```bash
# Generate migration code (doesn't apply yet)
dotnet ef migrations add AddThumbnailSupport \
  --project FileShare.Infrastructure \
  --startup-project FileShare.API

# Review the generated migration file
cat Migrations/[timestamp]_AddThumbnailSupport.cs

# Apply to database
dotnet ef database update
```

### Rolling back:

```bash
# Remove last migration (if not applied to prod)
dotnet ef migrations remove

# Revert database to previous migration
dotnet ef database update [previous-migration-name]
```

---

## Connection Strings

### Decision: Which database am I using?

```
Development (LocalDB):
  Server=(localdb)\mssqllocaldb;Database=FileShare;Trusted_Connection=True;

Development (SQL Server Express):
  Server=localhost,1433;Database=FileShare;User Id=sa;Password=***;TrustServerCertificate=True;

Docker (local docker-compose):
  Server=db,1433;Database=FileShare;User Id=sa;Password=***;TrustServerCertificate=True;

Production (hosted / Render container):
  Server=db,1433;Database=FileShare;User Id=sa;Password=***;TrustServerCertificate=True;
```

### Storing connection strings (never commit real ones):

```json
{
  "ConnectionStrings": {
    "Default": "Server=(localdb)\\mssqllocaldb;Database=FileShare;Trusted_Connection=True;"
  }
}
```

**Rule:** Never commit production connection strings. Use environment variables.

---

## Index Strategy

### Decision: When to add an index?

```
COMMON QUERY PATTERNS:

  □ WHERE Code = 'xyz'
    → Index on Code (already unique)
  
  □ WHERE ExpiresAt < now
    → Index on ExpiresAt
  
  □ WHERE UploaderId = '...'
    → Index on UploaderId (foreign key)
  
  □ ORDER BY CreatedAt DESC
    → Index on CreatedAt
  
  □ WHERE UploaderId = '...' AND CreatedAt DESC
    → Composite index (UploaderId, CreatedAt)
```

### Creating an index:

```csharp
// In OnModelCreating:
b.Entity<FileRecord>(e =>
{
    // Single column
    e.HasIndex(f => f.ExpiresAt);
    
    // Composite index
    e.HasIndex(f => new { f.UploaderId, f.CreatedAt });
    
    // Covering index (advanced)
    e.HasIndex(f => f.Code).IsUnique();
});
```

---

## Data Integrity

### Constraints to enforce:

```
NOT NULL:
  - Code (must have identifier)
  - OriginalFilename
  - MimeType
  - SizeBytes
  
UNIQUE:
  - Code (shareable link)
  - Email (user accounts)
  
FOREIGN KEY:
  - UploaderId → Users.Id (with ON DELETE SET NULL)
  
CHECK:
  - SizeBytes > 0 (no empty files)
  - DownloadCount >= 0 (no negative counts)
  - MaxDownloads > 0 if set (sensible limits)
```

---

## Computed Properties

### When to use computed properties:

```
IsExpired:
  - Calculated on-the-fly
  - Not stored in database
  - Used in queries: where f.IsExpired

IsAvailable:
  - Depends on multiple conditions
  - Calculated from IsExpired and IsOverLimit
  - Used in service validation
```

### Implementation:

```csharp
public class FileRecord
{
    public DateTime? ExpiresAt { get; set; }
    public int? MaxDownloads { get; set; }
    public int DownloadCount { get; set; }
    
    // Computed properties (not persisted)
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt < DateTime.UtcNow;
    public bool IsOverLimit => MaxDownloads.HasValue && DownloadCount >= MaxDownloads;
    public bool IsAvailable => !IsExpired && !IsOverLimit;
}
```

---

## Environment Database Setup

### Development Setup:

```bash
# Using LocalDB (included with Visual Studio)
dotnet ef database update

# Using Docker SQL Server
docker-compose up -d db
dotnet ef database update
```

### Production Setup:

```bash
# Using SQL Server container on Render / Railway
# 1. Deploy a SQL Server Docker image as a service
# 2. Get the internal connection string
# 3. Set in GitHub Secrets
# 4. Apply migrations during deployment
dotnet ef database update --connection "Server=db,1433;Database=FileShare;User Id=sa;Password=***;TrustServerCertificate=True;"
```

---

## Common Mistakes

❌ **DON'T:**
- Manually edit database and hope code matches (migrations needed)
- Skip indexes on foreign keys (performance suffers)
- Load entities just to delete them (use ExecuteDeleteAsync)
- Store passwords in plain text (use BCrypt)
- Use IDENTITY for distributed systems (use Guid)
- Add columns without migrations (breaks other environments)
- Skip NOT NULL constraints (data quality suffers)

✅ **DO:**
- Create migration for every schema change
- Add indexes on frequently-queried columns
- Use computed properties for logic
- Test migrations on dev database first
- Include timestamps (CreatedAt)
- Use appropriate data types (long for file sizes)
- Document complex queries with comments
- Test migrations on staging before production

---

## Cross-References

- **Using database in services** → See `fileshare-backend/SKILL.md`
- **Setting up architecture** → See `fileshare-architecture/SKILL.md`
- **Deploying database changes** → See `fileshare-devops/SKILL.md`
