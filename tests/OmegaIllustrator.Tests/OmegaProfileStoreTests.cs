using Microsoft.AspNetCore.DataProtection;
using Omega.AI.Gateway.Contracts;
using Omega.AI.Gateway.Providers.ChatGptOAuth;
using OmegaIllustrator.Web.Services;

namespace OmegaIllustrator.Tests;

public sealed class OmegaProfileStoreTests
{
    [Fact]
    public async Task Saved_api_profile_is_routed_to_compatible_gateway_with_selected_model()
    {
        var root = Path.Combine(Path.GetTempPath(), "OmegaIllustrator.GatewayProfiles.Tests", Guid.NewGuid().ToString("N"));
        var keys = Path.Combine(root, "keys");
        Directory.CreateDirectory(keys);
        try
        {
            var apiProfiles = new ProtectedAiApiProfileStore(DataProtectionProvider.Create(new DirectoryInfo(keys)), root);
            var saved = await apiProfiles.SaveAsync(null, "Compatible service", "https://api.example.test/v1", "synthetic-key", "Authorization", "Bearer ");
            var selectedModels = new SelectedAiModelStore(root);
            await selectedModels.SaveAsync("model-a");
            var profiles = new OmegaProfileStore(new EmptyOAuthCredentialStore(), selectedModels, apiProfiles);

            var profile = await profiles.GetAsync(saved.Id);

            Assert.NotNull(profile);
            Assert.Equal("openai-compatible", profile.ProviderKind);
            Assert.Equal(AiAuthMode.SecretReference, profile.AuthMode);
            Assert.Equal("https://api.example.test/v1/", profile.BaseUri.AbsoluteUri);
            Assert.Equal("model-a", profile.ModelId);
            Assert.Equal("synthetic-key", await apiProfiles.ResolveAsync(profile.SecretReference!));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class EmptyOAuthCredentialStore : IChatGptOAuthCredentialStore
    {
        public Task<ChatGptOAuthCredential?> GetAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ChatGptOAuthCredential?>(null);

        public Task SaveAsync(string profileId, ChatGptOAuthCredential credential, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask<IAsyncDisposable> AcquireRefreshLockAsync(string profileId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IAsyncDisposable>(new NoopAsyncDisposable());
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
