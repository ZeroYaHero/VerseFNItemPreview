using System.Security.Cryptography;
using System.Text;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FortItemParser;

internal sealed class PreviewService : IDisposable
{
    private readonly DefaultFileProvider _provider;
    private readonly string _version;
    private readonly string _cache;
    private readonly string _token;
    private readonly SemaphoreSlim _exports = new(1); // Semaphore local to the app and not to the os

    public sealed record PreviewRequest(string IconPath);

    private PreviewService(DefaultFileProvider provider, string version, string cache, string token)
    {
        _provider = provider;
        _version = version;
        _cache = cache;
        _token = token;
    }

    public static async Task RunAsync(DefaultFileProvider provider, string version, string cacheDirectory)
    {
        var endpointFile = Environment.GetEnvironmentVariable("VERSE_PREVIEW_ENDPOINT_FILE");
        var token = Environment.GetEnvironmentVariable("VERSE_PREVIEW_TOKEN"); // A little overkill but it "prevents" unauthorized applications from accessing.
        if (string.IsNullOrEmpty(endpointFile) || string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Preview endpoint file and token are required in serve mode.");
        }

        var cache = Path.Combine(cacheDirectory, "previews");
        Directory.CreateDirectory(cache);
        using var service = new PreviewService(provider, version, cache, token);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapPost("/preview", service.ExportPreviewAsync);
        await app.StartAsync();
        try
        {
            await File.WriteAllTextAsync(endpointFile + ".tmp", app.Urls.Single());
            File.Move(endpointFile + ".tmp", endpointFile, true);
            await app.WaitForShutdownAsync();
        }
        finally
        {
            File.Delete(endpointFile);
            File.Delete(endpointFile + ".tmp");
        }
    }

    private async Task<IResult> ExportPreviewAsync(HttpContext context, PreviewRequest request)
    {
        // Requires token/auth
        if (context.Request.Headers.Authorization != $"Bearer {_token}")
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.IconPath))
        {
            return Results.BadRequest(new { error = "iconPath is required" });
        }

        // The provider and temporary cache files are shared between requests.
        await _exports.WaitAsync(context.RequestAborted);
        try
        {
            var icon = request.IconPath;
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"icon-png-v2|{_version}|{icon.ToLowerInvariant()}")));
            var filePath = Path.Combine(_cache, key + ".png");
            var cached = File.Exists(filePath);
            if (!cached)
            {
                var texture = await _provider.SafeLoadPackageObjectAsync<UTexture2D>(icon);
                if (texture == null)
                {
                    throw new IOException("Icon texture could not be loaded");
                }
                var decoded = texture.Decode(256);
                if (decoded == null)
                {
                    throw new IOException("Icon texture could not be decoded");
                }
                var data = decoded.Encode(ETextureFormat.Png, false, out _);
                var temporaryPath = filePath + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporaryPath, data);
                    File.Move(temporaryPath, filePath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
            }
            return Results.Ok(new { filePath, cached });
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return Results.Json(new { error = error.Message }, statusCode: 500);
        }
        finally
        {
            _exports.Release();
        }
    }

    public void Dispose()
    {
        _exports.Dispose();
    }
}
