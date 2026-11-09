using System.Text;
using Microsoft.EntityFrameworkCore;
using Toetsr.Data;
using Toetsr.Domain;

namespace Toetsr.Services.ImportExport;

/// <summary>Een voorbeeld klaar voor import, samen met de bron van de afbeeldingen.</summary>
public record PreparedImport(ImportPreview Preview, IImageSource Images);

/// <summary>Import via de frontend (plakken, JSON- of zip-upload) en het opslaan in SQLite.</summary>
public class ImportService(
    IDbContextFactory<ToetsrDbContext> dbFactory,
    QuizFileReader reader,
    ImageStore imageStore,
    AppPaths paths,
    ToetsrOptions options)
{
    /// <summary>Standaardbron: de map Toetsen/afbeeldingen en de eigen afbeeldingenopslag.</summary>
    public IImageSource DefaultImages => new FolderImageSource(paths.QuizImagesFolder, paths.ImagesFolder);

    public List<PreparedImport> PrepareFromText(string json, string sourceName = "Geplakte JSON")
    {
        var images = DefaultImages;
        return reader.Read(json, sourceName, images.Exists).Select(p => new PreparedImport(p, images)).ToList();
    }

    /// <summary>Leest een geüpload .json- of .zip-bestand.</summary>
    public async Task<List<PreparedImport>> PrepareFromFileAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        using var buffer = new MemoryStream();
        await CopyLimitedAsync(content, buffer, options.MaxImportBytes, fileName, ct);
        buffer.Position = 0;

        if (ext == ".json" || ext == ".txt")
        {
            using var sr = new StreamReader(buffer, Encoding.UTF8);
            return PrepareFromText(await sr.ReadToEndAsync(ct), fileName);
        }

        if (ext == ".zip")
        {
            ZipContents zip;
            try
            {
                zip = await ZipReader.ReadAsync(buffer, options.MaxImportBytes, DefaultImages, ct);
            }
            catch (InvalidDataException)
            {
                return [Failed(fileName, "Dit is geen geldig zip-bestand.")];
            }
            if (zip.JsonFiles.Count == 0)
                return [Failed(fileName, "Er staat geen .json-bestand in de zip.")];

            var result = new List<PreparedImport>();
            foreach (var (name, json) in zip.JsonFiles)
                foreach (var p in reader.Read(json, $"{fileName} → {name}", zip.Images.Exists))
                {
                    p.Warnings.AddRange(zip.Warnings);
                    result.Add(new PreparedImport(p, zip.Images));
                }
            return result;
        }

        return [Failed(fileName, "Kies een .json- of .zip-bestand.")];
    }

    /// <summary>Slaat een geldige toets op als nieuwe toets. Geeft het id terug.</summary>
    public async Task<int> ImportAsync(PreparedImport prepared, CancellationToken ct = default)
    {
        var quiz = await MaterializeAsync(prepared, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync(ct);
        return quiz.Id;
    }

    /// <summary>Vervangt de inhoud van een bestaande toets (pogingen blijven bewaard).</summary>
    public async Task ReplaceAsync(int quizId, PreparedImport prepared, CancellationToken ct = default)
    {
        var incoming = await MaterializeAsync(prepared, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quiz = await db.Quizzes.Include(q => q.Questions).ThenInclude(q => q.Options).FirstOrDefaultAsync(q => q.Id == quizId, ct)
                   ?? throw new InvalidOperationException("Toets niet gevonden.");

        quiz.Title = incoming.Title;
        quiz.Description = incoming.Description;
        quiz.ShuffleQuestions = incoming.ShuffleQuestions;
        quiz.ShuffleOptions = incoming.ShuffleOptions;
        quiz.UpdatedAt = DateTime.UtcNow;
        db.Questions.RemoveRange(quiz.Questions);
        quiz.Questions = incoming.Questions;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Kopieert de afbeeldingen naar de opslag en geeft een op te slaan toets terug.</summary>
    private async Task<Quiz> MaterializeAsync(PreparedImport prepared, CancellationToken ct)
    {
        var preview = prepared.Preview;
        if (!preview.IsValid)
            throw new InvalidOperationException("Deze toets bevat nog fouten en kan niet worden geïmporteerd.");

        var stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        async Task<string?> Store(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (stored.TryGetValue(name, out var s)) return s;
            await using var stream = prepared.Images.Open(name);
            return stored[name] = await imageStore.SaveAsync(stream, name, ct);
        }

        var src = preview.Quiz!;
        var quiz = new Quiz
        {
            Title = src.Title,
            Description = src.Description,
            ShuffleQuestions = src.ShuffleQuestions,
            ShuffleOptions = src.ShuffleOptions,
        };
        foreach (var q in src.Questions)
        {
            var question = new Question
            {
                Order = q.Order,
                Type = q.Type,
                Title = q.Title,
                Text = q.Text,
                Points = q.Points,
                Explanation = q.Explanation,
                Excluded = q.Excluded,
                ImagePath = await Store(q.ImagePath),
            };
            foreach (var o in q.Options)
                question.Options.Add(new AnswerOption
                {
                    GapNumber = o.GapNumber,
                    Order = o.Order,
                    Text = o.Text,
                    IsCorrect = o.IsCorrect,
                    ImagePath = await Store(o.ImagePath),
                });
            quiz.Questions.Add(question);
        }
        return quiz;
    }

    private static async Task CopyLimitedAsync(Stream source, Stream target, long max, string fileName, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > max)
                throw new InvalidOperationException($"'{fileName}' is groter dan {max / 1024 / 1024} MB.");
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private PreparedImport Failed(string source, string error)
    {
        var p = new ImportPreview { SourceName = source };
        p.Errors.Add(error);
        return new PreparedImport(p, DefaultImages);
    }
}
