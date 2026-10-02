using Atlas.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Data;
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<Document> Files => Set<Document>();
    public DbSet<FileVersion> FileVersions => Set<FileVersion>();
    public DbSet<OfficeSession> OfficeSessions => Set<OfficeSession>();
    public DbSet<OfficeParticipant> OfficeParticipants => Set<OfficeParticipant>();
    public DbSet<FolderPermission> FolderPermissions => Set<FolderPermission>();
    public DbSet<FilePermission> FilePermissions => Set<FilePermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
        b.Entity<User>().Property(x => x.Email).HasMaxLength(254);
        b.Entity<User>().HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Role>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Folder>().Property(x => x.Name).HasMaxLength(255);
        b.Entity<Folder>().HasOne(x => x.ParentFolder).WithMany().HasForeignKey(x => x.ParentFolderId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Folder>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Folder>().HasIndex(x => new { x.ParentFolderId, x.Name }).IsUnique();
        b.Entity<Folder>().HasIndex(x => x.OwnerId).IsUnique().HasFilter("\"IsRoot\" = true");
        b.Entity<Document>().ToTable("Files");
        b.Entity<Document>().Property(x => x.Name).HasMaxLength(255);
        b.Entity<Document>().HasOne(x => x.ParentFolder).WithMany().HasForeignKey(x => x.ParentFolderId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Document>().HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Document>().HasIndex(x => new { x.ParentFolderId, x.Name }).IsUnique();
        b.Entity<Document>().HasIndex(x => x.ObjectKey).IsUnique();
        b.Entity<Document>().Property(x => x.Version).IsConcurrencyToken();
        b.Entity<FileVersion>().HasIndex(x => new { x.FileId, x.Number }).IsUnique();
        b.Entity<FileVersion>().HasIndex(x => new { x.FileId, x.SaveId }).IsUnique();
        b.Entity<FileVersion>().HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<FileVersion>().HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<OfficeSession>().HasKey(x => x.Key);
        b.Entity<OfficeSession>().Property(x => x.Key).HasMaxLength(128);
        b.Entity<OfficeSession>().HasIndex(x => new { x.FileId, x.ClosedAt });
        b.Entity<OfficeSession>().HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OfficeParticipant>().HasKey(x => new { x.SessionKey, x.UserId });
        b.Entity<OfficeParticipant>().HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionKey).OnDelete(DeleteBehavior.Cascade);
        b.Entity<OfficeParticipant>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<FolderPermission>().HasIndex(x => new { x.FolderId, x.UserId }).IsUnique();
        b.Entity<FolderPermission>().HasOne(x => x.Folder).WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<FilePermission>().HasIndex(x => new { x.FileId, x.UserId }).IsUnique();
        b.Entity<FilePermission>().HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AuditLog>().HasIndex(x => x.Timestamp);
        b.Entity<AuditLog>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
