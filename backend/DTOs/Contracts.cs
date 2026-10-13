using System.ComponentModel.DataAnnotations;
using Atlas.Api.Entities;
namespace Atlas.Api.DTOs;
public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public record UserDto(Guid Id, string Email, string Name, string Role, Guid? DepartmentId = null, string? Department = null, bool IsActive = true);
public record NameRequest([Required, StringLength(255, MinimumLength = 1)] string Name);
public record CreateFolderRequest([Required, StringLength(255, MinimumLength = 1)] string Name, Guid ParentFolderId);
public record MoveRequest(Guid ParentFolderId);
public record ShareRequest(Guid UserId, Access Permissions);
public record FavoriteRequest(bool IsFavorite);
public record ShareDto(Guid UserId, string Name, string Email, Access Permissions, bool Inherited, string Source);
public record ResourceDto(Guid Id, string Name, string Type, Guid OwnerId, string Owner, string OwnerEmail,
    DateTime Modified, long Size, Guid? ParentFolderId, Access Permissions, IReadOnlyList<ShareDto> SharedWith, string? ContentType = null, bool IsFavorite = false, Guid? TeamSpaceId = null, bool IsTeamRoot = false);
public record GroupShareRequest(Guid GroupId, Access Permissions);
public record GroupShareDto(Guid GroupId, string Name, Access Permissions, bool Inherited, string Source);
public record BreadcrumbDto(Guid Id, string Name);
public record ExplorerDto(Guid? FolderId, Access Permissions, IReadOnlyList<BreadcrumbDto> Breadcrumbs, IReadOnlyList<ResourceDto> Items, int Total = 0, int Page = 1, int PageSize = 20);
