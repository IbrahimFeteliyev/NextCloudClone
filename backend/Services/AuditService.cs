using Atlas.Api.Data;
using Atlas.Api.Entities;
namespace Atlas.Api.Services;
public class AuditService(AppDbContext db)
{
    public void Add(Guid user, string action, Guid? resource, string type, string details) =>
        db.AuditLogs.Add(new AuditLog { UserId = user, Action = action, ResourceId = resource, ResourceType = type, Details = details });
}
