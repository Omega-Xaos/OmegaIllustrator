using OmegaIllustrator.Web.Services;

namespace OmegaIllustrator.Tests;

public sealed class SelectedAiModelStoreTests
{
    [Fact]
    public async Task Selected_model_is_available_after_store_is_recreated()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OmegaIllustrator.ModelPreference.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            await new SelectedAiModelStore(directory).SaveAsync("gpt-6-luna");

            var loaded = await new SelectedAiModelStore(directory).GetAsync();

            Assert.Equal("gpt-6-luna", loaded);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\nmodel")]
    public async Task Save_rejects_invalid_model_identifiers(string modelId)
    {
        var directory = Path.Combine(Path.GetTempPath(), "OmegaIllustrator.ModelPreference.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => new SelectedAiModelStore(directory).SaveAsync(modelId));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
