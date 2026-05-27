using FileShare.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FileShare.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<FileRecord> Files => Set<FileRecord>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // FileRecord configuration
        modelBuilder.Entity<FileRecord>(entity =>
        {
            entity.HasKey(f => f.Id);

            entity.HasIndex(f => f.Code)
                  .IsUnique();

            entity.Property(f => f.Code)
                  .HasMaxLength(10)
                  .IsRequired();

            entity.Property(f => f.OriginalFilename)
                  .HasMaxLength(255)
                  .IsRequired();

            entity.Property(f => f.MimeType)
                  .HasMaxLength(100)
                  .IsRequired();

            entity.Property(f => f.StoragePath)
                  .HasMaxLength(500)
                  .IsRequired();

            entity.Property(f => f.PasswordHash)
                  .HasMaxLength(255);

            entity.Property(f => f.ThumbnailPath)
                  .HasMaxLength(500);

            // Computed properties — not mapped to DB columns
            entity.Ignore(f => f.IsExpired);
            entity.Ignore(f => f.IsOverLimit);
            entity.Ignore(f => f.IsAvailable);
            entity.Ignore(f => f.IsImage);

            // FK → Users.Id, SET NULL on delete
            entity.HasOne(f => f.Uploader)
                  .WithMany(u => u.Files)
                  .HasForeignKey(f => f.UploaderId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);

            entity.HasIndex(u => u.Email)
                  .IsUnique();

            entity.Property(u => u.Email)
                  .HasMaxLength(255)
                  .IsRequired();

            entity.Property(u => u.PasswordHash)
                  .HasMaxLength(255)
                  .IsRequired();
        });
    }
}
