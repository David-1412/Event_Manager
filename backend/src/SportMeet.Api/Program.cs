using Serilog;
using System.Text.Json;
using System.Text.Json.Serialization;
using SportMeet.Api.Common;
using SportMeet.Api.Middleware;
using SportMeet.Api.Serialization;
using SportMeet.Application;
using SportMeet.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfig) => loggerConfig
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddControllers(options =>
    {
        // Runs FluentValidation before any action body, so services never see an
        // out-of-bounds DTO and the 422 shape is defined once.
        options.Filters.Add<ValidationFilter>();
    })
    .AddJsonOptions(json =>
    {
        json.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        json.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        JsonOptionsConfiguration.ConfigureEnumConverters(json.JsonSerializerOptions);
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Errors carry the `code` extension frontend/src/lib/api.ts reads; see
// ProblemDetailsDefaults. Media types as measured on this build: an unhandled or
// domain exception (ExceptionMiddleware) and a ValidationFilter 422 both leave as
// application/problem+json; the 404 an action returns deliberately comes from
// MVC's client-error fast path (ClientErrorResultFilter, which runs ahead of any
// custom IResultFilter) as application/json with the same RFC 9457 body. api.ts
// parses the body rather than the media type, so the 404's label is left alone
// instead of reaching into MVC's writer; revisit if a client ever negotiates on
// Content-Type.
builder.Services.AddProblemDetails();

// Browsers are separate origins from the API, and api.ts sends a plain fetch with
// no credentials, so the policy is wide open on methods/headers but pinned to the
// configured origins rather than "*" - an unauthenticated wildcard CORS on a
// service that will gain cookies is the kind of default that outlives its reason.
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Per-user ceiling on paste-to-event imports; see ImportRateLimit.
builder.Services.AddRateLimiter(limiter => ImportRateLimit.Configure(
    limiter,
    builder.Configuration.GetSection(SportMeet.Application.Imports.ImportOptions.SectionName)
        .Get<SportMeet.Application.Imports.ImportOptions>() ?? new()));

// Firebase bearer authentication, installed only when Firebase:ProjectId is set (see
// AddFirebaseJwtBearer). Unconfigured it is a no-op and every endpoint stays anonymous
// behind the configured demo identity, exactly as before.
builder.Services.AddFirebaseJwtBearer(builder.Configuration);


// The polling worker only exists when Ingestion:Enabled. Inert by default for the
// same reason the extractor is heuristic and the mailbox list is empty: this feature
// reads someone's mailbox, so it must never start because a deploy happened.
builder.Services.AddEmailIngestionWorker(builder.Configuration);

var app = builder.Build();

// Host-uploaded event thumbnails are written to App_Data/uploads (a compose
// volume) and referenced by the URL /uploads/<key>. Serving them here keeps the
// stored URL exactly what the browser requests; the GUID-named key cannot
// traverse, and the directory holds only files this API itself wrote. Placed
// before the controllers so a static hit never reaches routing.
var uploadRoot = Path.Combine(app.Environment.ContentRootPath, "App_Data", "uploads");
try
{
    Directory.CreateDirectory(uploadRoot);
}
catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
{
    // A read-only or pre-mounted volume: serve what is there, and let the upload
    // endpoint surface the write failure per-request rather than crash startup.
    Log.Warning(ex, "Uploads directory {Path} is not writable; thumbnail uploads will fail", uploadRoot);
}
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadRoot),
    RequestPath = "/uploads",
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public,max-age=86400",
});

app.UseSerilogRequestLogging();
app.UseCors();

// First in the pipeline so exceptions from model binding, auth or endpoints all
// arrive here and leave as RFC 9457 rather than a framework HTML error.
app.UseMiddleware<ExceptionMiddleware>();

// Authentication before authorization before the controllers. Harmless when the
// handler was never installed (no scheme → every request is anonymous), required when
// Firebase:ProjectId configures it.
app.UseAuthentication();
app.UseAuthorization();
// After authentication: the import limiter partitions by the verified user.
app.UseRateLimiter();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.MapControllers();

await SportMeet.Api.StartupTasks.RunAsync(app);

app.Run();

/// <summary>Marker so WebApplicationFactory&lt;Program&gt; can find this entry point.</summary>
public partial class Program;
