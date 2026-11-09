using System.Reflection;
using _855Media.Core.LDPlayer;
using Xunit;

namespace _855Media.Core.Tests.LDPlayer;

public class PFlowGraphIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeviceGraphReadersDoNotExtractTokensOrMakeHttpRequests(bool uidOnly)
    {
        var ld = DispatchProxy.Create<ILDPlayerService, PFlowRootRemovalTests.DenyDevice>();
        using var handler = new PFlowRootRemovalTests.DenyHttp();
        using var http = new HttpClient(handler);
        var reader = new PFlowGraphIdentityReader(http);
        Assert.Null(
            uidOnly
                ? await reader.TryReadUidAsync(ld, 12, null, default)
                : await reader.TryReadAsync(ld, 12, null, default)
        );
        Assert.Equal(0, ((PFlowRootRemovalTests.DenyDevice)(object)ld).Reads + handler.Reads);
    }

    [Fact]
    public void CachedPictureFallbackAcceptsOnlyTheNamedSecurePictureField()
    {
        Assert.Equal(
            "https://example.test/picture.jpg",
            LDPlayerService.ReadRootProfilePicture(
                "profile_pic_url\0https://example.test/picture.jpg\0"
            )
        );
        Assert.Empty(
            LDPlayerService.ReadRootProfilePicture("website\nhttps://example.test/picture.jpg")
        );
        Assert.Empty(
            LDPlayerService.ReadRootProfilePicture(
                "profile_pic_url\nhttp://example.test/picture.jpg"
            )
        );
    }
}
