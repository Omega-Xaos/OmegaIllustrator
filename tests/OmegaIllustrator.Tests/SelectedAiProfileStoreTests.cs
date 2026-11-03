using OmegaIllustrator.Web.Services;

namespace OmegaIllustrator.Tests;

public sealed class SelectedAiProfileStoreTests
{
    [Fact]
    public async Task Selected_profile_is_persisted_and_defaults_to_chatgpt()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new SelectedAiProfileStore(directory);

            Assert.Equal("chatgpt", await store.GetAsync());
            await store.SaveAsync("api-0123456789abcdef0123456789abcdef");

            Assert.Equal("api-0123456789abcdef0123456789abcdef", await new SelectedAiProfileStore(directory).GetAsync());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("../profile")]
    [InlineData("api-not-a-guid")]
    public async Task Invalid_profile_ids_are_rejected(string profileId)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => new SelectedAiProfileStore(directory).SaveAsync(profileId));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "OmegaIllustrator.SelectedAiProfile.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
