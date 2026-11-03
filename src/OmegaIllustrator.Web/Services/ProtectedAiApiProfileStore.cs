using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Omega.AI.Gateway.Contracts;
using Omega.AI.Gateway.Security;

namespace OmegaIllustrator.Web.Services;

public sealed record AiApiProfileSummary(
    string Id,
    string DisplayName,
    string BaseUrl,
    string SecretHeaderName,
    string SecretHeaderPrefix,
    bool HasApiKey,
    string ProviderKind = "openai-compatible");

public sealed class ProtectedAiApiProfileStore : IAiSecretResolver
{
    private const string SecretReferencePrefix = "omega-ai-api-profile:";
    private const int MaximumApiKeyLength = 8_192;
    private readonly string _directory;
    private readonly IDataProtector _profileProtector;
    private readonly IDataProtector _credentialProtector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProtectedAiApiProfileStore(IDataProtectionProvider protection, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(protection);
        var configuredRoot = Environment.GetEnvironmentVariable("OMEGA_DATA_DIR");
        var root = !string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.GetFullPath(dataDirectory)
            : !string.IsNullOrWhiteSpace(configuredRoot)
                ? Path.GetFullPath(configuredRoot)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmegaIllustrator");
        _directory = Path.Combine(root, "ai-api-profiles");
        Directory.CreateDirectory(_directory);
        _profileProtector = protection.CreateProtector("OmegaIllustrator.AiApiProfile.v1");
        _credentialProtector = protection.CreateProtector("OmegaIllustrator.AiApiCredential.v1");
    }

    public async Task<IReadOnlyList<AiApiProfileSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profiles = new List<AiApiProfileSummary>();
            foreach (var path in Directory.EnumerateFiles(_directory, "api-*.profile"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var profileId = Path.GetFileNameWithoutExtension(path);
                if (!TryNormalizeProfileId(profileId, out var normalizedId)) continue;
                var stored = await ReadProfileUnsafeAsync(normalizedId, cancellationToken);
                if (stored is null) continue;
                var credential = await ReadCredentialUnsafeAsync(normalizedId, stored.CredentialVersion, cancellationToken);
                profiles.Add(stored.ToSummary(!string.IsNullOrWhiteSpace(credential?.ApiKey)));
            }

            return profiles.OrderBy(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<AiApiProfileSummary?> GetSummaryAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeProfileId(profileId, out var normalizedId)) return null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = await ReadProfileUnsafeAsync(normalizedId, cancellationToken);
            if (stored is null) return null;
            var credential = await ReadCredentialUnsafeAsync(normalizedId, stored.CredentialVersion, cancellationToken);
            return stored.ToSummary(!string.IsNullOrWhiteSpace(credential?.ApiKey));
        }
        finally { _gate.Release(); }
    }

    public async Task<AiProfile?> GetGatewayProfileAsync(
        string profileId,
        string? modelId,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeProfileId(profileId, out var normalizedId)) return null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = await ReadProfileUnsafeAsync(normalizedId, cancellationToken);
            if (stored is null) return null;

            var (baseUri, allowLocalHttp) = ValidateBaseUrl(stored.BaseUrl);
            var providerKind = stored.EffectiveProviderKind;
            if (providerKind == "openai-compatible")
                ValidateSecretHeader(stored.SecretHeaderName, stored.SecretHeaderPrefix);
            var credential = await ReadCredentialUnsafeAsync(normalizedId, stored.CredentialVersion, cancellationToken);
            if (credential is null) return null;
            ValidateApiKey(credential.ApiKey);
            return new AiProfile(
                stored.Id,
                stored.DisplayName,
                providerKind,
                baseUri,
                string.IsNullOrWhiteSpace(modelId) ? "default" : modelId,
                AiAuthMode.SecretReference,
                SecretReferencePrefix + stored.Id,
                Timeout: TimeSpan.FromSeconds(45),
                IsEnabled: true,
                AllowLocalHttp: allowLocalHttp,
                SecretHeaderName: stored.SecretHeaderName,
                SecretHeaderPrefix: stored.SecretHeaderPrefix);
        }
        finally { _gate.Release(); }
    }

    public async Task<AiApiProfileSummary> SaveAsync(
        string? profileId,
        string displayName,
        string providerKind,
        string baseUrl,
        string? apiKey,
        string secretHeaderName,
        string? secretHeaderPrefix,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = ValidateDisplayName(displayName);
        var normalizedProviderKind = ValidateProviderKind(providerKind);
        var (normalizedBaseUri, _) = ValidateBaseUrl(baseUrl);
        var normalizedHeaderName = normalizedProviderKind switch
        {
            "anthropic-messages" => "x-api-key",
            "google-gemini" => "x-goog-api-key",
            _ => ValidateSecretHeaderName(secretHeaderName)
        };
        var normalizedHeaderPrefix = normalizedProviderKind == "openai-compatible"
            ? ValidateSecretHeaderPrefix(secretHeaderPrefix)
            : string.Empty;
        if (normalizedProviderKind == "openai-compatible")
            ValidateSecretHeader(normalizedHeaderName, normalizedHeaderPrefix);
        var normalizedId = string.IsNullOrWhiteSpace(profileId)
            ? $"api-{Guid.NewGuid():N}"
            : NormalizeProfileId(profileId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await ReadProfileUnsafeAsync(normalizedId, cancellationToken);
            var currentCredential = existing is null
                ? null
                : await ReadCredentialUnsafeAsync(normalizedId, existing.CredentialVersion, cancellationToken);
            var normalizedApiKey = string.IsNullOrWhiteSpace(apiKey)
                ? currentCredential?.ApiKey ?? throw new ArgumentException("An API key is required for a new profile.", nameof(apiKey))
                : ValidateApiKey(apiKey.Trim());
            var credentialVersion = Guid.NewGuid().ToString("N");
            var stored = new StoredAiApiProfile(
                normalizedId,
                normalizedName,
                normalizedBaseUri.AbsoluteUri,
                credentialVersion,
                normalizedHeaderName,
                normalizedHeaderPrefix,
                normalizedProviderKind);

            // The profile metadata is the commit point; the previous key stays resolvable until it is replaced.
            var credentialPath = CredentialPath(normalizedId, credentialVersion);
            await WriteProtectedAtomicallyAsync(
                credentialPath,
                _credentialProtector.Protect(JsonSerializer.Serialize(new StoredAiApiCredential(normalizedApiKey))),
                cancellationToken);
            try
            {
                await WriteProtectedAtomicallyAsync(
                    ProfilePath(normalizedId),
                    _profileProtector.Protect(JsonSerializer.Serialize(stored)),
                    cancellationToken);
            }
            catch
            {
                if (File.Exists(credentialPath)) File.Delete(credentialPath);
                throw;
            }

            if (existing is not null && existing.CredentialVersion != credentialVersion)
            {
                var previousCredentialPath = CredentialPath(normalizedId, existing.CredentialVersion);
                if (File.Exists(previousCredentialPath)) File.Delete(previousCredentialPath);
            }

            return stored.ToSummary(true);
        }
        finally { _gate.Release(); }
    }

    public Task<AiApiProfileSummary> SaveAsync(
        string? profileId,
        string displayName,
        string baseUrl,
        string? apiKey,
        string secretHeaderName,
        string? secretHeaderPrefix,
        CancellationToken cancellationToken = default) =>
        SaveAsync(profileId, displayName, "openai-compatible", baseUrl, apiKey, secretHeaderName, secretHeaderPrefix, cancellationToken);

    public async Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var normalizedId = NormalizeProfileId(profileId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = await ReadProfileUnsafeAsync(normalizedId, cancellationToken);
            var path = ProfilePath(normalizedId);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path)) File.Delete(path);
            if (stored is not null)
            {
                var credentialPath = CredentialPath(normalizedId, stored.CredentialVersion);
                if (File.Exists(credentialPath)) File.Delete(credentialPath);
            }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<string?> ResolveAsync(string secretReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretReference) ||
            !secretReference.StartsWith(SecretReferencePrefix, StringComparison.Ordinal) ||
            !TryNormalizeProfileId(secretReference[SecretReferencePrefix.Length..], out var profileId))
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var stored = await ReadProfileUnsafeAsync(profileId, cancellationToken);
            if (stored is null) return null;
            ValidateBaseUrl(stored.BaseUrl);
            ValidateSecretHeader(stored.SecretHeaderName, stored.SecretHeaderPrefix);
            var credential = await ReadCredentialUnsafeAsync(profileId, stored.CredentialVersion, cancellationToken);
            return credential is null ? null : ValidateApiKey(credential.ApiKey);
        }
        finally { _gate.Release(); }
    }

    private async Task<StoredAiApiProfile?> ReadProfileUnsafeAsync(string profileId, CancellationToken cancellationToken)
    {
        var path = ProfilePath(profileId);
        if (!File.Exists(path)) return null;
        try
        {
            var protectedText = await File.ReadAllTextAsync(path, cancellationToken);
            var json = _profileProtector.Unprotect(protectedText);
            var stored = JsonSerializer.Deserialize<StoredAiApiProfile>(json);
            return stored is not null && string.Equals(stored.Id, profileId, StringComparison.Ordinal) &&
                   Guid.TryParseExact(stored.CredentialVersion, "N", out _)
                ? stored
                : null;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    private async Task<StoredAiApiCredential?> ReadCredentialUnsafeAsync(
        string profileId,
        string credentialVersion,
        CancellationToken cancellationToken)
    {
        var path = CredentialPath(profileId, credentialVersion);
        if (!File.Exists(path)) return null;
        try
        {
            var protectedText = await File.ReadAllTextAsync(path, cancellationToken);
            var json = _credentialProtector.Unprotect(protectedText);
            return JsonSerializer.Deserialize<StoredAiApiCredential>(json);
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    private async Task WriteProtectedAtomicallyAsync(string path, string protectedText, CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, protectedText, cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private string ProfilePath(string profileId) => Path.Combine(_directory, NormalizeProfileId(profileId) + ".profile");

    private string CredentialPath(string profileId, string credentialVersion)
    {
        var normalizedId = NormalizeProfileId(profileId);
        if (!Guid.TryParseExact(credentialVersion, "N", out var version))
            throw new ArgumentException("Credential version is invalid.", nameof(credentialVersion));
        return Path.Combine(_directory, $"{normalizedId}.{version:N}.credential");
    }

    private static string ValidateDisplayName(string displayName)
    {
        var value = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || value.Any(char.IsControl))
            throw new ArgumentException("Profile name must contain 1–80 printable characters.", nameof(displayName));
        return value;
    }

    private static string ValidateProviderKind(string providerKind) => providerKind switch
    {
        "openai-compatible" or "anthropic-messages" or "google-gemini" => providerKind,
        _ => throw new ArgumentException("Select a supported API protocol.", nameof(providerKind))
    };

    private static (Uri BaseUri, bool AllowLocalHttp) ValidateBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            throw new ArgumentException("Provider URL must be an absolute base URL without credentials, query, or fragment.", nameof(baseUrl));
        }

        var localHttp = uri.Scheme == Uri.UriSchemeHttp &&
                        (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
        if (uri.Scheme != Uri.UriSchemeHttps && !localHttp)
            throw new ArgumentException("Provider URL must use HTTPS; HTTP is allowed only for localhost or loopback services.", nameof(baseUrl));

        return (new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute), localHttp);
    }

    private static void ValidateSecretHeader(string name, string prefix)
    {
        ValidateSecretHeaderName(name);
        ValidateSecretHeaderPrefix(prefix);
    }

    private static string ValidateSecretHeaderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 ||
            name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') ||
            name.ToLowerInvariant() is "host" or "content-length" or "transfer-encoding" or "connection" or "cookie" or "proxy-authorization" or "upgrade")
        {
            throw new ArgumentException("API-key header name is invalid or reserved.", nameof(name));
        }

        return name;
    }

    private static string ValidateSecretHeaderPrefix(string? prefix)
    {
        var value = prefix ?? string.Empty;
        if (value.Length > 64 || value.Any(char.IsControl))
            throw new ArgumentException("API-key header prefix is invalid.", nameof(prefix));
        return value;
    }

    private static string ValidateApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > MaximumApiKeyLength || apiKey.Any(char.IsControl))
            throw new ArgumentException("API key must contain 1–8192 characters without control characters.", nameof(apiKey));
        return apiKey;
    }

    private static string NormalizeProfileId(string profileId)
    {
        if (!TryNormalizeProfileId(profileId, out var normalized))
            throw new ArgumentException("API profile ID is invalid.", nameof(profileId));
        return normalized;
    }

    private static bool TryNormalizeProfileId(string? profileId, out string normalized)
    {
        normalized = string.Empty;
        if (profileId is null || !profileId.StartsWith("api-", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(profileId[4..], "N", out var id))
        {
            return false;
        }

        normalized = $"api-{id:N}";
        return true;
    }

    private sealed record StoredAiApiProfile(
        string Id,
        string DisplayName,
        string BaseUrl,
        string CredentialVersion,
        string SecretHeaderName,
        string SecretHeaderPrefix,
        string? ProviderKind = null)
    {
        public string EffectiveProviderKind => ProviderKind is "anthropic-messages" or "google-gemini"
            ? ProviderKind
            : "openai-compatible";

        public AiApiProfileSummary ToSummary(bool hasApiKey) =>
            new(Id, DisplayName, BaseUrl, SecretHeaderName, SecretHeaderPrefix, hasApiKey, EffectiveProviderKind);
    }

    private sealed record StoredAiApiCredential(string ApiKey);
}
