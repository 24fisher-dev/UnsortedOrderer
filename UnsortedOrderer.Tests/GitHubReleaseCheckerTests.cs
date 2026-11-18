using System.Net;
using System.Text;
using UnsortedOrderer.Infrastructure.Updates;
using Xunit;

namespace UnsortedOrderer.Tests;

public sealed class GitHubReleaseCheckerTests
{
    [Theory]
    [InlineData("v0.4.9.0", false)]
    [InlineData("v0.4.10", false)]
    [InlineData("v0.4.10.0", false)]
    [InlineData("v0.4.10.1", true)]
    [InlineData("v0.4.11.0", true)]
    [InlineData("v0.5.1", true)]
    [InlineData("v0.4.99.0-rc", false)]
    [InlineData("v0.4.999.0/other", false)]
    [InlineData("v0.4", false)]
    [InlineData("024", false)]
    public async Task Only_a_newer_numeric_release_version_is_offered(string tag, bool expected)
    {
        var localVersion = new Version(0, 4, 10, 0);
        var body = $$"""{"tag_name":"{{tag}}","html_url":"https://example.com/untrusted.exe"}""";
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.github.com/repos/24fisher-dev/UnsortedOrderer/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Equal("UnsortedOrderer/0.4.10.0", request.Headers.UserAgent.ToString());
            return Task.FromResult(Response(body));
        }));

        var update = await new GitHubReleaseChecker(http).CheckAsync(localVersion);

        if (expected)
        {
            Assert.NotNull(update);
            Assert.Equal(new Version(tag[1..]), update.Version);
            Assert.Equal($"https://github.com/24fisher-dev/UnsortedOrderer/releases/tag/{tag}", update.ReleasePage.AbsoluteUri);
        }
        else
        {
            Assert.Null(update);
        }
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"tag_name":14}""")]
    public async Task Bad_release_responses_do_not_prompt(string body)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Response(body))));
        Assert.Null(await new GitHubReleaseChecker(http).CheckAsync(new Version(0, 4, 10, 0)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Unavailable_release_does_not_prompt(HttpStatusCode status)
    {
        using var http = new HttpClient(new Handler((_, _) =>
            Task.FromResult(new HttpResponseMessage(status))));
        Assert.Null(await new GitHubReleaseChecker(http).CheckAsync(new Version(0, 4, 10, 0)));
    }

    [Fact]
    public async Task Offline_app_can_continue()
    {
        using var http = new HttpClient(new Handler((_, _) => throw new HttpRequestException("offline")));
        Assert.Null(await new GitHubReleaseChecker(http).CheckAsync(new Version(0, 4, 10, 0)));
    }

    [Fact]
    public async Task Closing_the_window_cancels_a_pending_lookup()
    {
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response("""{"tag_name":"v0.4.11.0"}""");
        }));
        var check = new GitHubReleaseChecker(http).CheckAsync(new Version(0, 4, 10, 0), cancellation.Token);
        cancellation.Cancel();
        Assert.Null(await check.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
