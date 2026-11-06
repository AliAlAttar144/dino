namespace Toetsr.Services.ImportExport;

/// <summary>
/// Houdt de map Toetsen/ in de gaten terwijl de app draait. Verandert er een .json-bestand of een afbeelding,
/// dan wordt de map na een korte pauze opnieuw ingelezen. Zo hoef je niet op "Map nu opnieuw inlezen" te klikken.
/// </summary>
public sealed class QuizFolderWatcher(AppPaths paths, FolderImportService folderImport, ILogger<QuizFolderWatcher> logger)
    : IHostedService, IDisposable
{
    /// <summary>Wacht tot er even niets verandert, zodat een editor of script klaar is met schrijven.</summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);

    private FileSystemWatcher? _watcher;
    private Timer? _timer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.QuizFolder);
        _timer = new Timer(_ => _ = RescanAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(paths.QuizFolder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += OnChange;
        _watcher.Changed += OnChange;
        _watcher.Deleted += OnChange;
        _watcher.Renamed += OnChange;
        _watcher.Error += (_, e) => logger.LogWarning(e.GetException(), "Map {Folder} kan niet goed worden gevolgd", paths.QuizFolder);
        _watcher.EnableRaisingEvents = true;
        return Task.CompletedTask;
    }

    private void OnChange(object sender, FileSystemEventArgs e)
    {
        if (!IsRelevant(e.FullPath) && !(e is RenamedEventArgs r && IsRelevant(r.OldFullPath))) return;
        _timer?.Change(Delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Alleen .json-bestanden direct in de map, en afbeeldingen in afbeeldingen/.</summary>
    private bool IsRelevant(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        if (name.StartsWith('.')) return false; // tijdelijke bestanden
        var dir = Path.GetDirectoryName(fullPath);
        if (PathEquals(dir, paths.QuizFolder))
            return Path.GetExtension(name).Equals(".json", StringComparison.OrdinalIgnoreCase);
        return PathEquals(dir, paths.QuizImagesFolder) && ImageStore.IsAllowedImage(name);
    }

    private static bool PathEquals(string? a, string b) =>
        a is not null && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private async Task RescanAsync()
    {
        try
        {
            var entries = await folderImport.ImportAllAsync();
            var changed = entries.Count(e => e.Status is FolderImportStatus.Imported or FolderImportStatus.Updated);
            if (changed > 0) logger.LogInformation("Map {Folder}: {Count} toets(en) automatisch ingelezen", paths.QuizFolder, changed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Automatisch inlezen van {Folder} mislukt", paths.QuizFolder);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_watcher is not null) _watcher.EnableRaisingEvents = false;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _timer?.Dispose();
    }
}
