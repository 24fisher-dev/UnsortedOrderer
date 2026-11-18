using System.Net.Http.Headers;
using System.Text.Json;

namespace UnsortedOrderer.Infrastructure.Updates;

public sealed class GitHubReleaseChecker(HttpClient client)
{
    private const string Repository = "24fisher-dev/UnsortedOrderer";
    private static readonly Uri LatestRelease = new($"https://api.github.com/repos/{Repository}/releases/latest");

    public async Task<ReleaseUpdate?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("UnsortedOrderer", currentVersion.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tag_name", out var property) || property.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var tag = property.GetString()!;
            if (!tag.StartsWith('v') || tag.Skip(1).Any(c => c != '.' && !char.IsAsciiDigit(c)) ||
                !Version.TryParse(tag[1..], out var remoteVersion) || remoteVersion.Build < 0 ||
                Normalize(remoteVersion) <= Normalize(currentVersion))
            {
                return null;
            }

            return new ReleaseUpdate(remoteVersion, new Uri($"https://github.com/{Repository}/releases/tag/{tag}"));
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            // An unavailable or invalid release does not block the app.
            return null;
        }
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
}

public sealed record ReleaseUpdate(Version Version, Uri ReleasePage);
