using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Toetsr.Domain;
using Toetsr.Services.QuestionTypes;

namespace Toetsr.Services.ImportExport;

/// <summary>Exporteert een toets naar het JSON-formaat, of naar een zip met de afbeeldingen erbij.</summary>
public class ExportService(QuizService quizzes, QuestionTypeRegistry registry, ImageStore imageStore)
{
    public static QuizFileDto ToDto(Quiz quiz, QuestionTypeRegistry registry) => new()
    {
        Title = quiz.Title,
        Description = quiz.Description,
        ShuffleQuestions = quiz.ShuffleQuestions ? true : null,
        ShuffleOptions = quiz.ShuffleOptions ? true : null,
        Questions = quiz.Questions.OrderBy(q => q.Order).Select(q => new QuestionDto
        {
            Type = registry.Get(q.Type).JsonName,
            Title = q.Title,
            Text = q.Text,
            Image = q.ImagePath,
            Points = q.Points,
            Explanation = q.Explanation,
            Options = q.Type == QuestionType.Dropdown ? null : q.Options.OrderBy(o => o.Order).Select(ToOption).ToList(),
            Gaps = q.Type != QuestionType.Dropdown ? null : DropdownText.GapNumbers(q.Text).Select(gap => new GapDto
            {
                Options = q.Options.Where(o => o.GapNumber == gap).OrderBy(o => o.Order).Select(ToOption).ToList(),
            }).ToList(),
        }).ToList(),
    };

    private static OptionDto ToOption(AnswerOption o) => new()
    {
        Text = o.Text,
        Correct = o.IsCorrect ? true : null,
        Image = o.ImagePath,
    };

    public static string ToJson(Quiz quiz, QuestionTypeRegistry registry) =>
        JsonSerializer.Serialize(ToDto(quiz, registry), QuizFileJson.WriteOptions);

    public async Task<(string FileName, byte[] Content)?> ExportJsonAsync(int quizId, CancellationToken ct = default)
    {
        var quiz = await quizzes.GetAsync(quizId, ct);
        if (quiz is null) return null;
        return ($"{Slug(quiz.Title)}.json", Encoding.UTF8.GetBytes(ToJson(quiz, registry)));
    }

    public async Task<(string FileName, byte[] Content)?> ExportZipAsync(int quizId, CancellationToken ct = default)
    {
        var quiz = await quizzes.GetAsync(quizId, ct);
        if (quiz is null) return null;
        var slug = Slug(quiz.Title);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry($"{slug}.json", CompressionLevel.Optimal);
            await using (var s = entry.Open())
                await s.WriteAsync(Encoding.UTF8.GetBytes(ToJson(quiz, registry)), ct);

            var images = quiz.Questions.Select(q => q.ImagePath)
                .Concat(quiz.Questions.SelectMany(q => q.Options).Select(o => o.ImagePath))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var name in images)
            {
                var path = imageStore.GetFullPath(name);
                if (path is not null)
                    zip.CreateEntryFromFile(path, $"afbeeldingen/{name}", CompressionLevel.NoCompression);
            }
        }
        return ($"{slug}.zip", ms.ToArray());
    }

    /// <summary>Veilige bestandsnaam op basis van de titel.</summary>
    public static string Slug(string title)
    {
        var sb = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if ((c == ' ' || c == '-' || c == '_' || c == '.') && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 60) slug = slug[..60].Trim('-');
        return slug.Length == 0 ? "toets" : slug;
    }
}
