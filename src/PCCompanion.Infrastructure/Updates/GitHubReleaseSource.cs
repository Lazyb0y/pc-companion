using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PCCompanion.Infrastructure.Updates;

public enum ReleaseAnswerKind
{
    /// <summary>GitHub named the latest release's version.</summary>
    Release,

    /// <summary>GitHub has no release yet (404).</summary>
    NoRelease,

    /// <summary>GitHub answered with another status, such as a rate limit.</summary>
    ErrorStatus,

    /// <summary>GitHub answered, but the answer can't be used.</summary>
    Unreadable,

    /// <summary>The request got no answer: no network, a failed name lookup, connection or TLS, or a timeout.</summary>
    NoAnswer,
}

/// <summary>
/// What GitHub said about the latest release. Nothing from the answer is kept but the version.
/// </summary>
public sealed record ReleaseAnswer(ReleaseAnswerKind Kind)
{
    public ReleaseVersion? Version { get; init; }

    public int? StatusCode { get; init; }

    /// <summary>Why the answer can't be used, or why there was none, for the log.</summary>
    public string? Problem { get; init; }

    public Exception? Exception { get; init; }
}

/// <summary>
/// Asks GitHub's releases API for PC Companion's latest release. The request is the same from
/// everyone: it carries nothing that identifies the user or the PC.
/// </summary>
public sealed class GitHubReleaseSource
{
    /// <summary>The largest answer that's read.</summary>
    public const int MaximumAnswerSize = 1024 * 1024;

    /// <summary>GitHub requires a User-Agent; this one names only the app.</summary>
    public const string UserAgent = "PCCompanion";

    /// <summary>How long GitHub has to answer, including the whole release.</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<HttpMessageHandler> _createHandler;
    private readonly TimeProvider _time;

    public GitHubReleaseSource(TimeProvider time)
        : this(CreateHandler, time)
    {
    }

    /// <param name="createHandler">Creates the handler for each request, which is disposed after it.</param>
    /// <param name="time">Times out the request.</param>
    public GitHubReleaseSource(Func<HttpMessageHandler> createHandler, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(createHandler);
        ArgumentNullException.ThrowIfNull(time);

        _createHandler = createHandler;
        _time = time;
    }

    /// <summary>The latest published release that isn't a draft or a pre-release.</summary>
    public static Uri LatestReleaseAddress { get; } = new("https://api.github.com/repos/Lazyb0y/pc-companion/releases/latest");

    /// <summary>The page of the latest release, the only page the update check opens.</summary>
    public static Uri ReleasePageAddress { get; } = new("https://github.com/Lazyb0y/pc-companion/releases/latest");

    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<ReleaseAnswer> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(AnswerTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        // A client for each request, so no connection stays open between checks.
        using var client = new HttpClient(_createHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = CreateRequest();

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            return new ReleaseAnswer(ReleaseAnswerKind.NoAnswer) { Problem = exception.HttpRequestError.ToString(), Exception = exception };
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new ReleaseAnswer(ReleaseAnswerKind.NoAnswer) { Problem = "Timeout", Exception = exception };
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ReleaseAnswer(ReleaseAnswerKind.NoRelease) { StatusCode = status };
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return new ReleaseAnswer(ReleaseAnswerKind.ErrorStatus) { StatusCode = status };
            }

            ReadOnlyMemory<byte>? body;
            try
            {
                body = await ReadAsync(response.Content, linked.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                return Unreadable(status, "it ended early", exception);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                return Unreadable(status, "it took longer than 30 seconds", exception);
            }

            if (body is not { } json)
            {
                return Unreadable(status, "it's larger than 1 MB");
            }

            // The exception isn't kept: its message can quote the answer.
            GitHubRelease? release;
            try
            {
                release = JsonSerializer.Deserialize(json.Span, UpdateJsonContext.Default.GitHubRelease);
            }
            catch (JsonException)
            {
                return Unreadable(status, "it isn't a release in JSON");
            }

            if (release?.TagName is not { } tag)
            {
                return Unreadable(status, "it has no tag");
            }

            return ReleaseVersion.TryParseTag(tag, out var version)
                ? new ReleaseAnswer(ReleaseAnswerKind.Release) { StatusCode = status, Version = version }
                : Unreadable(status, "its tag isn't a version such as v1.2.0");
        }
    }

    // The Windows proxy settings apply, without credentials. No cookies are kept or sent.
    private static SocketsHttpHandler CreateHandler() => new() { UseCookies = false };

    // Only the two headers the API needs. No ETag is sent back, since it could work like a cookie,
    // and no API version, since GitHub ends support for each one.
    private static HttpRequestMessage CreateRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseAddress);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, null));
        return request;
    }

    // Null when the answer is larger than MaximumAnswerSize.
    private static async Task<ReadOnlyMemory<byte>?> ReadAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumAnswerSize)
        {
            return null;
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[MaximumAnswerSize + 1];
            var length = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            if (length > MaximumAnswerSize)
            {
                return null;
            }

            return buffer.AsMemory(0, length);
        }
    }

    private static ReleaseAnswer Unreadable(int status, string problem, Exception? exception = null) =>
        new(ReleaseAnswerKind.Unreadable) { StatusCode = status, Problem = problem, Exception = exception };
}
