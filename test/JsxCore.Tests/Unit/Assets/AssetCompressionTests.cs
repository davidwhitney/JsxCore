using System.IO.Compression;
using System.Text;
using JsxCore.Hosting;
using Shouldly;

namespace JsxCore.Tests.Unit.Assets;

/// <summary>
/// Choosing an encoding, and holding the result.
/// </summary>
public class AssetCompressionTests
{
    [Theory]
    [InlineData("br, gzip, deflate", AssetEncoding.Brotli)]
    [InlineData("gzip, deflate", AssetEncoding.Gzip)]
    [InlineData("gzip", AssetEncoding.Gzip)]
    [InlineData("deflate", AssetEncoding.Identity)]
    [InlineData("", AssetEncoding.Identity)]
    [InlineData(null, AssetEncoding.Identity)]
    public void Negotiate_ClientAdvertisesEncodings_PicksTheSmallestItAccepts(string? header, AssetEncoding expected) =>
        AssetCompressionCache.Negotiate(header).ShouldBe(expected);

    [Fact]
    public void Negotiate_EncodingIsRefusedWithQZero_IsNotUsed()
    {
        // "q=0" is how a client says it will not take an encoding, and is the one qualifier worth
        // reading: sending brotli to something that refused it produces an unreadable response.
        AssetCompressionCache.Negotiate("br;q=0, gzip").ShouldBe(AssetEncoding.Gzip);
        AssetCompressionCache.Negotiate("br;q=0, gzip;q=0").ShouldBe(AssetEncoding.Identity);
    }

    [Fact]
    public void Negotiate_EncodingIsMerelyDeprioritised_IsStillUsed() =>
        AssetCompressionCache.Negotiate("br;q=0.1, gzip;q=0.9").ShouldBe(AssetEncoding.Brotli);

    [Fact]
    public void Get_SameBuild_CompressesOnceAndReturnsSomethingThatDecompresses()
    {
        var cache = new AssetCompressionCache();
        var content = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("export const value = 1;\n", 200)));

        var calls = 0;
        byte[] Source() { calls++; return content; }

        var first = cache.Get("build-1", "views/Index.js", AssetEncoding.Gzip, Source);
        var second = cache.Get("build-1", "views/Index.js", AssetEncoding.Gzip, Source);

        calls.ShouldBe(1);
        second.ShouldBeSameAs(first);
        first.Length.ShouldBeLessThan(content.Length);
        Decompress(first).ShouldBe(content);
    }

    [Fact]
    public void Get_BuildIdChanges_DoesNotServeThePreviousBuildsBytes()
    {
        // The same asset name means different content after a rebuild. Holding the old body would
        // serve last build's JavaScript from a URL that has already moved on.
        var cache = new AssetCompressionCache();
        var before = Encoding.UTF8.GetBytes("export const value = 1;");
        var after = Encoding.UTF8.GetBytes("export const value = 2;");

        cache.Get("build-1", "views/Index.js", AssetEncoding.Gzip, () => before);
        var second = cache.Get("build-2", "views/Index.js", AssetEncoding.Gzip, () => after);

        Decompress(second).ShouldBe(after);
    }

    private static byte[] Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
