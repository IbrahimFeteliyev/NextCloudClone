using Atlas.Api.DTOs;
using Atlas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Atlas.Api.Controllers;
[Authorize, ApiController, Route("api")]
public class WorkspaceController(DocumentService documents) : ControllerBase
{
    [HttpGet("explorer")] public Task<ExplorerDto> Explore([FromQuery] string view = "files", [FromQuery] Guid? folderId = null, [FromQuery] string? search = null) => documents.Explore(view, folderId, search);
    [HttpGet("stats")] public Task<object> Stats() => documents.Stats();
    [HttpGet("audit")] public Task<object> Audit() => documents.Audit();
    [HttpPost("folders")] public async Task<ActionResult<ResourceDto>> Create(CreateFolderRequest request) => Ok(await documents.CreateFolder(request));
    [HttpPost("files/upload"), RequestSizeLimit(105_906_176), RequestFormLimits(MultipartBodyLengthLimit = 105_906_176)]
    public async Task<ActionResult<ResourceDto>> Upload([FromForm] Guid parentFolderId, IFormFile file) => Ok(await documents.Upload(parentFolderId, file));
    [HttpGet("files/{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id) { var d = await documents.Download(id); return File(d.Stream, d.ContentType, d.Name); }
    [HttpPatch("resources/{type}/{id:guid}/name")]
    public async Task<IActionResult> Rename(string type, Guid id, NameRequest request) { await documents.Rename(type, id, request.Name); return NoContent(); }
    [HttpPatch("resources/{type}/{id:guid}/move")]
    public async Task<IActionResult> Move(string type, Guid id, MoveRequest request) { await documents.Move(type, id, request.ParentFolderId); return NoContent(); }
    [HttpDelete("resources/{type}/{id:guid}")]
    public async Task<IActionResult> Delete(string type, Guid id) { await documents.Delete(type, id); return NoContent(); }
    [HttpGet("resources/{type}/{id:guid}/shares")] public Task<List<ShareDto>> Shares(string type, Guid id) => documents.GetShares(type, id);
    [HttpPut("resources/{type}/{id:guid}/shares")]
    public async Task<IActionResult> Share(string type, Guid id, ShareRequest request) { await documents.SetShare(type, id, request); return NoContent(); }
    [HttpDelete("resources/{type}/{id:guid}/shares/{userId:guid}")]
    public async Task<IActionResult> Unshare(string type, Guid id, Guid userId) { await documents.RemoveShare(type, id, userId); return NoContent(); }
}
