using System.Text;
using Atlas.Api.Entities;
using Atlas.Api.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Atlas.Api.Data;
public class SeedData(AppDbContext db, PasswordHasher<User> hasher, ObjectStorage storage, IConfiguration config)
{
    public async Task Initialize()
    {
        await storage.Initialize(); if (await db.Users.AnyAsync()) return;
        db.Roles.AddRange(new Role { Id = 1, Name = "Admin" }, new Role { Id = 2, Name = "Manager" }, new Role { Id = 3, Name = "Employee" });
        var specs = new[] { ("admin", "Alex Morgan", 1), ("finance", "Sarah Wilson", 3), ("manager", "James Chen", 2), ("employee", "Emma Davis", 3) };
        foreach (var (email, name, role) in specs)
        {
            var user = new User { Email = $"{email}@demo.local", Name = name, RoleId = role };
            user.PasswordHash = hasher.HashPassword(user, config["Demo:Password"] ?? "Demo123!"); db.Users.Add(user);
            var root = new Folder { Name = "My Files", OwnerId = user.Id, IsRoot = true }; db.Folders.Add(root);
            foreach (var folderName in new[] { "Documents", "Projects", "Reports", "Brand assets" }) db.Folders.Add(new Folder { Name = folderName, OwnerId = user.Id, ParentFolderId = root.Id });
            var docs = new[] {
                ("Q4 planning notes.md", "text/markdown", "# Q4 planning\n\n## Priorities\n- Review department budgets\n- Align project milestones\n- Prepare the annual report\n"),
                ("Expense summary.csv", "text/csv", "Department,Category,Amount\nFinance,Operations,42000\nEngineering,Equipment,18500\nMarketing,Campaigns,12500\n"),
                ("Welcome to Atlas.txt", "text/plain", "Welcome to Atlas.\n\nYour team's documents, thoughtfully organized.\nCreate folders, upload files, and share exactly the access your teammates need.\n") };
            foreach (var (fileName, contentType, content) in docs)
            {
                var bytes = Encoding.UTF8.GetBytes(content); var doc = new Document { Name = fileName, OriginalName = fileName, ContentType = contentType, Size = bytes.Length, OwnerId = user.Id, ParentFolderId = root.Id, ObjectKey = $"seed/{Guid.NewGuid():N}" };
                using var stream = new MemoryStream(bytes); await storage.Put(doc.ObjectKey, stream, bytes.Length, contentType); db.Files.Add(doc);
                db.FileVersions.Add(Services.FileVersionService.Initial(doc, user.Id));
            }
        }
        await db.SaveChangesAsync();
    }
}
