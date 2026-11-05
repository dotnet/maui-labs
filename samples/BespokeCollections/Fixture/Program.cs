using BespokeCollections.Core;
using BespokeCollections.Fixture;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.TypeInfoResolverChain.Insert(0, PhotoJsonContext.Default));
var app = builder.Build();
var requests = 0;
var thumbnails = 0;
var failuresRemaining = 0;
var latency = 120;

app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapGet("/photos", async (HttpContext context, string? query, string? cursor, int? limit) =>
{
    Interlocked.Increment(ref requests);
    await Task.Delay(Volatile.Read(ref latency), context.RequestAborted);
    if (Interlocked.Exchange(ref failuresRemaining, 0) > 0)
        return Results.Problem("Scripted metadata failure.", statusCode: 503);
    try
    {
        return Results.Json(FixtureCatalog.Page(query ?? "0", cursor, limit ?? 50), PhotoJsonContext.Default.PhotoPage);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});
app.MapGet("/thumbnails/{id:long}", async (HttpContext context, long id, int? width) =>
{
    Interlocked.Increment(ref thumbnails);
    await Task.Delay(80 + (int)(id % 4) * 60, context.RequestAborted);
    if (id is <= 0 or > FixtureCatalog.Count * 2 || width is < 32 or > 1024)
        return Results.BadRequest();
    var item = FixtureCatalog.Item((int)((id - 1) % FixtureCatalog.Count), id > FixtureCatalog.Count ? "1" : "0");
    var targetWidth = width ?? 160;
    var targetHeight = Math.Max(16, (int)Math.Round(targetWidth * (double)item.PixelHeight / item.PixelWidth));
    var bytes = FixturePng.Create(id, targetWidth, targetHeight);
    context.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.File(bytes, "image/png");
});
app.MapPost("/control/fail-next", () => { Interlocked.Exchange(ref failuresRemaining, 1); return Results.Ok(); });
app.MapPost("/control/latency/{milliseconds:int}", (int milliseconds) =>
{
    if (milliseconds is < 0 or > 5000)
        return Results.BadRequest();
    Interlocked.Exchange(ref latency, milliseconds);
    return Results.Ok();
});
app.MapGet("/stats", () => Results.Ok(new { metadataRequests = requests, thumbnailRequests = thumbnails, latency }));
app.Run();
