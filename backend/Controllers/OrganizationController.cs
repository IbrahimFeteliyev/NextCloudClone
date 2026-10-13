using Atlas.Api.Services;
using Atlas.Api.DTOs;
using Atlas.Api.Entities;
using Atlas.Api.Auth;
using Atlas.Api.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Atlas.Api.Controllers;
[Authorize, ApiController, Route("api")]
public class OrganizationController(OrganizationService organization, PermissionService permissions, CurrentUser user) : ControllerBase
{
    public record DepartmentRequest(Guid? DepartmentId);
    public record ActiveRequest(bool IsActive);
    public record MemberRequest(bool Member);
    public record UserRequest(string Name, string Email, string Password, Guid? DepartmentId);
    public record TeamRequest(string Name, Guid GroupId, Access Permissions);
    [HttpGet("organization")] public Task<object> Catalog() => organization.Catalog();
    [HttpGet("organization/admin")] public Task<object> Admin() => organization.Administration();
    [HttpPost("organization/departments")] public async Task<IActionResult> Department(NameRequest request) { await organization.CreateDepartment(request.Name); return NoContent(); }
    [HttpPost("organization/groups")] public async Task<IActionResult> Group(NameRequest request) { await organization.CreateGroup(request.Name); return NoContent(); }
    [HttpPost("organization/users")] public async Task<IActionResult> CreateUser(UserRequest request) { await organization.CreateUser(request.Name, request.Email, request.Password, request.DepartmentId); return NoContent(); }
    [HttpPut("organization/users/{id:guid}/department")] public async Task<IActionResult> Assign(Guid id, DepartmentRequest request) { await organization.SetDepartment(id, request.DepartmentId); return NoContent(); }
    [HttpPut("organization/users/{id:guid}/active")] public async Task<IActionResult> Active(Guid id, ActiveRequest request) { await organization.SetActive(id, request.IsActive); return NoContent(); }
    [HttpPut("organization/groups/{group:guid}/members/{id:guid}")] public async Task<IActionResult> Member(Guid group, Guid id, MemberRequest request) { await organization.SetMember(group, id, request.Member); return NoContent(); }
    [HttpPost("team-folders")] public async Task<IActionResult> Team(TeamRequest request) { await organization.CreateTeam(request.Name, request.GroupId, request.Permissions); return NoContent(); }
    [HttpGet("resources/{type}/{id:guid}/group-shares")] public async Task<List<GroupShareDto>> Shares(string type, Guid id) { await permissions.Require(user.Id, type, id, Access.Read); return await permissions.GroupShares(type, id); }
    [HttpPut("resources/{type}/{id:guid}/group-shares")] public async Task<IActionResult> Share(string type, Guid id, GroupShareRequest request) { await organization.SetGroupShare(type, id, request.GroupId, request.Permissions); return NoContent(); }
    [HttpDelete("resources/{type}/{id:guid}/group-shares/{group:guid}")] public async Task<IActionResult> Remove(string type, Guid id, Guid group) { await organization.SetGroupShare(type, id, group, Access.None, true); return NoContent(); }
}
