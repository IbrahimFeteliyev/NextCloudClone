using System.ComponentModel.DataAnnotations;
using Atlas.Api.Auth;
using Atlas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

public record TextSaveRequest([Required(AllowEmptyStrings = true)] string Content, [Range(1, int.MaxValue)] int ExpectedVersion);

[ApiController, Authorize, Route("api/files")]
public class TextContentController(TextContentService text, FileContentService previews, CurrentUser user) : ControllerBase
{
    // JSON escaping can expand a 512 KB text body up to six times.
    [HttpPut("{id:guid}/content"), RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<ActionResult<PreviewTicket>> Save(Guid id, TextSaveRequest request)
    {
        await text.Save(user.Id, id, request.Content, request.ExpectedVersion, HttpContext.RequestAborted);
        return Ok(await previews.CreateTicket(id));
    }
}
