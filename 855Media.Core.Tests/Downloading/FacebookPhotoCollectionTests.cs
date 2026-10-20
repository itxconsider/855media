using _855Media.Core.Resolving;
using Xunit;

namespace _855Media.Core.Tests.Downloading;

public class FacebookPhotoCollectionTests
{
    [Theory]
    [InlineData(
        "https://www.facebook.com/chhayee.proeurt.2025/photos",
        "https://www.facebook.com/chhayee.proeurt.2025/photos"
    )]
    [InlineData("facebook.com/example.page", "https://facebook.com/example.page/photos")]
    [InlineData(
        "https://www.facebook.com/profile.php?id=12345&sk=about",
        "https://www.facebook.com/profile.php?id=12345&sk=photos"
    )]
    [InlineData(
        "https://www.facebook.com/people/Example/12345/",
        "https://www.facebook.com/profile.php?id=12345&sk=photos"
    )]
    [InlineData(
        "https://www.facebook.com/example/photos_albums",
        "https://www.facebook.com/example/photos_albums"
    )]
    [InlineData(
        "https://www.facebook.com/photo.php?fbid=12345&set=a.56789",
        "https://www.facebook.com/photo.php?fbid=12345&set=a.56789"
    )]
    [InlineData(
        "https://www.facebook.com/media/set/?set=a.12345",
        "https://www.facebook.com/media/set/?set=a.12345"
    )]
    public void ProfilePageAndPhotoLinks_OpenAccessibleBrowserGallery(string input, string expected)
    {
        Assert.True(FacebookPhotoCollection.TryGetGalleryUrl(input, out var gallery));
        Assert.Equal(expected, gallery);
    }

    [Theory]
    [InlineData("https://facebook.com.evil.test/name/photos")]
    [InlineData("https://notfacebook.com/name/photos")]
    [InlineData("https://www.facebook.com/reel/12345")]
    [InlineData("https://www.facebook.com/watch/?v=12345")]
    [InlineData("https://www.facebook.com/login/")]
    [InlineData("https://www.facebook.com/profile.php?id=")]
    [InlineData("https://www.facebook.com/profile.php?id=abc")]
    [InlineData("https://www.facebook.com/groups/12345")]
    [InlineData("https://example.fbcdn.net/photo.jpg?signature=keep")]
    public void OtherQueries_AreNotRoutedAsProfilePhotos(string input)
    {
        Assert.False(FacebookPhotoCollection.TryGetGalleryUrl(input, out _));
    }
}
