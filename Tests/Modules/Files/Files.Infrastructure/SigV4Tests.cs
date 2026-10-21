using Files.Infrastructure.Storage;

namespace Files.Infrastructure;

/// <summary>La signature SigV4, vérifiée sur l'exemple publié par AWS (URL pré-signée d'un GET, documentation S3).</summary>
public sealed class SigV4Tests
{
    [Fact]
    public void A_presigned_url_matches_the_published_aws_example()
    {
        var signer = new SigV4("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "us-east-1");

        var url = signer.Presign(
            "GET",
            new Uri("https://examplebucket.s3.amazonaws.com"),
            "/test.txt",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            TimeSpan.FromSeconds(86400),
            new DateTimeOffset(2013, 5, 24, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(
            "https://examplebucket.s3.amazonaws.com/test.txt?X-Amz-Algorithm=AWS4-HMAC-SHA256"
            + "&X-Amz-Credential=AKIAIOSFODNN7EXAMPLE%2F20130524%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20130524T000000Z"
            + "&X-Amz-Expires=86400&X-Amz-SignedHeaders=host"
            + "&X-Amz-Signature=aeeed9bbccd4d02ee5c0109b86d86835f995330da4c265957d157751f604d404",
            url.AbsoluteUri);
    }

    [Theory]
    [InlineData("tenants/abc/catalog.product/f00", "/bucket/tenants/abc/catalog.product/f00")]
    [InlineData("a b/é", "/bucket/a%20b/%C3%A9")]
    public void Paths_are_encoded_segment_by_segment(string key, string expected)
    {
        Assert.Equal(expected, SigV4.CanonicalPath("bucket", key));
    }
}
