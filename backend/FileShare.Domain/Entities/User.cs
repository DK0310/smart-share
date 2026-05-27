namespace FileShare.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";           // unique
    public string PasswordHash { get; set; } = "";    // BCrypt — NEVER plain text
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<FileRecord> Files { get; set; } = [];
}
