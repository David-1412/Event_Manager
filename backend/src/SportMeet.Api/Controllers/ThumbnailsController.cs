using Microsoft.AspNetCore.Mvc;

namespace SportMeet.Api.Controllers;

/// <summary>Body of `POST /api/events/thumbnail` - the URL the client stores on
/// the event and renders. camelCase policy leaves `url` unchanged.</summary>
public sealed record ThumbnailUploadDto(string Url);

/// <summary>
/// Host thumbnail upload. The create form posts the picked image here as
/// multipart/form-data, gets back a URL, and forwards that URL on the create
/// payload; the image bytes never travel through POST /api/events.
///
/// Storage is the local filesystem under App_Data/uploads (a compose volume, so
/// uploads outlive a rebuild), served back from /uploads/... by Get. That is
/// deliberately the simplest thing that works in this local stack - the plan's
/// eventual home is Azure Blob with a SAS URL (IMPLEMENTATION_PLAN.md Week 3) -
/// and the DTO contract (a URL string) is unchanged when that swap happens, so
/// no caller moves.
///
/// Anonymous-tolerant like POST /api/events: the create page runs under the
/// configured demo identity, so a RequireAuthenticatedUser gate here would 401
/// the very flow it serves. The bytes are validated instead (size + magic
/// number) and the stored name is a fresh GUID, so a caller never controls the
/// path or the extension on disk.
/// </summary>
[ApiController]
[Route("api/events")]
public class ThumbnailsController(IWebHostEnvironment env) : ControllerBase
{
    /// <summary>5 MB, matching the avatar ceiling the spec settles on
    /// (UIUX_DESIGN_SPEC.md: "5MB/format rejected before upload").</summary>
    private const long MaxBytes = 5L * 1024 * 1024;

    /// <summary>Upload one image and get back its URL. 400 (Problem Details) for a
    /// missing/oversized/unsupported file.</summary>
    [HttpPost("thumbnail")]
    [RequestSizeLimit(MaxBytes)]
    [ProducesResponseType(typeof(ThumbnailUploadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ThumbnailUploadDto>> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = "Choose an image to upload" });
        }

        if (file.Length > MaxBytes)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = "That image is larger than 5 MB" });
        }

        // Trust the bytes, not the declared content type: sniff the magic number
        // and store under the extension that matches what is actually there.
        var header = new byte[12];
        await using (var probe = file.OpenReadStream())
        {
            var read = await probe.ReadAsync(header.AsMemory(0, 12), ct);
            var kind = SniffImage(header.AsSpan(0, read));
            if (kind is null)
            {
                return BadRequest(new ProblemDetails { Status = 400, Title = "Use a JPG, PNG or WebP image" });
            }

            // Server-generated GUID name, never derived from the upload's original
            // filename, so a caller cannot smuggle a path or a second extension in.
            var key = $"{Guid.NewGuid():N}{kind.Value.Extension}";
            var directory = Path.Combine(env.ContentRootPath, "App_Data", "uploads");
            Directory.CreateDirectory(directory);

            probe.Position = 0;
            await using (var stream = System.IO.File.Create(Path.Combine(directory, key)))
            {
                await file.CopyToAsync(stream, ct);
            }

            return Ok(new ThumbnailUploadDto($"/uploads/{key}"));
        }
    }


    /// <summary>Serve an uploaded image. The key is validated to a bare GUID-named
    /// file so this can never traverse outside the uploads directory; the stored
    /// URL (/uploads/...) resolves through here. 404 for anything else.</summary>
    [HttpGet("thumbnail/{key}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Get(string key)
    {
        if (!IsSafeKey(key))
        {
            return NotFound();
        }

        var path = Path.Combine(env.ContentRootPath, "App_Data", "uploads", key);
        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(path, ContentTypeForExtension(Path.GetExtension(key)));
    }

    private readonly record struct ImageKind(string Extension, string ContentType);

    /// <summary>JPEG (FFD8FF), PNG (89504E47) or WebP (RIFF....WEBP) by leading
    /// bytes; null for anything else, which the caller rejects.</summary>
    private static ImageKind? SniffImage(ReadOnlySpan<byte> header) => header switch
    {
        { Length: >= 3 } when header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF
            => new ImageKind(".jpg", "image/jpeg"),
        { Length: >= 8 } when header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            => new ImageKind(".png", "image/png"),
        { Length: >= 12 } when header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
                              && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50
            => new ImageKind(".webp", "image/webp"),
        _ => null,
    };

    /// <summary>Only the exact 32-hex-chars.jpg|.png|.webp the upload writes - no
    /// separators, no traversal.</summary>
    private static bool IsSafeKey(string key)
    {
        var dot = key.LastIndexOf('.');
        if (dot != 32)
        {
            return false;
        }

        var stem = key[..dot];
        var extension = key[dot..];
        return stem.All(Uri.IsHexDigit) && extension is ".jpg" or ".png" or ".webp";
    }

    private static string ContentTypeForExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg",
    };
}
