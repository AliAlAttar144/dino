using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Toetsr.Data;
using Toetsr.Domain;
using Toetsr.Services.QuestionTypes;

namespace Toetsr.Services.ImportExport;

public enum FolderImportStatus { Imported, Updated, Unchanged, SkippedDeleted, Failed }

public record FolderImportEntry(string FileName, FolderImportStatus Status, int? QuizId, string? Title, List<string> Messages);

/// <summary>Resultaat van de laatste keer dat de map Toetsen/ is ingelezen.</summary>
public class FolderImportReport
{
    public DateTime? RanAt { get; private set; }
    public IReadOnlyList<FolderImportEntry> Entries { get; private set; } = [];

    /// <summary>Gaat af na elke keer inlezen, zodat open pagina's zichzelf kunnen verversen.</summary>
    public event Action? Changed;

    public void Set(IReadOnlyList<FolderImportEntry> entries)
    {
        Entries = entries;
        RanAt = DateTime.Now;
        Changed?.Invoke();
    }
}

/// <summary>
/// Houdt de map Toetsen/ en de app gelijk.
///
/// Map → app (<see cref="ImportAllAsync"/>), idempotent:
/// - nieuw bestand → nieuwe toets;
/// - bestand gewijzigd (andere hash van JSON + afbeeldingen) → vragen van de bestaande toets worden vervangen, pogingen blijven bewaard;
/// - ongewijzigd → niets;
/// - toets in de app verwijderd en bestand ongewijzigd → niet opnieuw importeren.
///
/// App → map (<see cref="WriteQuizAsync"/>, <see cref="MoveToDeletedAsync"/>, <see cref="ExportMissingAsync"/>):
/// - elke toets met minstens één vraag staat als .json-bestand in de map;
/// - het bestand van een toets die in de app wordt verwijderd, gaat naar de submap verwijderd/.
/// </summary>
public class FolderImportService(
    IDbContextFactory<ToetsrDbContext> dbFactory,
    QuizFileReader reader,
    ImportService importService,
    QuestionTypeRegistry registry,
    AppPaths paths,
    FolderImportReport report,
    ILogger<FolderImportService> logger)
{
    /// <summary>Submap voor bestanden van in de app verwijderde toetsen. Wordt niet ingelezen.</summary>
    public const string DeletedFolderName = "verwijderd";

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public string DeletedFolder => Path.Combine(paths.QuizFolder, DeletedFolderName);

    /// <summary>Schrijft een toets uit de app naar zijn bestand in de map (maakt het bestand aan als het nog niet bestaat).</summary>
    public async Task WriteQuizAsync(int quizId, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await WriteQuizCoreAsync(quizId, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Toets {Id} kon niet naar de map {Folder} worden geschreven", quizId, paths.QuizFolder);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Schrijft elke toets die nog geen bestand in de map heeft (bijvoorbeeld toetsen van vóór deze functie).</summary>
    public async Task ExportMissingAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var quizIds = await db.Quizzes.Select(q => q.Id).ToListAsync(ct);
            var records = await db.ImportedFiles.Where(f => f.QuizId != null).ToListAsync(ct);
            foreach (var id in quizIds)
            {
                var record = records.FirstOrDefault(f => f.QuizId == id);
                if (record is not null && File.Exists(Path.Combine(paths.QuizFolder, record.FileName))) continue;
                try
                {
                    await WriteQuizCoreAsync(id, ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogError(ex, "Toets {Id} kon niet naar de map {Folder} worden geschreven", id, paths.QuizFolder);
                }
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Verplaatst het bestand van een toets die in de app wordt verwijderd naar de submap verwijderd/.</summary>
    public async Task MoveToDeletedAsync(int quizId, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            foreach (var record in await db.ImportedFiles.Where(f => f.QuizId == quizId).ToListAsync(ct))
            {
                var source = Path.Combine(paths.QuizFolder, record.FileName);
                if (File.Exists(source))
                {
                    Directory.CreateDirectory(DeletedFolder);
                    var target = Path.Combine(DeletedFolder, record.FileName);
                    if (File.Exists(target))
                        target = Path.Combine(DeletedFolder, $"{Path.GetFileNameWithoutExtension(record.FileName)}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    File.Move(source, target);
                    logger.LogInformation("Bestand {File} verplaatst naar {Folder}", record.FileName, DeletedFolder);
                }
                // Zet je het bestand later terug in de map, dan wordt het weer als nieuwe toets ingelezen.
                db.ImportedFiles.Remove(record);
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Bestand van toets {Id} kon niet naar {Folder} worden verplaatst", quizId, DeletedFolder);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task WriteQuizCoreAsync(int quizId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quiz = await db.Quizzes.AsNoTracking()
            .Include(q => q.Questions).ThenInclude(q => q.Options)
            .AsSplitQuery()
            .FirstOrDefaultAsync(q => q.Id == quizId, ct);
        // Een toets zonder vragen is geen geldig bestand; die komt in de map zodra er een vraag in staat.
        if (quiz is null || quiz.Questions.Count == 0) return;
        QuizService.Normalize(quiz);

        var record = await db.ImportedFiles.FirstOrDefaultAsync(f => f.QuizId == quizId, ct);
        var fileName = record?.FileName ?? await UniqueFileNameAsync(db, ExportService.Slug(quiz.Title), ct);
        var path = Path.Combine(paths.QuizFolder, fileName);

        // Afbeeldingen uit de app-opslag naar Toetsen/afbeeldingen, zodat het bestand op zichzelf werkt.
        var imageNames = quiz.Questions.Select(q => q.ImagePath)
            .Concat(quiz.Questions.SelectMany(q => q.Options).Select(o => o.ImagePath))
            .OfType<string>()
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Directory.CreateDirectory(paths.QuizImagesFolder);
        foreach (var name in imageNames)
        {
            var source = Path.Combine(paths.ImagesFolder, name);
            var target = Path.Combine(paths.QuizImagesFolder, name);
            if (!File.Exists(target) && File.Exists(source)) File.Copy(source, target);
        }

        var json = ExportService.ToJson(quiz, registry);
        var hash = ComputeHash(json, imageNames);
        if (record is not null && record.Hash == hash && File.Exists(path)) return;

        // Eerst naar een tijdelijk bestand, zodat de mapbewaker nooit een half geschreven bestand inleest.
        var temp = Path.Combine(paths.QuizFolder, $".{fileName}.tmp");
        await File.WriteAllTextAsync(temp, json, Utf8NoBom, ct);
        File.Move(temp, path, overwrite: true);

        record ??= db.ImportedFiles.Add(new ImportedFile { FileName = fileName }).Entity;
        record.Hash = hash;
        record.QuizId = quizId;
        record.ImportedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Toets {Id} opgeslagen als {File}", quizId, fileName);
    }

    private async Task<string> UniqueFileNameAsync(ToetsrDbContext db, string slug, CancellationToken ct)
    {
        var taken = new HashSet<string>(await db.ImportedFiles.Select(f => f.FileName).ToListAsync(ct), StringComparer.OrdinalIgnoreCase);
        for (var i = 1; ; i++)
        {
            var name = i == 1 ? $"{slug}.json" : $"{slug}-{i}.json";
            if (!taken.Contains(name) && !File.Exists(Path.Combine(paths.QuizFolder, name))) return name;
        }
    }

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
