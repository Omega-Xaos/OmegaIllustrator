namespace OmegaIllustrator.Web.Services;

public sealed class SelectedAiProfileStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SelectedAiProfileStore(string? dataDirectory = null)
    {
        var dataRoot = Environment.GetEnvironmentVariable("OMEGA_DATA_DIR");
        var root = !string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.GetFullPath(dataDirectory)
            : !string.IsNullOrWhiteSpace(dataRoot)
                ? Path.GetFullPath(dataRoot)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmegaIllustrator");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "selected-ai-profile.txt");
    }

    public async Task<string> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return "chatgpt";
            var value = (await File.ReadAllTextAsync(_path, cancellationToken)).Trim();
            return IsValid(value) ? Normalize(value) : "chatgpt";
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (!IsValid(profileId)) throw new ArgumentException("AI profile ID is invalid.", nameof(profileId));
        var normalized = Normalize(profileId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var temporaryPath = _path + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, normalized, cancellationToken);
            File.Move(temporaryPath, _path, true);
        }
        finally { _gate.Release(); }
    }

    private static bool IsValid(string? profileId) =>
        string.Equals(profileId, "chatgpt", StringComparison.OrdinalIgnoreCase) ||
        profileId is not null && profileId.StartsWith("api-", StringComparison.OrdinalIgnoreCase) &&
        Guid.TryParseExact(profileId[4..], "N", out _);

    private static string Normalize(string profileId) =>
        string.Equals(profileId, "chatgpt", StringComparison.OrdinalIgnoreCase)
            ? "chatgpt"
            : $"api-{Guid.ParseExact(profileId[4..], "N"):N}";
}
