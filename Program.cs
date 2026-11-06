using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Toetsr.Components;
using Toetsr.Data;
using Toetsr.Services;
using Toetsr.Services.ImportExport;
using Toetsr.Services.QuestionTypes;

var builder = WebApplication.CreateBuilder(args);

// Zorgt dat CSS/JS ook werken bij `dotnet run` buiten de Development-omgeving.
builder.WebHost.UseStaticWebAssets();

// Instellingen en mappen (alles relatief aan de app-map).
var options = builder.Configuration.GetSection("Toetsr").Get<ToetsrOptions>() ?? new ToetsrOptions();
var paths = new AppPaths(builder.Environment.ContentRootPath, options);
paths.EnsureFolders();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(paths);

// Database
builder.Services.AddDbContextFactory<ToetsrDbContext>(o => o.UseSqlite($"Data Source={paths.DatabasePath}"));

// Vraagtypen (strategy-patroon). Nieuw type? Registreer hier de handler.
builder.Services.AddSingleton<IQuestionTypeHandler, SingleChoiceHandler>();
builder.Services.AddSingleton<IQuestionTypeHandler, MultipleChoiceHandler>();
builder.Services.AddSingleton<IQuestionTypeHandler, DropdownHandler>();
builder.Services.AddSingleton<QuestionTypeRegistry>();
builder.Services.AddSingleton<QuestionValidator>();

// Toetslogica, import en export
builder.Services.AddSingleton<ImageStore>();
builder.Services.AddSingleton<QuizService>();
builder.Services.AddSingleton<AttemptService>();
builder.Services.AddSingleton<QuizFileReader>();
builder.Services.AddSingleton<ImportService>();
builder.Services.AddSingleton<ExportService>();
builder.Services.AddSingleton<FolderImportReport>();
builder.Services.AddSingleton<FolderImportService>();
builder.Services.AddScoped<ToastService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 8 * 1024 * 1024); // grote geplakte JSON

var app = builder.Build();

// Nederlandse datum- en getalnotatie.
var dutch = new CultureInfo("nl-NL");
CultureInfo.DefaultThreadCurrentCulture = dutch;
CultureInfo.DefaultThreadCurrentUICulture = dutch;
app.UseRequestLocalization(o =>
{
    o.SetDefaultCulture("nl-NL");
    o.AddSupportedCultures("nl-NL");
    o.AddSupportedUICultures("nl-NL");
});

// Database aanmaken/bijwerken, map Toetsen/ inlezen en ongebruikte afbeeldingen opruimen.
await using (var db = await app.Services.GetRequiredService<IDbContextFactory<ToetsrDbContext>>().CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
}
if (options.ImportQuizFolderOnStartup)
    await app.Services.GetRequiredService<FolderImportService>().ImportAllAsync();
await app.Services.GetRequiredService<ImageStore>().CleanupOrphansAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Afbeeldingen uit App_Data/afbeeldingen.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(paths.ImagesFolder),
    RequestPath = ImageStore.UrlPrefix,
});

app.UseAntiforgery();

// Downloads voor export.
app.MapGet("/export/{id:int}/json", async (int id, ExportService export, CancellationToken ct) =>
    await export.ExportJsonAsync(id, ct) is { } file
        ? Results.File(file.Content, "application/json; charset=utf-8", file.FileName)
        : Results.NotFound());
app.MapGet("/export/{id:int}/zip", async (int id, ExportService export, CancellationToken ct) =>
    await export.ExportZipAsync(id, ct) is { } file
        ? Results.File(file.Content, "application/zip", file.FileName)
        : Results.NotFound());

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
