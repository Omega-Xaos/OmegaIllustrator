namespace OmegaIllustrator.Web.Services;

public sealed class SelectedAiModelStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SelectedAiModelStore(string? dataDirectory = null)
    {
        var dataRoot = Environment.GetEnvironmentVariable("OMEGA_DATA_DIR");
        var root = !string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.GetFullPath(dataDirectory)
            : !string.IsNullOrWhiteSpace(dataRoot)
                ? Path.GetFullPath(dataRoot)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmegaIllustrator");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "selected-ai-model.txt");
    }

    public async Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return null;
            var value = (await File.ReadAllTextAsync(_path, cancellationToken)).Trim();
            return value.Length is > 0 and <= 200 ? value : null;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Length > 200 || modelId.Any(char.IsControl))
            throw new ArgumentException("Model ID must be a non-empty provider model identifier.", nameof(modelId));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var temporaryPath = _path + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, modelId, cancellationToken);
            File.Move(temporaryPath, _path, true);
        }
        finally { _gate.Release(); }
    }
}
