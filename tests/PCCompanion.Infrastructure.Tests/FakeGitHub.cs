using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PCCompanion.Infrastructure.Tests;

/// <summary>
/// Stands in for GitHub's API: records each request and answers it with <see cref="Respond"/>,
/// through a new handler for each request, as the release source creates them.
/// </summary>
internal sealed class FakeGitHub
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        (_, _) => Task.FromResult(Release("v0.2.0"));

    public List<RecordedRequest> Requests { get; } = [];

    public int HandlersCreated { get; private set; }

    public int HandlersDisposed { get; private set; }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>An answer like GitHub's, with fields around the tag.</summary>
    public static HttpResponseMessage Release(string tag) => Json($$"""
        {
          "url": "https://api.github.com/repos/Lazyb0y/pc-companion/releases/1",
          "html_url": "https://github.com/Lazyb0y/pc-companion/releases/tag/{{tag}}",
          "id": 1,
          "author": { "login": "Lazyb0y", "id": 2 },
          "tag_name": "{{tag}}",
          "name": "PC Companion Preview",
          "draft": false,
          "prerelease": false,
          "assets": [ { "name": "PCCompanion-x64.msi", "size": 123 } ],
          "body": "Notes"
        }
        """);

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent("{}") };

    public HttpMessageHandler CreateHandler()
    {
        HandlersCreated++;
        return new Handler(this);
    }

    internal sealed record RecordedRequest(HttpMethod Method, Uri? Address, IReadOnlyList<(string Name, string Value)> Headers, bool HasContent);

    private sealed class Handler(FakeGitHub gitHub) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            gitHub.Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                [.. request.Headers.Select(header => (header.Key, string.Join(", ", header.Value)))],
                request.Content is not null));
            return gitHub.Respond(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                gitHub.HandlersDisposed++;
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>A stream whose length isn't known, as with a chunked answer.</summary>
internal sealed class UnknownLengthStream(byte[] data, bool failPartWay = false) : Stream
{
    private int _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (failPartWay && _position > 0)
        {
            throw new IOException("The connection was closed.");
        }

        var length = Math.Min(Math.Min(count, data.Length - _position), 4096);
        Array.Copy(data, _position, buffer, offset, length);
        _position += length;
        return length;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
