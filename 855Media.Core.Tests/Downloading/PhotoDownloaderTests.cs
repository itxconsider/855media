using System.Net;
using System.Net.Http.Headers;
using _855Media.Core.Downloading;
using _855Media.Core.Resolving;
using Gress;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public sealed class PhotoDownloaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "855-photo-tests-" + Guid.NewGuid().ToString("N")
    );
    private string Destination => Path.Combine(_directory, "photo.jpg");

    public PhotoDownloaderTests() => Directory.CreateDirectory(_directory);

    private static VideoInfo Photo(VideoSource source) =>
        new(
            source,
            "photo",
            "https://www.instagram.com/p/BA/",
            "Caption",
            "Creator",
            null,
            null,
            null,
            ["https://example.test/full.jpg?token=signed%2Bvalue"],
            Caption: "Caption"
        );

    [Theory]
    [InlineData(VideoSource.InstagramPhoto, "https://www.instagram.com/")]
    [InlineData(VideoSource.FacebookPhoto, "https://www.facebook.com/")]
    public async Task ImageCopy_PreservesBytesAndUsesSourceReferer(
        VideoSource source,
        string referer
    )
    {
        byte[] bytes = [0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4];
        using var handler = new Handler(request =>
        {
            Assert.Equal(referer, request.Headers.Referrer!.AbsoluteUri);
            Assert.Contains("token=signed%2Bvalue", request.RequestUri!.AbsoluteUri);
            Assert.False(request.Headers.Contains("Cookie"));
            return Content(bytes, "image/jpeg");
        });
        using var client = new HttpClient(handler);
        await new PhotoDownloader(client).DownloadPhotoAsync(Destination, Photo(source));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Destination));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task LoginHtml_CannotReplaceAnExistingPhoto()
    {
        await File.WriteAllTextAsync(Destination, "existing photo");
        using var handler = new Handler(_ =>
            Content("<html>Login required</html>"u8.ToArray(), "text/html")
        );
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PhotoDownloader(client).DownloadPhotoAsync(
                Destination,
                Photo(VideoSource.InstagramPhoto)
            )
        );
        Assert.Equal("existing photo", await File.ReadAllTextAsync(Destination));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Cancellation_RemovesPartialFileAndKeepsExistingPhoto()
    {
        await File.WriteAllTextAsync(Destination, "existing photo");
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler(_ => Content(new byte[200_000], "image/jpeg"));
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PhotoDownloader(client).DownloadPhotoAsync(
                Destination,
                Photo(VideoSource.InstagramPhoto),
                new CancelProgress(cancellation),
                cancellation.Token
            )
        );
        Assert.Equal("existing photo", await File.ReadAllTextAsync(Destination));
        Assert.Single(Directory.GetFiles(_directory));
    }

    private static HttpResponseMessage Content(byte[] bytes, string type)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return response;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(send(request));
    }

    private sealed class CancelProgress(CancellationTokenSource cancellation)
        : IProgress<Percentage>
    {
        public void Report(Percentage value) => cancellation.Cancel();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
