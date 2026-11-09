using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using _855Media.Core.Resolving;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public sealed class InstagramProfileImportTests
{
    private const string Page = "https://www.instagram.com/p/ABC/";
    private const string Full = "https://scontent.fixture.cdninstagram.com/full.jpg?token=a%2Bb";

    private static JsonObject Photo(string code = "ABC") =>
        JsonNode
            .Parse(
                """
                {"code":"ABC","user":{"username":"owner"},"caption":{"text":"Full caption\nខ្មែរ 😊"},
                 "media_type":1,"original_width":1200,"original_height":1600,
                 "image_versions2":{"candidates":[
                   {"url":"https://scontent.fixture.cdninstagram.com/thumb.jpg","width":206,"height":206},
                   {"url":"https://scontent.fixture.cdninstagram.com/full.jpg?token=a%2Bb","width":1200,"height":1600}]}}
                """
            )!
            .AsObject()
            .WithCode(code);

    private static IReadOnlyList<VideoInfo> Media(
        JsonObject node,
        InstagramMediaKind kind = InstagramMediaKind.All
    )
    {
        var post = Assert.Single(InstagramProfileMedia.Parse(node.ToJsonString(), "owner"));
        return InstagramBrowserCollection.ParsePost(Page, post.Json, kind);
    }

    [Fact]
    public void LoadedBatchPreservesFullSizeSignedUrlsAndCaptionsWithoutImportingOtherAuthors()
    {
        var foreign = Photo("FOREIGN");
        foreign["user"]!["username"] = "someone_else";
        var json = new JsonObject { ["data"] = new JsonArray(Photo(), foreign, Photo("SECOND")) };
        var posts = InstagramProfileMedia.Parse(json.ToJsonString(), "OWNER");
        Assert.Equal(2, posts.Count);
        var media = Assert.Single(
            InstagramBrowserCollection.ParsePost(
                Page,
                posts.Single(p => p.Code == "ABC").Json,
                InstagramMediaKind.All
            )
        );
        Assert.Equal(Full, Assert.Single(media.ThumbnailUrls));
        Assert.Equal("Full caption\nខ្មែរ 😊", media.Caption);
    }

    [Theory]
    [InlineData("dimensions")]
    [InlineData("thumbnail")]
    [InlineData("preview")]
    [InlineData("cropped")]
    [InlineData("caption")]
    [InlineData("truncated")]
    [InlineData("blob")]
    [InlineData("type")]
    public void UnprovenProfileSummariesRequireTheIndividualPost(string defect)
    {
        var photo = Photo();
        switch (defect)
        {
            case "dimensions":
                photo.Remove("original_width");
                break;
            case "thumbnail":
                photo["image_versions2"]!["candidates"]!.AsArray().RemoveAt(1);
                break;
            case "preview":
                photo["image_versions2"]!["candidates"]![1]!["width"] = 600;
                photo["image_versions2"]!["candidates"]![1]!["height"] = 800;
                break;
            case "cropped":
                photo["image_versions2"]!["candidates"]![1]!["height"] = 1200;
                break;
            case "caption":
                photo.Remove("caption");
                break;
            case "truncated":
                photo["caption_is_truncated"] = true;
                break;
            case "blob":
                photo["image_versions2"]!["candidates"]![1]!["url"] =
                    "blob:https://www.instagram.com/cover";
                break;
            case "type":
                photo.Remove("media_type");
                break;
        }
        Assert.Empty(InstagramProfileMedia.Parse(photo.ToJsonString(), "owner"));
    }

    [Fact]
    public void ExplicitCaptionlessSmallOriginalIsAcceptedWithoutUsingAltOrComments()
    {
        var photo = Photo();
        photo["caption"] = null;
        photo["original_width"] = 206;
        photo["original_height"] = 206;
        photo["image_versions2"]!["candidates"]!.AsArray().RemoveAt(1);
        Assert.Equal("", Assert.Single(Media(photo)).Caption);
    }

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(4, 3, false)]
    [InlineData(0, 3, false)]
    [InlineData(3, 2, false)]
    public void CarouselRequiresAllChildrenBeforeAnyMediaCanBeImported(
        int count,
        int children,
        bool accepted
    )
    {
        var post = Photo();
        post["media_type"] = 8;
        post["carousel_media_count"] = count;
        post["carousel_media"] = new JsonArray(
            Enumerable
                .Range(0, children)
                .Select(i =>
                {
                    var photo = Photo("CHILD" + i);
                    photo["image_versions2"]!["candidates"]![1]!["url"] = Full.Replace(
                        "full.jpg",
                        $"full{i}.jpg"
                    );
                    return (JsonNode)photo;
                })
                .ToArray()
        );
        var posts = InstagramProfileMedia.Parse(post.ToJsonString(), "owner");
        Assert.Equal(accepted, posts.Any(p => p.Code == "ABC"));
        if (accepted)
            Assert.Equal(3, Media(post).Count);
    }

    [Fact]
    public void MixedCarouselKeepsOriginalIndicesAndVideoRendition()
    {
        var video = Photo("CHILD");
        video["media_type"] = 2;
        video["video_versions"] = JsonNode.Parse(
            """
            [{"url":"https://video.fixture.fbcdn.net/small.mp4","width":360,"height":640},
             {"url":"https://video.fixture.fbcdn.net/large.mp4?signed=exact","width":1080,"height":1920}]
            """
        );
        var post = Photo();
        post["media_type"] = 8;
        post["carousel_media_count"] = 2;
        post["carousel_media"] = new JsonArray(Photo("CHILD1"), video);
        var videos = Media(post, InstagramMediaKind.Videos);
        Assert.Equal("ABC_2", Assert.Single(videos).Id);
        Assert.Equal(
            "https://video.fixture.fbcdn.net/large.mp4?signed=exact",
            videos[0].DirectMediaUrl
        );
        Assert.Equal("Full caption\nខ្មែរ 😊", videos[0].Caption);
    }

    [Fact]
    public void GraphResponseUsesDimensionsDisplayResourcesAndExactCaptionEdges()
    {
        var post = Photo();
        post.Remove("code");
        post["shortcode"] = "ABC";
        post.Remove("caption");
        post["edge_media_to_caption"] = JsonNode.Parse(
            """{"edges":[{"node":{"text":"Graph caption"}}]}"""
        );
        post["display_resources"] = JsonNode.Parse(
            """[{"src":"https://scontent.fixture.cdninstagram.com/graph.jpg","config_width":1200,"config_height":1600}]"""
        );
        post.Remove("image_versions2");
        Assert.Equal("Graph caption", Assert.Single(Media(post)).Caption);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"caption\":{\"code\":\"ABC\"}}")]
    public void UnsupportedResponsesYieldNoFastPath(string json) =>
        Assert.Empty(InstagramProfileMedia.Parse(json, "owner"));

    [Theory]
    [InlineData("owner")]
    [InlineData("code")]
    [InlineData("type")]
    public void ContradictoryStructuredIdentityOrTypeCannotEnterFastPath(string contradiction)
    {
        var photo = Photo();
        if (contradiction == "owner")
            photo["owner"] = new JsonObject { ["username"] = "different" };
        if (contradiction == "code")
            photo["shortcode"] = "DIFFERENT";
        if (contradiction == "type")
            photo["is_video"] = true;
        Assert.Empty(InstagramProfileMedia.Parse(photo.ToJsonString(), "owner"));
    }

    [Fact]
    public void OversizedOrExcessivelyNestedPayloadFallsBackWithoutUnboundedTraversal()
    {
        Assert.Empty(
            InstagramProfileMedia.Parse(
                new string(' ', InstagramProfileMedia.MaximumJsonLength + 1),
                "owner"
            )
        );
        Assert.Empty(
            InstagramProfileMedia.Parse(new string('[', 70) + "0" + new string(']', 70), "owner")
        );
    }

    [Theory]
    [InlineData("?oe=00000001", false)]
    [InlineData("?expires=1", false)]
    [InlineData("?exp=invalid", false)]
    [InlineData("?oe=FFFFFFFF", true)]
    [InlineData("?token=a%2Bb", true)]
    public void ExpiredOrUnparseableSignedExpiryFallsBack(string query, bool expected) =>
        Assert.Equal(
            expected,
            InstagramProfileMedia.IsFreshMediaUrl(Full.Split('?')[0] + query, DateTimeOffset.UtcNow)
        );

    [Fact]
    public async Task LoadedPostsAndResumeAvoidAllDetailRequestsAndPacingDelays()
    {
        var items = Media(Photo());
        var opened = 0;
        var queued = 0;
        var saved = 0;
        var result = await InstagramImportRunner.RunAsync(
            Enumerable.Range(0, 1000).Select(i => $"https://www.instagram.com/p/P{i}/").ToArray(),
            _ => Task.CompletedTask,
            url => url.EndsWith("P0/"),
            _ => items,
            (_, _) =>
            {
                opened++;
                return Task.FromResult(items);
            },
            (media, _) =>
            {
                queued += media.Count;
                return Task.FromResult(media.Count);
            },
            (_, _, _) =>
            {
                saved++;
                return Task.CompletedTask;
            },
            _ => { },
            CancellationToken.None
        );
        Assert.Equal(0, opened);
        Assert.Equal(999, result.Captured);
        Assert.Equal(1, result.Resumed);
        Assert.Equal(999, saved);
        Assert.Equal(999, queued);
    }

    [Fact]
    public async Task MissingPostsRemainSequentialAndPacedWhileCapturedPostsDoNotWait()
    {
        var items = Media(Photo());
        var opened = new List<TimeSpan>();
        var watch = Stopwatch.StartNew();
        var result = await InstagramImportRunner.RunAsync(
            [Page, Page.Replace("ABC", "SECOND"), Page.Replace("ABC", "THIRD")],
            _ => Task.CompletedTask,
            _ => false,
            url => url.Contains("SECOND") ? items : null,
            (_, _) =>
            {
                opened.Add(watch.Elapsed);
                return Task.FromResult(items);
            },
            (_, _) => Task.FromResult(1),
            (_, _, _) => Task.CompletedTask,
            _ => { },
            CancellationToken.None,
            interval: TimeSpan.FromMilliseconds(100)
        );
        Assert.Equal(2, result.Opened);
        Assert.Equal(1, result.Captured);
        Assert.True(opened[1] - opened[0] >= TimeSpan.FromMilliseconds(90));
    }

    [Fact]
    public async Task CancellationAndAccessStopsApplyEvenToCapturedAndResumedPosts()
    {
        var queued = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InstagramImportRunner.RunAsync(
                [Page],
                _ => throw new InvalidOperationException("rate limit or access"),
                _ => true,
                _ => Media(Photo()),
                (_, _) => throw new Exception("must not open"),
                (_, _) =>
                {
                    queued = true;
                    return Task.FromResult(1);
                },
                (_, _, _) => Task.CompletedTask,
                _ => { },
                CancellationToken.None
            )
        );
        Assert.False(queued);
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InstagramImportRunner.RunAsync(
                [Page],
                _ => Task.CompletedTask,
                _ => false,
                _ => Media(Photo()),
                (_, _) => throw new Exception("must not open"),
                (_, _) =>
                {
                    cancel.Cancel();
                    return Task.FromResult(1);
                },
                (_, _, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                _ => { },
                cancel.Token
            )
        );
    }

    [Fact]
    public async Task RestartResumeNeedsManifestAndAllCompletedFilesNotQueuedPlaceholders()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "855-ig-progress-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var store = new InstagramImportProgress(root, "owner/destination/photos");
            var items = Media(Photo());
            var file = Path.Combine(root, "image.jpg");
            await store.RememberAsync(Page, items, CancellationToken.None);
            var restarted = new InstagramImportProgress(root, "owner/destination/photos");
            bool Complete(string _, InstagramImportItem item) =>
                item.Id == "ABC_1" && File.Exists(file) && new FileInfo(file).Length == 4;
            Assert.False(restarted.CanSkip(Page, Complete));
            File.WriteAllBytes(file, []);
            Assert.False(restarted.CanSkip(Page, Complete));
            File.WriteAllBytes(file, [1, 2, 3, 4]);
            Assert.True(restarted.CanSkip(Page, Complete));
            Assert.False(
                new InstagramImportProgress(root, "other/destination/photos").CanSkip(
                    Page,
                    Complete
                )
            );
            Assert.False(
                new InstagramImportProgress(root, "owner/other-destination/photos").CanSkip(
                    Page,
                    Complete
                )
            );
            Assert.False(
                new InstagramImportProgress(root, "owner/destination/videos").CanSkip(
                    Page,
                    Complete
                )
            );
            var manifest = File.ReadAllText(Directory.GetFiles(root, "*.jsonl").Single());
            Assert.DoesNotContain("token=", manifest);
            Assert.DoesNotContain("cdninstagram", manifest);
            Assert.DoesNotContain("Full caption", manifest);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ActiveCollectionAddsNewPostsWhileDownloadsAreAlreadyQueued()
    {
        var items = Media(Photo());
        var queued = 0;
        var discovered = 0;
        var second = Page.Replace("ABC", "SECOND");
        var third = Page.Replace("ABC", "THIRD");
        var result = await InstagramImportRunner.RunAsync(
            [Page],
            _ => Task.CompletedTask,
            _ => false,
            _ => items,
            (_, _) => throw new Exception("No detail request expected"),
            (_, _) => Task.FromResult(++queued > 0 ? 1 : 0),
            (_, _, _) => Task.CompletedTask,
            _ => { },
            CancellationToken.None,
            discover: _ =>
            {
                Assert.True(queued > 0); // Discovery continues after the first download was queued.
                discovered++;
                return Task.FromResult(
                    discovered == 1
                        ? new InstagramCollectionSnapshot([Page, second], true)
                        : new InstagramCollectionSnapshot([Page, second, third], false)
                );
            }
        );
        Assert.Equal(3, result.Posts);
        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Queued);
        Assert.Equal(0, result.Opened);
    }

    [Fact]
    public async Task StopCancelsWaitingForMoreProfilePosts()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            InstagramImportRunner.RunAsync(
                [],
                _ => Task.CompletedTask,
                _ => false,
                _ => null,
                (_, _) => throw new Exception("No posts to load"),
                (_, _) => Task.FromResult(0),
                (_, _, _) => Task.CompletedTask,
                _ => { },
                cancel.Token,
                discover: _ => Task.FromResult(new InstagramCollectionSnapshot([], true))
            )
        );
    }

    [Theory]
    [InlineData("{\"Code\":\"ABC\",\"Items\":[null]}")]
    [InlineData("{\"Code\":\"ABC\",\"Items\":[{\"Id\":\"WRONG_1\"}]}")]
    [InlineData("{\"Code\":\"ABC\",\"Items\":[{\"Id\":\"ABC_1\"},{\"Id\":\"ABC_1\"}]}")]
    [InlineData("{\"Code\":\"ABC\",\"Items\":[]}")]
    [InlineData("{\"Code\":\"ABC\",\"Items\":")]
    public void CorruptCheckpointCannotSkipPosts(string invalid)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "855-ig-corrupt-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        try
        {
            var scope = "test-profile";
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(scope)
                )
            );
            File.WriteAllText(Path.Combine(root, hash + ".jsonl"), invalid);
            Assert.False(new InstagramImportProgress(root, scope).CanSkip(Page, (_, _) => true));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

internal static class InstagramFixtureExtensions
{
    public static JsonObject WithCode(this JsonObject value, string code)
    {
        value["code"] = code;
        return value;
    }
}
