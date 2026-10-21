using Files.Domain;

namespace Files.Domain;

public class FileValueObjectsTests
{
    [Theory]
    [InlineData("menu.pdf", "menu.pdf")]
    [InlineData("  photo du plat.JPG ", "photo du plat.JPG")]
    [InlineData("archive.tar.gz", "archive.tar.gz")]
    public void FileName_is_trimmed_and_kept(string input, string expected) =>
        Assert.Equal(expected, FileName.Create(input).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FileName_rejects_an_empty_name(string? value) =>
        DomainAssert.Throws(FileErrors.FileNameEmpty, () => FileName.Create(value));

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("dir/menu.pdf")]
    [InlineData("dir\\menu.pdf")]
    [InlineData("/menu.pdf")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("menu\0.pdf")]
    [InlineData("menu\n.pdf")]
    public void FileName_rejects_path_separators_and_control_characters(string value) =>
        DomainAssert.Throws(FileErrors.FileNameInvalid, () => FileName.Create(value));

    [Theory]
    [InlineData("invoice\u202Egnp.exe")] // for\u00E7age droite-\u00E0-gauche : affich\u00E9 \u00AB invoiceexe.png \u00BB
    [InlineData("invoice\u202Dgnp.exe")] // for\u00E7age gauche-\u00E0-droite
    [InlineData("invoice\u2066gnp.exe")] // isolat gauche-\u00E0-droite
    [InlineData("\u200Emenu.pdf")]       // marque gauche-\u00E0-droite
    [InlineData("me\u200Bnu.pdf")]       // espace sans chasse
    [InlineData("me\u200Dnu.pdf")]       // liant sans chasse
    [InlineData("menu.pdf\uFEFF")]       // espace ins\u00E9cable sans chasse / BOM
    [InlineData("me\u00ADnu.pdf")]       // trait d'union conditionnel
    [InlineData("me\U000E0041nu.pdf")]   // caract\u00E8re d'\u00E9tiquette, hors du BMP
    public void FileName_rejects_invisible_and_direction_changing_characters(string value) =>
        DomainAssert.Throws(FileErrors.FileNameInvalid, () => FileName.Create(value));

    [Theory]
    [InlineData("menu\u2028.pdf")] // s\u00e9parateur de ligne
    [InlineData("menu\u2029.pdf")] // s\u00e9parateur de paragraphe
    [InlineData("menu\u0085.pdf")] // ligne suivante (contr\u00f4le)
    public void FileName_rejects_line_and_paragraph_separators(string value) =>
        DomainAssert.Throws(FileErrors.FileNameInvalid, () => FileName.Create(value));

    [Fact]
    public void FileName_rejects_a_lone_surrogate() =>
        DomainAssert.Throws(FileErrors.FileNameInvalid, () => FileName.Create("menu" + (char)0xD83D + ".pdf"));

    [Theory]
    [InlineData("...")]
    [InlineData("....")]
    [InlineData(" ... ")]
    public void FileName_rejects_a_name_made_only_of_dots(string value) =>
        DomainAssert.Throws(FileErrors.FileNameInvalid, () => FileName.Create(value));

    [Theory]
    [InlineData("CON")]
    [InlineData("prn")]
    [InlineData("Aux")]
    [InlineData("NUL")]
    [InlineData("nul.txt")]
    [InlineData("COM1")]
    [InlineData("com9.png")]
    [InlineData("LPT1")]
    [InlineData("lpt9.tar.gz")]
    [InlineData("con .txt")]
    public void FileName_rejects_windows_reserved_device_names(string value) =>
        DomainAssert.Throws(FileErrors.FileNameInvalid, () => FileName.Create(value));

    [Theory]
    [InlineData("console.log")]
    [InlineData("com10.txt")]
    [InlineData("com0.txt")]
    [InlineData("lpt.txt")]
    [InlineData("menu.con")]
    [InlineData("nulle part.pdf")]
    [InlineData("carte été 2026.pdf")]
    [InlineData("メニュー.pdf")]
    [InlineData("menu 🍕.png")]
    [InlineData(".gitignore")]
    [InlineData("a..b")]
    public void FileName_accepts_harmless_names(string value) =>
        Assert.Equal(value, FileName.Create(value).Value);

    [Fact]
    public void FileName_length_is_bounded()
    {
        Assert.Equal(FileName.MaxLength, FileName.Create(new string('a', FileName.MaxLength)).Value.Length);
        DomainAssert.Throws(FileErrors.FileNameTooLong, () => FileName.Create(new string('a', FileName.MaxLength + 1)));
    }

    [Theory]
    [InlineData("menu.PDF", "pdf")]
    [InlineData("archive.tar.gz", "gz")]
    [InlineData("README", "")]
    [InlineData(".gitignore", "")]
    [InlineData("trailing.", "")]
    public void FileName_exposes_a_lowercase_extension(string name, string expected) =>
        Assert.Equal(expected, FileName.Create(name).Extension);

    [Theory]
    [InlineData("image/png", "image/png")]
    [InlineData(" IMAGE/JPEG ", "image/jpeg")]
    [InlineData("text/plain; charset=utf-8", "text/plain")]
    [InlineData("application/vnd.ms-excel", "application/vnd.ms-excel")]
    [InlineData("image/svg+xml", "image/svg+xml")]
    public void ContentType_is_normalized(string input, string expected) =>
        Assert.Equal(expected, ContentType.Create(input).Value);

    [Fact]
    public void ContentType_exposes_type_and_subtype()
    {
        var contentType = ContentType.Create("image/svg+xml");

        Assert.Equal("image", contentType.Type);
        Assert.Equal("svg+xml", contentType.Subtype);
        Assert.Equal(ContentType.Create("IMAGE/SVG+XML"), contentType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("image")]
    [InlineData("image/")]
    [InlineData("/png")]
    [InlineData("image/png/extra")]
    [InlineData("image/*")]
    [InlineData("ima ge/png")]
    [InlineData("; charset=utf-8")]
    public void ContentType_rejects_an_invalid_mime_type(string? value) =>
        DomainAssert.Throws(FileErrors.ContentTypeInvalid, () => ContentType.Create(value));

    [Fact]
    public void ContentType_length_is_bounded() =>
        DomainAssert.Throws(
            FileErrors.ContentTypeInvalid,
            () => ContentType.Create("image/" + new string('a', ContentType.MaxLength)));

    [Fact]
    public void StorageKey_is_kept_as_is()
    {
        var key = StorageKey.Create("tenants/abc/2026/01/9f3c.bin");

        Assert.Equal("tenants/abc/2026/01/9f3c.bin", key.Value);
        Assert.Equal(StorageKey.Create("tenants/abc/2026/01/9f3c.bin"), key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a b")]
    [InlineData("a\tb")]
    public void StorageKey_rejects_an_empty_or_whitespace_key(string? value) =>
        DomainAssert.Throws(FileErrors.StorageKeyInvalid, () => StorageKey.Create(value));

    [Fact]
    public void StorageKey_length_is_bounded()
    {
        Assert.Equal(StorageKey.MaxLength, StorageKey.Create(new string('k', StorageKey.MaxLength)).Value.Length);
        DomainAssert.Throws(FileErrors.StorageKeyInvalid, () => StorageKey.Create(new string('k', StorageKey.MaxLength + 1)));
    }

    [Fact]
    public void FileOwner_normalizes_its_type()
    {
        var ownerId = Guid.NewGuid();

        var owner = FileOwner.Create(" Catalog.Product ", ownerId);

        Assert.Equal("catalog.product", owner.OwnerType);
        Assert.Equal(ownerId, owner.OwnerId);
        Assert.Equal(FileOwner.Create("catalog.product", ownerId), owner);
        Assert.NotEqual(FileOwner.Create("catalog.product", Guid.NewGuid()), owner);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("catalog product")]
    [InlineData("catalog..product")]
    [InlineData("1catalog")]
    [InlineData("catalog/product")]
    [InlineData("product")]
    [InlineData("catalog.")]
    [InlineData(".product")]
    [InlineData("catalog.1product")]
    [InlineData("catalog.pro\nduct")]
    [InlineData("catalog.Pro-duct")]
    public void FileOwner_rejects_an_invalid_type(string? type) =>
        DomainAssert.Throws(FileErrors.OwnerTypeInvalid, () => FileOwner.Create(type, Guid.NewGuid()));

    [Fact]
    public void FileOwner_type_length_is_bounded() =>
        DomainAssert.Throws(
            FileErrors.OwnerTypeInvalid,
            () => FileOwner.Create("files." + new string('a', FileOwner.MaxTypeLength - 5), Guid.NewGuid()));

    [Theory]
    [InlineData("catalog.product")]
    [InlineData("tenants.tenant")]
    [InlineData("tenants.location")]
    [InlineData("reporting.export_job")]
    [InlineData("identity.user")]
    public void FileOwner_accepts_the_canonical_owner_type_codes(string type) =>
        Assert.Equal(type, FileOwner.Create(type, Guid.NewGuid()).OwnerType);

    [Fact]
    public void FileOwner_type_accepts_the_maximum_length()
    {
        var type = "files." + new string('a', FileOwner.MaxTypeLength - 6);

        Assert.Equal(FileOwner.MaxTypeLength, FileOwner.Create(type, Guid.NewGuid()).OwnerType.Length);
    }

    [Fact]
    public void FileOwner_rejects_an_empty_id() =>
        DomainAssert.Throws(FileErrors.OwnerIdEmpty, () => FileOwner.Create("catalog.product", Guid.Empty));

    [Fact]
    public void Default_policy_allows_common_images_and_pdf_up_to_10_megabytes()
    {
        var policy = FileUploadPolicy.Default;

        Assert.Equal(10L * 1024 * 1024, policy.MaxSizeInBytes);
        Assert.True(policy.Allows(ContentType.Create("image/jpeg")));
        Assert.True(policy.Allows(ContentType.Create("image/png")));
        Assert.True(policy.Allows(ContentType.Create("image/webp")));
        Assert.True(policy.Allows(ContentType.Create("application/pdf")));
        Assert.False(policy.Allows(ContentType.Create("application/x-msdownload")));
        Assert.Equal(4, policy.AllowedContentTypes.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Policy_rejects_a_non_positive_max_size(long maxSize) =>
        DomainAssert.Throws(
            FileErrors.PolicyMaxSizeInvalid,
            () => FileUploadPolicy.Create(maxSize, [ContentType.Create("image/png")]));

    [Fact]
    public void Policy_requires_at_least_one_content_type() =>
        DomainAssert.Throws(FileErrors.PolicyContentTypesEmpty, () => FileUploadPolicy.Create(1024, []));

    [Fact]
    public void Policy_deduplicates_content_types_and_compares_by_value()
    {
        var png = ContentType.Create("image/png");
        var pdf = ContentType.Create("application/pdf");

        var policy = FileUploadPolicy.Create(1024, [png, pdf, ContentType.Create("IMAGE/PNG")]);

        Assert.Equal(2, policy.AllowedContentTypes.Count);
        Assert.Equal(FileUploadPolicy.Create(1024, [pdf, png]), policy);
        Assert.NotEqual(FileUploadPolicy.Create(2048, [pdf, png]), policy);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1024, true)]
    [InlineData(1025, false)]
    public void Policy_size_check_is_inclusive_of_the_limit(long size, bool expected) =>
        Assert.Equal(expected, FileUploadPolicy.Create(1024, [ContentType.Create("image/png")]).AllowsSize(size));
}
