using Atlas.Api.Auth;
using Atlas.Api.Services;
using Atlas.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Api.Controllers;

[ApiController, Route("api/files")]
public class FileContentController(FileContentService content, CurrentUser current, ObjectStorage storage, ILogger<FileContentController> logger) : ControllerBase
{
    [Authorize, HttpPost("{id:guid}/preview")]
    public Task<PreviewTicket> Preview(Guid id) => content.CreateTicket(id);

    [AllowAnonymous, HttpGet("{id:guid}/content"), HttpHead("{id:guid}/content")]
    public async Task Content(Guid id, [FromQuery(Name = "preview_token")] string? ticket = null)
    {
        // AllowAnonymous permits scoped media tickets; requests without a valid identity still get 401.
        var actor = User.Identity?.IsAuthenticated == true ? current.Id : Guid.Empty;
        int? ticketVersion = null;
        if (!string.IsNullOrEmpty(ticket)) { var identity = content.ValidateTicket(ticket, id); actor = identity.Actor; ticketVersion = identity.Version; }
        if (actor == Guid.Empty) throw new ApiException(401, "Please sign in to preview this file.");
        var file = await content.RequireFile(id, actor);
        if (ticketVersion.HasValue && ticketVersion != file.Version) throw new ApiException(409, "This file has changed. Close the preview and open the latest version.");
        var etag = $"\"{file.Id:N}-v{file.Version}\"";
        var rangeHeader = Request.Headers.Range.ToString();
        // If-Range must match this immutable version. Otherwise return the full current representation.
        if (Request.Headers.IfRange.Count > 0 && Request.Headers.IfRange.ToString() != etag) rangeHeader = "";
        ContentRange range;
        try { range = ContentRange.Parse(rangeHeader, file.Size); }
        catch (ApiException ex) when (ex.Status == 416) { Response.Headers.ContentRange = $"bytes */{file.Size}"; throw; }
        Response.StatusCode = range.Partial ? 206 : 200;
        Response.ContentType = FileContentTypes.Resolve(file);
        Response.ContentLength = range.Length;
        Response.Headers.AcceptRanges = "bytes";
        Response.Headers.ContentDisposition = "inline";
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ETag = etag;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        if (range.Partial) Response.Headers.ContentRange = $"bytes {range.Offset}-{range.Offset + range.Length - 1}/{file.Size}";
        if (HttpMethods.IsHead(Request.Method) || range.Length == 0) return;
        try { await storage.CopyTo(file.ObjectKey, Response.Body, range.Offset, range.Length, HttpContext.RequestAborted); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to stream file {FileId}", id);
            if (Response.HasStarted) { HttpContext.Abort(); return; }
            Response.ContentLength = null; Response.ContentType = null;
            throw new ApiException(502, "File content could not be loaded from storage. Try again.");
        }
    }
}
