using System.Text.Json;
using Atlas.Api.Auth;
using Atlas.Api.DTOs;
using Atlas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;
[Authorize, ApiController, Route("api/onlyoffice/files/{id:guid}")]
public class OnlyOfficeController(OnlyOfficeService office, ILogger<OnlyOfficeController> logger) : ControllerBase
{
    [HttpGet("config")]
    public Task<OfficeConfigDto> Config(Guid id, [FromQuery] string mode = "auto")
    { Response.Headers.CacheControl = "no-store"; return office.Config(id, mode); }
    [AllowAnonymous, HttpGet("content")]
    public async Task<IActionResult> Content(Guid id, [FromQuery(Name = "access_token")] string? accessToken)
    {
        Response.Headers.CacheControl = "no-store";
        var content = await office.Content(id, accessToken); return File(content.Stream, content.ContentType, content.Name);
    }
    [AllowAnonymous, HttpPost("callback"), RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Callback(Guid id, [FromQuery(Name = "access_token")] string? accessToken, [FromBody] JsonElement body)
    {
        try { await office.Callback(id, accessToken, body, Request.Headers.Authorization); return Ok(new { error = 0 }); }
        catch (ApiException e) { logger.LogWarning("ONLYOFFICE callback rejected for {FileId}: {Reason}", id, e.Message); return StatusCode(e.Status, new { error = 1, message = e.Message }); }
        catch (Exception e) { logger.LogError(e, "ONLYOFFICE save failed for {FileId}", id); return StatusCode(500, new { error = 1, message = "Document save failed. The previous version is preserved; retry saving." }); }
    }
}
[Authorize, ApiController, Route("api/files/{id:guid}/versions")]
public class FileVersionsController(FileVersionService versions, CurrentUser user) : ControllerBase
{
    [HttpGet] public Task<object> List(Guid id) => versions.List(user.Id, id);
    [HttpGet("{number:int}/download")]
    public async Task<IActionResult> Download(Guid id, int number)
    { var content = await versions.Download(user.Id, id, number); return File(content.Stream, content.ContentType, content.Name); }
}
