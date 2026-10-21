using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PCCompanion.Infrastructure.Updates;
using PCCompanion.Testing;
using Xunit;

namespace PCCompanion.Infrastructure.Tests;

public sealed class GitHubReleaseSourceTests
{
    private readonly FakeGitHub _gitHub = new();
    private readonly ManualTimeProvider _time = new();
    private readonly GitHubReleaseSource _source;

    public GitHubReleaseSourceTests()
    {
        _source = new GitHubReleaseSource(_gitHub.CreateHandler, _time);
    }

    [Fact]
    public async Task TheRequestAsksForTheLatestReleaseWithOnlyTwoHeaders()
    {
        await _source.GetLatestAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(_gitHub.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://api.github.com/repos/Lazyb0y/pc-companion/releases/latest"), request.Address);
        Assert.Equal([("Accept", "application/vnd.github+json"), ("User-Agent", "PCCompanion")], request.Headers.OrderBy(header => header.Name, StringComparer.Ordinal));
        Assert.False(request.HasContent);
    }

    [Fact]
    public async Task EachRequestHasItsOwnHandlerWhichIsDisposedAfterIt()
    {
        await _source.GetLatestAsync(TestContext.Current.CancellationToken);
        await _source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, _gitHub.HandlersCreated);
        Assert.Equal(2, _gitHub.HandlersDisposed);
    }

    [Fact]
    public async Task AReleaseGivesItsVersion()
    {
        var answer = await _source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseAnswerKind.Release, answer.Kind);
        Assert.Equal(new ReleaseVersion(0, 2, 0), answer.Version);
    }

    [Fact]
    public async Task NotFoundMeansNoRelease()
    {
        _gitHub.Respond = (_, _) => Task.FromResult(FakeGitHub.Status(HttpStatusCode.NotFound));

        var answer = await _source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseAnswerKind.NoRelease, answer.Kind);
        Assert.Null(answer.Version);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotModified)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired)]
    public async Task OtherStatusesAreErrors(HttpStatusCode status)
    {
        _gitHub.Respond = (_, _) => Task.FromResult(FakeGitHub.Status(status));

        var answer = await _source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseAnswerKind.ErrorStatus, answer.Kind);
        Assert.Equal((int)status, answer.StatusCode);
        Assert.Null(answer.Version);
    }

    [Theory]
    [InlineData("not json", "it isn't a release in JSON")]
    [InlineData("[]", "it isn't a release in JSON")]
    [InlineData("""{ "tag_name": 2 }""", "it isn't a release in JSON")]
    [InlineData("{}", "it has no tag")]
    [InlineData("""{ "tag_name": null }""", "it has no tag")]
    [InlineData("""{ "tag_name": "v0.2.0-beta.1" }""", "its tag isn't a version such as v1.2.0")]
    [InlineData("""{ "tag_name": "0.2.0" }""", "its tag isn't a version such as v1.2.0")]
    public async Task AnAnswerThatIsntAReleaseCantBeUsed(string json, string problem)
    {
        _gitHub.Respond = (_, _) => Task.FromResult(FakeGitHub.Json(json));

        var answer = await _source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseAnswerKind.Unreadable, answer.Kind);
        Assert.Equal(problem, answer.Problem);
        Assert.Null(answer.Version);
        Assert.Null(answer.Exception);
    }

    [Fact]
    public async Task AnAnswerUpToTheLimitIsRead()
    {
        var json = """{ "tag_name": "v0.2.0" }""".PadRight(GitHubReleaseSource.MaximumAnswerSize);
        _gitHub.Respond = (_, _) => Task.FromResult(FakeGitHub.Json(json));

        var answer = await _source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new ReleaseVersion(0, 2, 0), answer.Version);
    }

    [Fact]
    public async Task AnAnswerOverTheLimitCantBeUsed()
    {
        var json = """{ "tag_name": "v0.2.0" }""".PadRight(GitHubReleaseSource.MaximumAnswerSize + 1);
        _gitHub.Respond = (_, _) => Task.FromResult(FakeGitHub.Json(json));

        var answer = await _source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseAnswerKind.Unreadable, answer.Kind);
        Assert.Equal("it's larger than 1 MB", answer.Problem);
    }

    [Fact]
    public async Task AnAnswerOfUnknownLengthOverTheLimitCantBeUsed()
    {
        var json = Encoding.UTF8.GetBytes("""{ "tag_name": "v0.2.0" }""".PadRight(GitHubReleaseSource.MaximumAnswerSize + 1));
        _gitHub.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(json)) });

        var answer = await _source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseAnswerKind.Unreadable, answer.Kind);
        Assert.Equal("it's larger than 1 MB", answer.Problem);
    }

    [Fact]
    public async Task AnAnswerThatEndsEarlyCantBeUsed()
    {
        var json = Encoding.UTF8.GetBytes("""{ "tag_name": "v0.2.0" }""".PadRight(10_000));
        _gitHub.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(json, failPartWay: true)) });

        var answer = await _source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseAnswerKind.Unreadable, answer.Kind);
        Assert.Equal("it ended early", answer.Problem);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    [InlineData(HttpRequestError.ProxyTunnelError)]
    public async Task AFailedRequestIsNoAnswer(HttpRequestError error)
    {
        _gitHub.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException(error, "Failed."));

        var answer = await _source.GetLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ReleaseAnswerKind.NoAnswer, answer.Kind);
        Assert.Equal(error.ToString(), answer.Problem);
        Assert.IsType<HttpRequestException>(answer.Exception);
    }

    [Fact]
    public async Task NoAnswerWithin30SecondsIsNoAnswer()
    {
        _gitHub.Respond = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return FakeGitHub.Release("v0.2.0");
        };

        var pending = _source.GetLatestAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(pending.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(1));
        var answer = await pending;

        Assert.Equal(ReleaseAnswerKind.NoAnswer, answer.Kind);
        Assert.Equal("Timeout", answer.Problem);
    }

    [Fact]
    public async Task CancelingThrows()
    {
        using var cancel = new CancellationTokenSource();
        _gitHub.Respond = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return FakeGitHub.Release("v0.2.0");
        };

        var pending = _source.GetLatestAsync(cancel.Token);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public void TheReleasePageIsTheLatestReleaseOnGitHub()
    {
        Assert.Equal(new Uri("https://github.com/Lazyb0y/pc-companion/releases/latest"), GitHubReleaseSource.ReleasePageAddress);
    }
}
