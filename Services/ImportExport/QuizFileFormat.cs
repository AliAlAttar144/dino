using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toetsr.Services.ImportExport;

// Het JSON-formaat voor toetsen. Zie README.md voor uitleg en een voorbeeld.

public class QuizFileDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool? ShuffleQuestions { get; set; }
    public bool? ShuffleOptions { get; set; }
    public List<QuestionDto>? Questions { get; set; }
}

public class QuestionDto
{
    /// <summary>"single", "multiple" of "dropdown".</summary>
    public string? Type { get; set; }
    public string? Title { get; set; }
    public string? Text { get; set; }
    public string? Image { get; set; }
    public double? Points { get; set; }
    public string? Explanation { get; set; }

    /// <summary>Antwoordopties bij single/multiple.</summary>
    public List<OptionDto>? Options { get; set; }

    /// <summary>Bij dropdown: per gat de keuzes. Het eerste element hoort bij {{1}}, het tweede bij {{2}}, enz.</summary>
    public List<GapDto>? Gaps { get; set; }
}

public class OptionDto
{
    public string? Text { get; set; }
    public bool? Correct { get; set; }
    public string? Image { get; set; }
}

public class GapDto
{
    public List<OptionDto>? Options { get; set; }
}

public static class QuizFileJson
{
    /// <summary>Opties voor inlezen: hoofdletterongevoelig, commentaar en komma's aan het eind toegestaan.</summary>
    public static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Opties voor exporteren: leesbaar, camelCase, zonder lege velden.</summary>
    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly IReadOnlySet<string> QuizFields = Names("title", "description", "shuffleQuestions", "shuffleOptions", "questions", "$schema");
    public static readonly IReadOnlySet<string> QuestionFields = Names("type", "title", "text", "image", "points", "explanation", "options", "gaps");
    public static readonly IReadOnlySet<string> OptionFields = Names("text", "correct", "image");
    public static readonly IReadOnlySet<string> GapFields = Names("options");

    private static HashSet<string> Names(params string[] names) => new(names, StringComparer.OrdinalIgnoreCase);
}
