using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Toetsr.Data;
using Toetsr.Domain;

namespace Toetsr.Services.ImportExport;

public enum FolderImportStatus { Imported, Updated, Unchanged, SkippedDeleted, Failed }

public record FolderImportEntry(string FileName, FolderImportStatus Status, int? QuizId, string? Title, List<string> Messages);

/// <summary>Resultaat van de laatste keer dat de map Toetsen/ is ingelezen.</summary>
public class FolderImportReport
{
    public DateTime? RanAt { get; private set; }
    public IReadOnlyList<FolderImportEntry> Entries { get; private set; } = [];

    public void Set(IReadOnlyList<FolderImportEntry> entries)
    {
        Entries = entries;
        RanAt = DateTime.Now;
    }
}

/// <summary>
/// Leest alle .json-bestanden in de map Toetsen/ in. Idempotent:
/// - nieuw bestand → nieuwe toets;
/// - bestand gewijzigd (andere hash van JSON + afbeeldingen) → vragen van de bestaande toets worden vervangen, pogingen blijven bewaard;
/// - ongewijzigd → niets;
/// - toets in de app verwijderd en bestand ongewijzigd → niet opnieuw importeren.
/// </summary>
public class FolderImportService(
    IDbContextFactory<ToetsrDbContext> dbFactory,
    QuizFileReader reader,
    ImportService importService,
    AppPaths paths,
    FolderImportReport report,
    ILogger<FolderImportService> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<IReadOnlyList<FolderImportEntry>> ImportAllAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var entries = new List<FolderImportEntry>();
            if (!Directory.Exists(paths.QuizFolder))
            {
                report.Set(entries);
                return entries;
            }

            var images = new FolderImageSource(paths.QuizImagesFolder);
            foreach (var file in Directory.EnumerateFiles(paths.QuizFolder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    entries.Add(await ImportFileAsync(file, images, ct));
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or DbUpdateException)
                {
                    logger.LogError(ex, "Importeren van {File} mislukt", file);
                    entries.Add(new(Path.GetFileName(file), FolderImportStatus.Failed, null, null, [ex.Message]));
                }
            }

            foreach (var e in entries.Where(e => e.Status == FolderImportStatus.Failed))
                logger.LogWarning("Toets {File} niet geïmporteerd: {Errors}", e.FileName, string.Join(" | ", e.Messages));

            report.Set(entries);
            return entries;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<FolderImportEntry> ImportFileAsync(string path, FolderImageSource images, CancellationToken ct)
    {
        var fileName = Path.GetFileName(path);
        var json = await File.ReadAllTextAsync(path, ct);
        var previews = reader.Read(json, fileName, images.Exists);
        if (previews.Count != 1)
            return new(fileName, FolderImportStatus.Failed, null, null, ["Een bestand in de map Toetsen/ mag precies één toets bevatten."]);

        var preview = previews[0];
        if (!preview.IsValid)
            return new(fileName, FolderImportStatus.Failed, null, preview.Quiz?.Title, preview.Errors);

        var hash = ComputeHash(json, preview.ImageNames);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var record = await db.ImportedFiles.FirstOrDefaultAsync(f => f.FileName == fileName, ct);
        var quizExists = record?.QuizId is { } qid && await db.Quizzes.AnyAsync(q => q.Id == qid, ct);
        var title = preview.Quiz!.Title;

        if (record is not null && record.Hash == hash)
            return quizExists
                ? new(fileName, FolderImportStatus.Unchanged, record.QuizId, title, preview.Warnings)
                : new(fileName, FolderImportStatus.SkippedDeleted, null, title, ["De toets is in de app verwijderd; het bestand wordt pas opnieuw geïmporteerd als het verandert."]);

        var prepared = new PreparedImport(preview, images);
        FolderImportStatus status;
        int quizId;
        if (quizExists)
        {
            quizId = record!.QuizId!.Value;
            await importService.ReplaceAsync(quizId, prepared, ct);
            status = FolderImportStatus.Updated;
        }
        else
        {
            quizId = await importService.ImportAsync(prepared, ct);
            status = FolderImportStatus.Imported;
        }

        record ??= db.ImportedFiles.Add(new ImportedFile { FileName = fileName }).Entity;
        record.Hash = hash;
        record.QuizId = quizId;
        record.ImportedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Toets {File} {Status} (id {Id})", fileName, status, quizId);
        return new(fileName, status, quizId, title, preview.Warnings);
    }

    private string ComputeHash(string json, IEnumerable<string> imageNames)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Encoding.UTF8.GetBytes(json));
        foreach (var name in imageNames.Order(StringComparer.OrdinalIgnoreCase))
        {
            var p = Path.Combine(paths.QuizImagesFolder, name);
            sha.AppendData(Encoding.UTF8.GetBytes(name));
            if (File.Exists(p)) sha.AppendData(File.ReadAllBytes(p));
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
