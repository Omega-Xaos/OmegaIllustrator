using OmegaIllustrator.Web.Components;
using OmegaIllustrator.Web.Services;
using OmegaIllustrator.Core.Suggestions;
using Omega.AI.Gateway.DependencyInjection;
using Omega.AI.Gateway.Profiles;
using Omega.AI.Gateway.Security;
using Omega.AI.Gateway.Providers.ChatGptOAuth;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddDataProtection().SetApplicationName("OmegaIllustrator");
builder.Services.AddSingleton<IChatGptOAuthCredentialStore, ProtectedOAuthCredentialStore>();
builder.Services.AddSingleton<StableHostIdProvider>();
builder.Services.AddSingleton<SelectedAiModelStore>();
builder.Services.AddSingleton<SelectedAiProfileStore>();
builder.Services.AddSingleton<ProtectedAiApiProfileStore>();
builder.Services.AddScoped<SceneSuggestionService>();
builder.Services.AddSingleton<IAiProfileStore, OmegaProfileStore>();
builder.Services.AddSingleton<IAiSecretResolver>(services => services.GetRequiredService<ProtectedAiApiProfileStore>());
builder.Services.AddOmegaAiGateway(gateway => gateway
    .AddOpenAiCompatible()
    .AddAnthropicMessages()
    .AddGoogleGemini()
    .AddChatGptOAuth(new ChatGptOAuthOptions { ApplicationName = "Omega Illustrator Local" }));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
