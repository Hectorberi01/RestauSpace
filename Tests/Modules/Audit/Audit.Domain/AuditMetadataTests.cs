using Audit.Domain;

namespace Audit.Domain;

public class AuditMetadataTests
{
    private static AuditMetadata Build(params (string Key, string Value)[] entries) =>
        AuditMetadata.Create(entries.Select(e => new KeyValuePair<string, string>(e.Key, e.Value)));

    [Fact]
    public void Create_keeps_non_sensitive_entries()
    {
        var metadata = Build(("ip", "10.0.0.1"), (" reason ", "customer request"));

        Assert.Equal(2, metadata.Values.Count);
        Assert.Equal("10.0.0.1", metadata.Values["ip"]);
        Assert.Equal("customer request", metadata.Values["reason"]);
    }

    [Fact]
    public void Create_without_entries_returns_empty()
    {
        Assert.Same(AuditMetadata.Empty, AuditMetadata.Create(null));
        Assert.Empty(AuditMetadata.Empty.Values);
        Assert.Equal(AuditMetadata.Empty, Build());
    }

    [Theory]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("newPassword")]
    [InlineData("old_password_hash")]
    [InlineData("client_secret")]
    [InlineData("SecretReference")]
    [InlineData("access_token")]
    [InlineData("refreshToken")]
    [InlineData("Authorization")]
    [InlineData("api_key")]
    [InlineData("Api-Key")]
    [InlineData("apiKey")]
    [InlineData("X-API-KEY")]
    [InlineData("card number")]
    [InlineData("cardNumber")]
    [InlineData("card_number")]
    [InlineData("CVV")]
    [InlineData("cvc2")]
    [InlineData("private_key")]
    [InlineData("session-cookie")]
    [InlineData("credentials")]
    public void Create_redacts_the_value_of_a_sensitive_key(string key)
    {
        var metadata = Build((key, "hunter2"));

        Assert.Equal(AuditMetadata.RedactedMarker, metadata.Values[key]);
        Assert.DoesNotContain("hunter2", metadata.Values.Values);
    }

    [Fact]
    public void Create_redacts_only_the_sensitive_keys()
    {
        var metadata = Build(("email", "a@b.co"), ("password", "hunter2"), ("ip", "10.0.0.1"));

        Assert.Equal("a@b.co", metadata.Values["email"]);
        Assert.Equal(AuditMetadata.RedactedMarker, metadata.Values["password"]);
        Assert.Equal("10.0.0.1", metadata.Values["ip"]);
    }

    [Fact]
    public void Create_redacts_a_sensitive_value_even_when_it_exceeds_the_length_limit()
    {
        var metadata = Build(("token", new string('x', AuditMetadata.MaxValueLength * 3)));

        Assert.Equal(AuditMetadata.RedactedMarker, metadata.Values["token"]);
    }

    [Theory]
    [InlineData("ip")]
    [InlineData("reason")]
    [InlineData("order_number")]
    [InlineData("card_brand")]
    public void IsSensitiveKey_is_false_for_ordinary_keys(string key) => Assert.False(AuditMetadata.IsSensitiveKey(key));

    [Fact]
    public void Sensitive_fragment_list_covers_the_minimum_required_by_the_spec()
    {
        foreach (var fragment in new[] { "password", "secret", "token", "authorization", "apikey", "cardnumber", "cvv" })
        {
            Assert.Contains(fragment, AuditMetadata.SensitiveKeyFragments);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_key(string key) =>
        DomainAssert.Throws(AuditErrors.MetadataKeyInvalid, () => Build((key, "v")));

    [Fact]
    public void Create_rejects_a_null_key() =>
        DomainAssert.Throws(AuditErrors.MetadataKeyInvalid, () => Build((null!, "v")));

    [Fact]
    public void Create_rejects_a_key_over_the_limit()
    {
        Assert.Single(Build((new string('k', AuditMetadata.MaxKeyLength), "v")).Values);
        DomainAssert.Throws(
            AuditErrors.MetadataKeyInvalid,
            () => Build((new string('k', AuditMetadata.MaxKeyLength + 1), "v")));
    }

    [Fact]
    public void Create_rejects_duplicate_keys_ignoring_case() =>
        DomainAssert.Throws(AuditErrors.MetadataKeyDuplicated, () => Build(("Reason", "a"), ("reason", "b")));

    [Fact]
    public void Create_rejects_a_null_value() =>
        DomainAssert.Throws(AuditErrors.MetadataValueMissing, () => Build(("reason", null!)));

    [Fact]
    public void Create_rejects_a_value_over_the_limit()
    {
        Assert.Single(Build(("reason", new string('v', AuditMetadata.MaxValueLength))).Values);
        DomainAssert.Throws(
            AuditErrors.MetadataValueTooLong,
            () => Build(("reason", new string('v', AuditMetadata.MaxValueLength + 1))));
    }

    [Fact]
    public void Create_rejects_more_entries_than_the_limit()
    {
        static (string, string)[] Entries(int count) => [.. Enumerable.Range(0, count).Select(i => ($"k{i}", "v"))];

        Assert.Equal(AuditMetadata.MaxEntries, Build(Entries(AuditMetadata.MaxEntries)).Values.Count);
        DomainAssert.Throws(AuditErrors.MetadataTooManyEntries, () => Build(Entries(AuditMetadata.MaxEntries + 1)));
    }

    [Fact]
    public void Equality_ignores_entry_order()
    {
        Assert.Equal(Build(("a", "1"), ("b", "2")), Build(("b", "2"), ("a", "1")));
        Assert.NotEqual(Build(("a", "1")), Build(("a", "2")));
    }

    [Fact]
    public void Values_cannot_be_mutated_through_the_exposed_type()
    {
        var metadata = Build(("ip", "10.0.0.1"));

        var dictionary = Assert.IsAssignableFrom<IDictionary<string, string>>(metadata.Values);
        Assert.True(dictionary.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("x", "y"));
        Assert.Throws<NotSupportedException>(() => dictionary["ip"] = "changed");
        Assert.Throws<NotSupportedException>(() => dictionary.Remove("ip"));
        Assert.Throws<NotSupportedException>(dictionary.Clear);
        Assert.Equal("10.0.0.1", metadata.Values["ip"]);
    }

    [Fact]
    public void Values_are_a_copy_of_the_source()
    {
        var source = new Dictionary<string, string> { ["ip"] = "10.0.0.1" };
        var metadata = AuditMetadata.Create(source);

        source["ip"] = "changed";
        source["password"] = "hunter2";

        Assert.Equal("10.0.0.1", Assert.Single(metadata.Values).Value);
    }

    // ---- Masquage étendu des clés

    [Theory]
    [InlineData("pwd")]
    [InlineData("user_pwd")]
    [InlineData("pin")]
    [InlineData("PIN")]
    [InlineData("pinCode")]
    [InlineData("pincode")]
    [InlineData("user_pin")]
    [InlineData("pin2")]
    [InlineData("otp")]
    [InlineData("otpCode")]
    [InlineData("sms-otp")]
    [InlineData("bearer")]
    [InlineData("jwt")]
    [InlineData("idJwt")]
    [InlineData("session")]
    [InlineData("sessionId")]
    [InlineData("signature")]
    [InlineData("webhook_signature")]
    [InlineData("auth")]
    [InlineData("x-auth")]
    [InlineData("key")]
    [InlineData("signingKey")]
    [InlineData("connectionString")]
    [InlineData("connection_string")]
    [InlineData("ConnectionStrings:Default")]
    [InlineData("motdepasse")]
    [InlineData("mot_de_passe")]
    [InlineData("Mot de passe")]
    [InlineData("mdp")]
    [InlineData("nouveau_mdp")]
    [InlineData("iban")]
    [InlineData("customerIban")]
    [InlineData("ssn")]
    [InlineData("userSSN")]
    [InlineData("SSNNumber")]
    [InlineData("salt")]
    [InlineData("password_salt")]
    [InlineData("hashSalt")]
    [InlineData("credential")]
    [InlineData("cookie")]
    public void Create_redacts_every_listed_sensitive_key(string key)
    {
        var metadata = Build((key, "hunter2"));

        Assert.True(AuditMetadata.IsSensitiveKey(key));
        Assert.Equal(AuditMetadata.RedactedMarker, metadata.Values[key]);
    }

    [Theory]
    [InlineData("ｐａｓｓｗｏｒｄ")]             // lettres pleine chasse
    [InlineData("ＡＰＩ＿ＫＥＹ")]               // lettres et séparateur pleine chasse
    [InlineData("p.a.s.s.w.o.r.d")]
    [InlineData("pass-word")]
    [InlineData("pass word")]
    [InlineData("pass\u200Bword")]             // espace de largeur nulle
    [InlineData("to\u00ADken")]                // trait d'union conditionnel
    [InlineData("ⓢⓔⓒⓡⓔⓣ")]                 // lettres cerclées
    [InlineData("s_e_c_r_e_t")]
    public void Create_redacts_width_and_separator_variants_of_a_sensitive_key(string key)
    {
        var metadata = Build((key, "hunter2"));

        Assert.Equal(AuditMetadata.RedactedMarker, metadata.Values[key]);
    }

    [Fact]
    public void A_key_that_is_not_valid_unicode_is_treated_as_sensitive()
    {
        var key = "pass" + (char)0xD800 + "word";

        Assert.True(AuditMetadata.IsSensitiveKey(key));
        Assert.Equal(AuditMetadata.RedactedMarker, Build((key, "hunter2")).Values[key]);
    }

    [Theory]
    [InlineData("ip")]
    [InlineData("reason")]
    [InlineData("order_number")]
    [InlineData("card_brand")]
    [InlineData("shippingAddress")]   // contient "pin"
    [InlineData("shipping")]
    [InlineData("opinion")]
    [InlineData("pinned")]
    [InlineData("mapping")]
    [InlineData("slotPosition")]      // contient "otp"
    [InlineData("businessName")]      // contient "ssn"
    [InlineData("addressNumber")]
    [InlineData("cmdParam")]          // contient "mdp"
    [InlineData("saltedCaramel")]     // contient "salt"
    [InlineData("status")]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("customerId")]
    [InlineData("tableNumber")]
    [InlineData("email")]
    [InlineData("userAgent")]
    [InlineData("previousStatus")]
    public void Harmless_keys_are_not_redacted(string key)
    {
        Assert.False(AuditMetadata.IsSensitiveKey(key));
        Assert.Equal("paid", Build((key, "paid")).Values[key]);
    }

    [Fact]
    public void Broad_fragments_over_redact_on_purpose()
    {
        // Prix documenté de "key", "auth" et "session" : ces clés inoffensives sont masquées elles aussi.
        Assert.True(AuditMetadata.IsSensitiveKey("monkey"));
        Assert.True(AuditMetadata.IsSensitiveKey("author"));
        Assert.True(AuditMetadata.IsSensitiveKey("tableSessionId"));
    }

    [Fact]
    public void Sensitive_key_lists_cover_every_reviewed_fragment()
    {
        var listed = AuditMetadata.SensitiveKeyFragments.Concat(AuditMetadata.SensitiveKeyWords).ToList();

        foreach (var fragment in new[]
        {
            "pwd", "pin", "otp", "bearer", "jwt", "session", "signature", "auth", "key", "connectionstring",
            "motdepasse", "mdp", "iban", "ssn", "salt", "credential", "cookie",
        })
        {
            Assert.Contains(fragment, listed);
        }
    }

    [Fact]
    public void Sensitive_key_lists_cannot_be_modified_through_a_downcast()
    {
        var fragments = Assert.IsAssignableFrom<IList<string>>(AuditMetadata.SensitiveKeyFragments);
        var words = Assert.IsAssignableFrom<IList<string>>(AuditMetadata.SensitiveKeyWords);

        Assert.IsNotType<string[]>(AuditMetadata.SensitiveKeyFragments);
        Assert.IsNotType<string[]>(AuditMetadata.SensitiveKeyWords);
        Assert.Throws<NotSupportedException>(() => fragments[0] = "nothing");
        Assert.Throws<NotSupportedException>(fragments.Clear);
        Assert.Throws<NotSupportedException>(() => words[0] = "nothing");
        Assert.True(AuditMetadata.IsSensitiveKey("password"));
    }

    // ---- Masquage par valeur

    private const string Jwt =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";

    [Theory]
    [InlineData("Bearer abcdef123456")]
    [InlineData("bearer abcdef123456")]
    [InlineData("BEARER abcdef123456")]
    [InlineData("  Bearer abcdef123456")]
    [InlineData("Basic dXNlcjpwYXNzd29yZA==")]
    [InlineData("basic dXNlcjpwYXNzd29yZA==")]
    public void Create_redacts_an_http_credential_stored_under_an_innocuous_key(string value) =>
        AssertRedacted("header", value);

    [Theory]
    [InlineData("-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASC\n-----END PRIVATE KEY-----")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----")]
    [InlineData("cert: -----BEGIN CERTIFICATE----- MIIC")]
    public void Create_redacts_a_pem_block(string value) => AssertRedacted("note", value);

    [Fact]
    public void Create_redacts_a_json_web_token()
    {
        AssertRedacted("note", Jwt);
        AssertRedacted("note", "received " + Jwt + " from the client");
        AssertRedacted("url", "https://example.com/callback#id=" + Jwt);
    }

    [Theory]
    [InlineData("https://example.com/reset?token=abc123")]
    [InlineData("https://example.com/login?user=ada&password=hunter2")]
    [InlineData("grant_type=client_credentials&client_secret=s3cr3t")]
    [InlineData("https://maps.example.com/api?api_key=AIzaSyD")]
    [InlineData("https://maps.example.com/api?apikey=AIzaSyD")]
    [InlineData("https://maps.example.com/api?key=AIzaSyD")]
    [InlineData("access_token=abc&expires_in=3600")]
    [InlineData("Server=db;User Id=sa;Password=hunter2;")]
    [InlineData("pwd=hunter2")]
    [InlineData("pin=1234")]
    [InlineData("otp: 123456")]
    [InlineData("mot de passe: 1234")]
    [InlineData("mdp=1234")]
    [InlineData("sessionid=abc123")]
    public void Create_redacts_a_value_holding_a_named_secret_pair(string value) => AssertRedacted("note", value);

    [Theory]
    [InlineData("{\"user\":\"ada\",\"password\":\"hunter2\"}")]
    [InlineData("{ \"token\" : \"abc\" }")]
    [InlineData("{\"client_secret\":\"abc\"}")]
    [InlineData("{\"apiKey\":\"abc\"}")]
    [InlineData("{\"api_key\":\"abc\"}")]
    [InlineData("{\"refreshToken\":\"abc\"}")]
    [InlineData("{\"pin\":\"1234\"}")]
    [InlineData("{\"key\":\"abc\"}")]
    [InlineData("{\\\"password\\\":\\\"hunter2\\\"}")] // JSON échappé dans une chaîne JSON
    [InlineData("{'password': 'hunter2'}")]
    public void Create_redacts_serialized_json_holding_a_secret(string value) => AssertRedacted("payload", value);

    [Theory]
    [InlineData("Authorization: Bearer abcdef")]
    [InlineData("curl -H 'authorization: basic dXNlcjpwYXNz' https://example.com")]
    [InlineData("postgres://admin:hunter2@db.internal:5432/app")]
    [InlineData("Cookie: sid=abc123")]
    public void Create_redacts_logged_headers_and_urls_with_credentials(string value) => AssertRedacted("request", value);

    [Fact]
    public void Create_redacts_a_secret_value_even_when_it_exceeds_the_length_limit() =>
        AssertRedacted("note", "password=" + new string('x', AuditMetadata.MaxValueLength * 3));

    [Theory]
    [InlineData("customer request")]
    [InlineData("10.0.0.1")]
    [InlineData("a@b.co")]
    [InlineData("42")]
    [InlineData("12:30")]
    [InlineData("2026-01-15T12:00:00Z")]
    [InlineData("Basic plan")]
    [InlineData("Basic")]
    [InlineData("Bearer of bad news")]
    [InlineData("status: paid")]
    [InlineData("from: Pending to: Paid")]
    [InlineData("https://example.com/menu?id=42&sort=name&page=2")]
    [InlineData("https://example.com:8443/menu/a:b@c")]
    [InlineData("shipping=express")]
    [InlineData("monkey: 3")]
    [InlineData("Keyboard: azerty")]
    [InlineData("key account manager")]
    [InlineData("The password policy was changed")]
    [InlineData("token expired")]
    [InlineData("authorization: granted by the manager")]
    [InlineData("eyJ")]
    [InlineData("v1.2.3")]
    [InlineData("a.b.c")]
    [InlineData("{\"status\":\"paid\",\"total\":\"12.50\"}")]
    [InlineData("0b0f3a9e-6f0b-7c11-9e7a-2f6d5c1b4a90")]
    [InlineData("Table 4 — 2 pizzas, 1 tiramisu")]
    public void Harmless_values_stay_intact(string value)
    {
        Assert.False(AuditMetadata.IsSensitiveValue(value));
        Assert.Equal(value, Build(("note", value)).Values["note"]);
    }

    private static void AssertRedacted(string key, string value)
    {
        Assert.False(AuditMetadata.IsSensitiveKey(key));
        Assert.True(AuditMetadata.IsSensitiveValue(value));
        Assert.Equal(AuditMetadata.RedactedMarker, Build((key, value)).Values[key]);
    }
}
