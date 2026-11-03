using Microsoft.AspNetCore.DataProtection;
using System.Text.Json.Nodes;
using OmegaIllustrator.Web.Services;

namespace OmegaIllustrator.Tests;

public sealed class ProtectedAiApiProfileStoreTests
{
    [Fact]
    public async Task Profile_key_is_encrypted_and_never_returned_by_listing_or_gateway_profile()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = CreateStore(root);
            const string key = "synthetic-api-key-for-test";

            var saved = await store.SaveAsync(null, "Test provider", "https://api.example.test/v1", key, "x-api-key", "");
            var profileText = await File.ReadAllTextAsync(ProfilePath(root, saved.Id));
            var credentialText = Assert.Single(Directory.GetFiles(Path.Combine(root, "ai-api-profiles"), saved.Id + ".*.credential"));
            var listed = Assert.Single(await store.ListAsync());
            var profile = await store.GetGatewayProfileAsync(saved.Id, "model-a");

            Assert.DoesNotContain(key, profileText, StringComparison.Ordinal);
            Assert.DoesNotContain(key, await File.ReadAllTextAsync(credentialText), StringComparison.Ordinal);
            Assert.Equal("Test provider", listed.DisplayName);
            Assert.True(listed.HasApiKey);
            Assert.NotNull(profile);
            Assert.Equal("x-api-key", profile.SecretHeaderName);
            Assert.Equal("model-a", profile.ModelId);
            Assert.Equal(key, await store.ResolveAsync($"omega-ai-api-profile:{saved.Id}"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Blank_key_on_update_preserves_the_existing_secret()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = CreateStore(root);
            var saved = await store.SaveAsync(null, "Initial", "https://api.example.test/v1", "synthetic-key", "Authorization", "Bearer ");

            var updated = await store.SaveAsync(saved.Id, "Renamed", "https://api.example.test/v2", "", "Authorization", "Bearer ");

            Assert.Equal(saved.Id, updated.Id);
            Assert.Equal("Renamed", updated.DisplayName);
            Assert.Equal("synthetic-key", await store.ResolveAsync($"omega-ai-api-profile:{saved.Id}"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("anthropic-messages", "x-api-key")]
    [InlineData("google-gemini", "x-goog-api-key")]
    public async Task Native_provider_kind_and_auth_header_are_saved_with_profile(string providerKind, string expectedHeader)
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = CreateStore(root);
            var baseUrl = providerKind == "anthropic-messages"
                ? "https://api.anthropic.com"
                : "https://generativelanguage.googleapis.com";

            var saved = await store.SaveAsync(null, "Native API", providerKind, baseUrl, "synthetic-key", "unused", "unused");
            var profile = await store.GetGatewayProfileAsync(saved.Id, "native-model");
            var summary = await store.GetSummaryAsync(saved.Id);

            Assert.NotNull(profile);
            Assert.NotNull(summary);
            Assert.Equal(providerKind, profile.ProviderKind);
            Assert.Equal(providerKind, summary.ProviderKind);
            Assert.Equal(expectedHeader, profile.SecretHeaderName);
            Assert.Equal("", profile.SecretHeaderPrefix);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Profile_without_provider_kind_keeps_the_legacy_openai_compatible_default()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var keys = Path.Combine(root, "protection-keys");
            Directory.CreateDirectory(keys);
            var protection = DataProtectionProvider.Create(new DirectoryInfo(keys));
            var store = new ProtectedAiApiProfileStore(protection, root);
            var saved = await store.SaveAsync(null, "Legacy profile", "https://api.example.test/v1", "synthetic-key", "Authorization", "Bearer ");
            var protector = protection.CreateProtector("OmegaIllustrator.AiApiProfile.v1");
            var profilePath = ProfilePath(root, saved.Id);
            var profileData = JsonNode.Parse(protector.Unprotect(await File.ReadAllTextAsync(profilePath)))!.AsObject();
            profileData.Remove("ProviderKind");
            await File.WriteAllTextAsync(profilePath, protector.Protect(profileData.ToJsonString()));

            var summary = await store.GetSummaryAsync(saved.Id);
            var profile = await store.GetGatewayProfileAsync(saved.Id, "legacy-model");

            Assert.NotNull(summary);
            Assert.NotNull(profile);
            Assert.Equal("openai-compatible", summary.ProviderKind);
            Assert.Equal("openai-compatible", profile.ProviderKind);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_profile_update_keeps_the_previous_url_and_key_usable()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = CreateStore(root);
            var saved = await store.SaveAsync(null, "Existing", "https://api.example.test/v1", "synthetic-key", "Authorization", "Bearer ");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.SaveAsync(saved.Id, "Changed", "http://api.example.test/v2", "", "x-api-key", ""));

            var unchanged = await store.GetSummaryAsync(saved.Id);
            Assert.NotNull(unchanged);
            Assert.Equal("Existing", unchanged.DisplayName);
            Assert.Equal("https://api.example.test/v1/", unchanged.BaseUrl);
            Assert.Equal("synthetic-key", await store.ResolveAsync($"omega-ai-api-profile:{saved.Id}"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("http://api.example.test/v1")]
    [InlineData("https://user:password@api.example.test/v1")]
    [InlineData("https://api.example.test/v1?key=unsafe")]
    [InlineData("https://api.example.test/v1#fragment")]
    public async Task Unsafe_provider_urls_are_rejected_before_profile_is_written(string baseUrl)
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = CreateStore(root);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.SaveAsync(null, "Invalid provider", baseUrl, "synthetic-key", "Authorization", "Bearer "));

            Assert.Empty(await store.ListAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.0.0.1:11434/v1")]
    public async Task Loopback_http_is_allowed_for_local_compatible_services(string baseUrl)
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = CreateStore(root);

            var saved = await store.SaveAsync(null, "Local provider", baseUrl, "synthetic-key", "Authorization", "Bearer ");
            var profile = await store.GetGatewayProfileAsync(saved.Id, "local-model");

            Assert.NotNull(profile);
            Assert.True(profile.AllowLocalHttp);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ProtectedAiApiProfileStore CreateStore(string root)
    {
        var keys = Path.Combine(root, "protection-keys");
        Directory.CreateDirectory(keys);
        return new ProtectedAiApiProfileStore(DataProtectionProvider.Create(new DirectoryInfo(keys)), root);
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "OmegaIllustrator.ApiProfiles.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ProfilePath(string root, string profileId) =>
        Path.Combine(root, "ai-api-profiles", profileId + ".profile");
}
