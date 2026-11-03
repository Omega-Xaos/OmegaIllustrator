using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Omega.AI.Gateway.Contracts;
using Omega.AI.Gateway.Profiles;
using Omega.AI.Gateway.Security;
using Omega.AI.Gateway.Providers.ChatGptOAuth;

namespace OmegaIllustrator.Web.Services;

public sealed class StableHostIdProvider
{
    private static readonly object Gate = new();
    private readonly string _path;
    private string? _value;

    public StableHostIdProvider()
    {
        var dataRoot = Environment.GetEnvironmentVariable("OMEGA_DATA_DIR");
        var root = string.IsNullOrWhiteSpace(dataRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmegaIllustrator")
            : Path.GetFullPath(dataRoot);
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "host-id.txt");
    }

    public string Get()
    {
        if (_value is not null) return _value;
        lock (Gate)
        {
            if (_value is not null) return _value;
            if (File.Exists(_path))
            {
                var saved = File.ReadAllText(_path).Trim();
                if (Guid.TryParse(saved.StartsWith("urn:uuid:", StringComparison.Ordinal) ? saved[9..] : saved, out var parsed))
                    return _value = $"urn:uuid:{parsed:D}";
            }

            var created = $"urn:uuid:{Guid.NewGuid():D}";
            var temp = _path + ".tmp";
            File.WriteAllText(temp, created);
            File.Move(temp, _path, true);
            return _value = created;
        }
    }
}

public sealed class OmegaProfileStore(
    IChatGptOAuthCredentialStore credentials,
    SelectedAiModelStore selectedModels,
    ProtectedAiApiProfileStore apiProfiles) : IAiProfileStore
{
    public async Task<AiProfile?> GetAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var modelId = await selectedModels.GetAsync(cancellationToken) ?? "gpt-6-luna";
        if (profileId == "chatgpt")
        {
            var credential = await credentials.GetAsync(profileId, cancellationToken);
            return new AiProfile("chatgpt", "ChatGPT plan OAuth", "chatgpt-oauth", new Uri("https://api.openai.com/v1/"), modelId, AiAuthMode.ChatGptOAuth, Timeout: TimeSpan.FromSeconds(45), IsEnabled: credential is not null);
        }

        return await apiProfiles.GetGatewayProfileAsync(profileId, modelId, cancellationToken);
    }
}

public sealed class ProtectedOAuthCredentialStore : IChatGptOAuthCredentialStore
{
    private readonly string _directory;
    private readonly IDataProtector _protector;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public ProtectedOAuthCredentialStore(IDataProtectionProvider protection, IWebHostEnvironment environment)
    {
        var root = Environment.GetEnvironmentVariable("OMEGA_DATA_DIR");
        _directory = Path.GetFullPath(string.IsNullOrWhiteSpace(root)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmegaIllustrator", "oauth")
            : Path.Combine(root, "oauth"));
        Directory.CreateDirectory(_directory);
        _protector = protection.CreateProtector("OmegaIllustrator.ChatGptOAuthCredential.v1");
    }

    public async Task<ChatGptOAuthCredential?> GetAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var path = FilePath(profileId);
        if (!File.Exists(path)) return null;
        try
        {
            var protectedText = await File.ReadAllTextAsync(path, cancellationToken);
            var json = _protector.Unprotect(protectedText);
            var stored = JsonSerializer.Deserialize<StoredOAuthCredential>(json);
            return stored?.ToCredential();
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    public async Task SaveAsync(string profileId, ChatGptOAuthCredential credential, CancellationToken cancellationToken = default)
    {
        var path = FilePath(profileId); var temp = path + ".tmp";
        var protectedText = _protector.Protect(JsonSerializer.Serialize(StoredOAuthCredential.From(credential)));
        await File.WriteAllTextAsync(temp, protectedText, cancellationToken);
        File.Move(temp, path, true);
    }

    public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var path = FilePath(profileId); if (File.Exists(path)) File.Delete(path); return Task.CompletedTask;
    }

    public async ValueTask<IAsyncDisposable> AcquireRefreshLockAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(profileId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken); return new LockHandle(gate);
    }

    private string FilePath(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId) || profileId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Invalid profile ID.", nameof(profileId));
        return Path.Combine(_directory, profileId + ".protected");
    }

    private sealed record StoredOAuthCredential(
        string ClientId, string HostId, string Subject, string? Email,
        string AccessToken, string RefreshToken, string IdToken,
        DateTimeOffset ExpiresAtUtc, string[] Scopes, string? PendingIdToken)
    {
        public static StoredOAuthCredential From(ChatGptOAuthCredential credential) => new(
            credential.ClientId, credential.HostId, credential.Subject, credential.Email,
            credential.AccessToken, credential.RefreshToken, credential.IdToken,
            credential.ExpiresAtUtc, credential.Scopes.ToArray(), credential.PendingIdToken);

        public ChatGptOAuthCredential ToCredential() => new(
            ClientId, HostId, Subject, Email, AccessToken, RefreshToken, IdToken,
            ExpiresAtUtc, new HashSet<string>(Scopes, StringComparer.Ordinal), PendingIdToken);
    }

    private sealed class LockHandle(SemaphoreSlim gate) : IAsyncDisposable { public ValueTask DisposeAsync() { gate.Release(); return ValueTask.CompletedTask; } }
}
