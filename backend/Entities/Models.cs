namespace Atlas.Api.Entities;

[Flags]
public enum Access { None = 0, Read = 1, Write = 2, Delete = 4, Share = 8, All = 15 }
public class Role { public int Id { get; set; } public string Name { get; set; } = ""; }
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
public class Folder
{
    public DateTime? DeletedAt { get; set; }
    public Guid? TrashRootId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public Guid? ParentFolderId { get; set; }
    public Folder? ParentFolder { get; set; }
    public bool IsRoot { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public class Document
{
    public DateTime? DeletedAt { get; set; }
    public Guid? TrashRootId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public int Version { get; set; } = 1;
    public long Size { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public Guid OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public Guid ParentFolderId { get; set; }
    public Folder ParentFolder { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
public class FileVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FileId { get; set; }
    public Document File { get; set; } = null!;
    public int Number { get; set; }
    public string ObjectKey { get; set; } = "";
    public long Size { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public string? SaveId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;
}
// Collaborators keep one key and initial content reference across ordinary saves.
public class OfficeSession
{
    public string InitialObjectKey { get; set; } = "";
    public string Key { get; set; } = "";
    public Guid FileId { get; set; }
    public Document File { get; set; } = null!;
    public int InitialVersion { get; set; }
    public int LastSavedVersion { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
public class OfficeParticipant
{
    public string SessionKey { get; set; } = "";
    public OfficeSession Session { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public bool CanEdit { get; set; }
}
public class OfficeSave
{
    public string Id { get; set; } = "";
    public Guid FileId { get; set; }
    public Document File { get; set; } = null!;
}
public class FolderPermission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FolderId { get; set; }
    public Folder Folder { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Access Access { get; set; }
}
public class FilePermission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FileId { get; set; }
    public Document File { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Access Access { get; set; }
}
public class Favorite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? FolderId { get; set; }
    public Folder? Folder { get; set; }
    public Guid? FileId { get; set; }
    public Document? File { get; set; }
}
public class AuditLog
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Action { get; set; } = "";
    public Guid? ResourceId { get; set; }
    public string ResourceType { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
