using System.Security.Claims;
namespace Atlas.Api.Auth;
public class CurrentUser(IHttpContextAccessor context)
{
    public Guid Id => Guid.TryParse(context.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new Services.ApiException(401, "Please sign in to continue.");
    public bool IsAdmin => context.HttpContext?.User.IsInRole("Admin") == true;
}
