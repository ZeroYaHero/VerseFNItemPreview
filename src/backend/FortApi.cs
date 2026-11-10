using System.Security.Cryptography;
using Org.BouncyCastle.Asn1.Ocsp;
using RestSharp;

namespace FortItemParser;

public sealed class FortApi : IDisposable
{
    private readonly RestClient _client = new("https://api.fortniteporting.app/v2/fortnite/versions/"); // For requesting the version info (provides mapping file url)
    private readonly HttpClient _downloads = new(); // For downloading the mappings file (FortnitePorting stores the file content elsewhere. Not restsharp for this reason)

    public sealed record FortKey(string Key, string Guid);
    public sealed record FortKeys(FortKey MainKey, List<FortKey> ExtraKeys);
    public sealed record FortMappings(string Url, string Md5Hash);
    public sealed record FortVersion(string Version, FortKeys Keys, FortMappings? Mappings);

    public async Task<FortVersion> FetchVersionAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.ExecuteGetAsync<FortVersion>(new RestRequest("latest"), cancellationToken);
        if (response.IsSuccessful && response.Data?.Keys?.MainKey?.Key != null && response.Data.Keys.MainKey.Key.Length > 0)
        {
            return response.Data;
        }
        throw new InvalidOperationException($"FortnitePorting version request failed: {response.StatusCode} {response.ErrorMessage}");
    }

    public async Task<string> GetMappingsAsync(FortVersion version, string cacheDirectory, CancellationToken cancellationToken = default)
    {
        var mapping = version.Mappings;
        if (mapping == null || string.IsNullOrEmpty(mapping.Url) || string.IsNullOrEmpty(mapping.Md5Hash))
        {
            throw new InvalidOperationException("The version API did not return a mappings file and MD5 hash.");
        }

        Directory.CreateDirectory(cacheDirectory); // Creates directory if it doesn't exist

        var hash = mapping.Md5Hash.ToLowerInvariant();
        if (hash.Length != 32 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("The version API returned an invalid mappings MD5 hash.");
        }
        
        var filePath = Path.Combine(cacheDirectory, $"{hash}.usmap");
        if (File.Exists(filePath) && await MatchesHashAsync(filePath, hash, cancellationToken))
        {
            return filePath; // mappings file already exists in cache
        }

        // Verifies the mapping url is valid for download
        if (!Uri.TryCreate(mapping.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("The version API returned an invalid mappings URL.");
        }

        using var response = await _downloads.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!MatchesHash(content, hash))
        {
            throw new InvalidDataException("Downloaded mappings did not match the API MD5 hash.");
        }

        await File.WriteAllBytesAsync(filePath, content, cancellationToken);
        return filePath;
    }

    private static bool MatchesHash(byte[] content, string expected)
    {
        var actual = Convert.ToHexString(MD5.HashData(content));
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    // Uses md5 to verify mapping file content.
    private static async Task<bool> MatchesHashAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await MD5.HashDataAsync(stream, cancellationToken));
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _client.Dispose();
        _downloads.Dispose();
    }
}
