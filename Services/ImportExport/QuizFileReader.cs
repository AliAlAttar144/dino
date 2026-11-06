using System.Text.Json;
using Toetsr.Domain;
using Toetsr.Services.QuestionTypes;

namespace Toetsr.Services.ImportExport;

/// <summary>Resultaat van het inlezen van één toets: het domeinmodel (nog niet opgeslagen) plus meldingen.</summary>
public class ImportPreview
{
    public string SourceName { get; init; } = "";
    public Quiz? Quiz { get; set; }
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool IsValid => Quiz is not null && Errors.Count == 0;

    /// <summary>Afbeeldingsbestanden waarnaar verwezen wordt (en die gevonden zijn).</summary>
    public HashSet<string> ImageNames { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Leest en valideert het JSON-formaat. Bevat geen database- of bestandslogica, zodat het goed te testen is.</summary>
public class QuizFileReader(QuestionTypeRegistry registry, QuestionValidator validator)
{
    /// <summary>Leest één toets (object) of meerdere toetsen (array) uit JSON-tekst.</summary>
    /// <param name="imageExists">Geeft aan of een afbeelding met deze bestandsnaam beschikbaar is.</param>
    public List<ImportPreview> Read(string? json, string sourceName, Func<string, bool> imageExists)
    {
        var cleaned = Clean(json, out var cleanWarning);
        if (string.IsNullOrWhiteSpace(cleaned))
            return [Failed(sourceName, "Er is geen JSON ingevuld.")];

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(cleaned, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            return [Failed(sourceName, DescribeJsonError(ex))];
        }

        using (doc)
        {
            var root = doc.RootElement;
            var elements = root.ValueKind switch
            {
                JsonValueKind.Object => [root],
                JsonValueKind.Array => root.EnumerateArray().ToList(),
                _ => [],
            };
            if (elements.Count == 0)
                return [Failed(sourceName, "De JSON moet een toets-object { ... } zijn (of een lijst [ ... ] met toetsen).")];

            var results = new List<ImportPreview>();
            for (var i = 0; i < elements.Count; i++)
            {
                var name = elements.Count == 1 ? sourceName : $"{sourceName} (toets {i + 1})";
                var preview = ReadQuiz(elements[i], name, imageExists);
                if (cleanWarning is not null) preview.Warnings.Insert(0, cleanWarning);
                results.Add(preview);
            }
            return results;
        }
    }

    private ImportPreview ReadQuiz(JsonElement element, string sourceName, Func<string, bool> imageExists)
    {
        var preview = new ImportPreview { SourceName = sourceName };
        if (element.ValueKind != JsonValueKind.Object)
        {
            preview.Errors.Add("Een toets moet een object { ... } zijn.");
            return preview;
        }

        CollectUnknownFields(element, preview.Warnings);

        QuizFileDto? dto;
        try
        {
            dto = element.Deserialize<QuizFileDto>(QuizFileJson.ReadOptions);
        }
        catch (JsonException ex)
        {
            preview.Errors.Add($"Een veld heeft een verkeerd type ({ex.Path?.TrimStart('$', '.')}): verwacht tekst, getal, true/false of een lijst zoals in het voorbeeld.");
            return preview;
        }
        if (dto is null)
        {
            preview.Errors.Add("Lege toets.");
            return preview;
        }

        var quiz = new Quiz
        {
            Title = dto.Title?.Trim() ?? "",
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            ShuffleQuestions = dto.ShuffleQuestions ?? false,
            ShuffleOptions = dto.ShuffleOptions ?? false,
        };

        if (dto.Questions is null || dto.Questions.Count == 0)
            preview.Errors.Add("De toets heeft geen vragen (veld \"questions\" ontbreekt of is leeg).");

        var structuralErrors = new HashSet<int>();
        for (var i = 0; i < (dto.Questions?.Count ?? 0); i++)
        {
            var q = dto.Questions![i];
            var label = $"Vraag {i + 1}";
            if (q is null)
            {
                preview.Errors.Add($"{label}: is leeg (null).");
                structuralErrors.Add(i);
                quiz.Questions.Add(new Question { Order = i, Text = "", Points = 1 });
                continue;
            }

            var question = new Question
            {
                Order = i,
                Title = string.IsNullOrWhiteSpace(q.Title) ? null : q.Title.Trim(),
                Text = q.Text?.Trim() ?? "",
                Points = q.Points ?? 1,
                Explanation = string.IsNullOrWhiteSpace(q.Explanation) ? null : q.Explanation.Trim(),
                ImagePath = CheckImage(q.Image, $"{label}", imageExists, preview),
            };

            if (string.IsNullOrWhiteSpace(q.Type))
            {
                preview.Errors.Add($"{label}: type ontbreekt (kies {registry.JsonNames}).");
                structuralErrors.Add(i);
            }
            else if (!registry.TryGetByJsonName(q.Type, out var handler))
            {
                preview.Errors.Add($"{label}: type \"{q.Type}\" is onbekend (kies {registry.JsonNames}).");
                structuralErrors.Add(i);
            }
            else
            {
                question.Type = handler.Type;
            }

            if (question.Type == QuestionType.Dropdown && !structuralErrors.Contains(i))
            {
                if (q.Options is { Count: > 0 })
                    preview.Warnings.Add($"{label}: bij een dropdownvraag horen de keuzes in \"gaps\"; \"options\" wordt genegeerd.");
                if (q.Gaps is null || q.Gaps.Count == 0)
                {
                    preview.Errors.Add($"{label}: \"gaps\" ontbreekt. Geef per gat ({{{{1}}}}, {{{{2}}}}, ...) een lijst met keuzes.");
                    structuralErrors.Add(i);
                }
                else
                {
                    for (var g = 0; g < q.Gaps.Count; g++)
                        AddOptions(question, q.Gaps[g]?.Options, g + 1, $"{label}, gat {g + 1}", imageExists, preview);
                }
            }
            else if (!structuralErrors.Contains(i))
            {
                if (q.Gaps is { Count: > 0 })
                    preview.Warnings.Add($"{label}: \"gaps\" wordt alleen gebruikt bij type dropdown en wordt genegeerd.");
                if (q.Options is null || q.Options.Count == 0)
                {
                    preview.Errors.Add($"{label}: \"options\" ontbreekt of is leeg.");
                    structuralErrors.Add(i);
                }
                else
                {
                    AddOptions(question, q.Options, null, label, imageExists, preview);
                }
            }

            quiz.Questions.Add(question);
        }

        // Inhoudelijke validatie (zelfde regels als in de editor), alleen voor vragen zonder structuurfouten.
        if (string.IsNullOrWhiteSpace(quiz.Title))
            preview.Errors.Insert(0, "Titel ontbreekt (veld \"title\").");
        else if (quiz.Title.Length > 300)
            preview.Errors.Insert(0, "Titel is te lang (maximaal 300 tekens).");

        for (var i = 0; i < quiz.Questions.Count; i++)
        {
            if (structuralErrors.Contains(i)) continue;
            preview.Errors.AddRange(validator.Validate(quiz.Questions[i]).Select(e => $"Vraag {i + 1}: {e}."));
        }

        // Meldingen op volgorde van vraagnummer (algemene meldingen eerst).
        var sorted = preview.Errors.OrderBy(QuestionNumberOf).ToList();
        preview.Errors.Clear();
        preview.Errors.AddRange(sorted);

        preview.Quiz = quiz;
        return preview;
    }

    private static int QuestionNumberOf(string message)
    {
        if (!message.StartsWith("Vraag ")) return 0;
        var digits = new string(message.Skip(6).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }

    private static void AddOptions(Question question, List<OptionDto>? options, int? gap, string label, Func<string, bool> imageExists, ImportPreview preview)
    {
        if (options is null) return;
        for (var j = 0; j < options.Count; j++)
        {
            var o = options[j];
            if (o is null) continue;
            question.Options.Add(new AnswerOption
            {
                GapNumber = gap,
                Order = j,
                Text = o.Text?.Trim() ?? "",
                IsCorrect = o.Correct ?? false,
                ImagePath = gap is null ? CheckImage(o.Image, $"{label}, optie {j + 1}", imageExists, preview) : null,
            });
        }
    }

    private static string? CheckImage(string? image, string label, Func<string, bool> imageExists, ImportPreview preview)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        var name = Path.GetFileName(image.Trim().Replace('\\', '/'));
        if (!ImageStore.IsAllowedImage(name))
        {
            preview.Warnings.Add($"{label}: \"{image}\" is geen ondersteunde afbeelding en wordt overgeslagen.");
            return null;
        }
        if (!imageExists(name))
        {
            preview.Warnings.Add($"{label}: afbeelding \"{name}\" niet gevonden; de vraag wordt zonder afbeelding geïmporteerd.");
            return null;
        }
        preview.ImageNames.Add(name);
        return name;
    }

    private static void CollectUnknownFields(JsonElement quiz, List<string> warnings)
    {
        Check(quiz, QuizFileJson.QuizFields, "Toets");
        if (!TryGet(quiz, "questions", out var questions) || questions.ValueKind != JsonValueKind.Array) return;

        var i = 0;
        foreach (var q in questions.EnumerateArray())
        {
            i++;
            if (q.ValueKind != JsonValueKind.Object) continue;
            Check(q, QuizFileJson.QuestionFields, $"Vraag {i}");
            if (TryGet(q, "options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                foreach (var o in opts.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.Object))
                    Check(o, QuizFileJson.OptionFields, $"Vraag {i}, optie");
            if (TryGet(q, "gaps", out var gaps) && gaps.ValueKind == JsonValueKind.Array)
                foreach (var g in gaps.EnumerateArray().Where(g => g.ValueKind == JsonValueKind.Object))
                {
                    Check(g, QuizFileJson.GapFields, $"Vraag {i}, gat");
                    if (TryGet(g, "options", out var gopts) && gopts.ValueKind == JsonValueKind.Array)
                        foreach (var o in gopts.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.Object))
                            Check(o, QuizFileJson.OptionFields, $"Vraag {i}, gat-optie");
                }
        }

        void Check(JsonElement e, IReadOnlySet<string> allowed, string label)
        {
            foreach (var p in e.EnumerateObject())
                if (!allowed.Contains(p.Name))
                    warnings.Add($"{label}: onbekend veld \"{p.Name}\" wordt genegeerd.");
        }
    }

    private static bool TryGet(JsonElement e, string name, out JsonElement value)
    {
        foreach (var p in e.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        value = default;
        return false;
    }

    /// <summary>Haalt Markdown-codeblokken en tekst rondom de JSON weg (handig bij plakken uit ChatGPT/Claude).</summary>
    internal static string Clean(string? input, out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(input)) return "";
        var text = input.Trim().TrimStart('﻿');

        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            var closing = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && closing > firstNewline)
                text = text[(firstNewline + 1)..closing].Trim();
        }

        if (text.Length > 0 && text[0] != '{' && text[0] != '[')
        {
            var start = text.IndexOfAny(['{', '[']);
            var end = Math.Max(text.LastIndexOf('}'), text.LastIndexOf(']'));
            if (start >= 0 && end > start)
            {
                text = text[start..(end + 1)];
                warning = "Er stond tekst om de JSON heen; die is genegeerd.";
            }
        }
        return text;
    }

    private static string DescribeJsonError(JsonException ex)
    {
        var where = ex.LineNumber is { } line
            ? $" rond regel {line + 1}, positie {(ex.BytePositionInLine ?? 0) + 1}"
            : "";
        return $"De JSON is ongeldig{where}. Controleer komma's, aanhalingstekens en haakjes. ({ex.Message.Split(" Path:")[0].Split(" LineNumber:")[0]})";
    }

    private static ImportPreview Failed(string source, string error)
    {
        var p = new ImportPreview { SourceName = source };
        p.Errors.Add(error);
        return p;
    }
}
