using NUnit.Framework;
using Shouldly;

namespace ImageResize.Tests;

[TestFixture]
public class PathMatchingTests
{
    private static string? GetMatchingRoot(string requestPath, List<string> contentRoots)
    {
        return contentRoots
            .FirstOrDefault(root =>
            {
                var rootPath = root.TrimStart('/');
                // Must match at path boundary: either exact match or followed by '/'
                return requestPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase) ||
                       requestPath.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase);
            });
    }

    [Test]
    public void PathMatching_ValidPaths_ShouldMatch()
    {
        var contentRoots = new List<string> { "img", "images", "media" };

        GetMatchingRoot("images/photo.jpg", contentRoots).ShouldBe("images");
        GetMatchingRoot("img/photo.jpg", contentRoots).ShouldBe("img");
        GetMatchingRoot("media/photo.jpg", contentRoots).ShouldBe("media");
    }

    [Test]
    public void PathMatching_InvalidPaths_ShouldNotMatch()
    {
        var contentRoots = new List<string> { "img", "images", "media" };

        // These should NOT match - but will they? This is the bug test
        GetMatchingRoot("imageshack/photo.jpg", contentRoots).ShouldBeNull();
        GetMatchingRoot("imgtest/photo.jpg", contentRoots).ShouldBeNull();
        GetMatchingRoot("mediafire/photo.jpg", contentRoots).ShouldBeNull();
    }

    [Test]
    public void PathMatching_ExactMatch_ShouldMatch()
    {
        var contentRoots = new List<string> { "img", "images", "media" };

        GetMatchingRoot("images", contentRoots).ShouldBe("images");
        GetMatchingRoot("img", contentRoots).ShouldBe("img");
    }

    [Test]
    public void PathMatching_EdgeCases_ShouldHandle()
    {
        var contentRoots = new List<string> { "img", "images", "media" };

        // Files at root level should not match
        GetMatchingRoot("img.jpg", contentRoots).ShouldBeNull();
        GetMatchingRoot("images.png", contentRoots).ShouldBeNull();

        // Nested paths should work
        GetMatchingRoot("images/subfolder/deep/photo.jpg", contentRoots).ShouldBe("images");
        
        // Case insensitive
        GetMatchingRoot("IMAGES/photo.jpg", contentRoots).ShouldBe("images");
        GetMatchingRoot("Images/photo.jpg", contentRoots).ShouldBe("images");
    }

    [Test]
    public void PathMatching_WithLeadingSlashes_ShouldWork()
    {
        var contentRoots = new List<string> { "/img", "/images", "media" };

        // Should handle leading slashes in contentRoots
        GetMatchingRoot("img/photo.jpg", contentRoots).ShouldBe("/img");
        GetMatchingRoot("images/photo.jpg", contentRoots).ShouldBe("/images");
        GetMatchingRoot("media/photo.jpg", contentRoots).ShouldBe("media");
    }
}

