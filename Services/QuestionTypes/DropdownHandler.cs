using System.Text.RegularExpressions;
using Toetsr.Domain;

namespace Toetsr.Services.QuestionTypes;

/// <summary>
/// Tekst met gaten ({{1}}, {{2}}, ...) waarbij de gebruiker per gat uit een dropdown kiest.
/// Scoreregel: elk gat is evenveel waard; score = punten × (goed ingevulde gaten ÷ aantal gaten).
/// </summary>
public class DropdownHandler : IQuestionTypeHandler
{
    public QuestionType Type => QuestionType.Dropdown;
    public string JsonName => "dropdown";
    public string DisplayName => "Dropdown (tekst met gaten)";
    public string Description => "Tekst met gaten als {{1}}. Per gat één goed antwoord. Elk gat levert een even groot deel van de punten op.";

    public double Score(Question question, IReadOnlyCollection<int> selectedOptionIds)
    {
        var gaps = DropdownText.GapNumbers(question.Text);
        if (gaps.Count == 0) return 0;

        var correctGaps = gaps.Count(gap =>
        {
            var chosen = question.Options.Where(o => o.GapNumber == gap && selectedOptionIds.Contains(o.Id)).ToList();
            return chosen.Count == 1 && chosen[0].IsCorrect;
        });
        return Math.Round(question.Points * correctGaps / gaps.Count, 2);
    }

    public bool IsAnswered(Question question, IReadOnlyCollection<int> selectedOptionIds) =>
        question.Options.Any(o => selectedOptionIds.Contains(o.Id));

    /// <summary>Binnen één gat kan maar één optie gekozen zijn; andere gaten blijven staan.</summary>
    public List<int> ApplySelection(Question question, IReadOnlyCollection<int> current, int optionId, bool selected)
    {
        var option = question.Options.FirstOrDefault(o => o.Id == optionId);
        if (option is null) return current.ToList();

        var sameGap = question.Options.Where(o => o.GapNumber == option.GapNumber).Select(o => o.Id).ToHashSet();
        var result = current.Where(id => !sameGap.Contains(id)).ToList();
        if (selected) result.Add(optionId);
        return result;
    }

    public IEnumerable<string> Validate(Question question)
    {
        var gaps = DropdownText.GapNumbers(question.Text);
        if (gaps.Count == 0)
        {
            yield return "geen gaten gevonden in de tekst; zet bijvoorbeeld {{1}} op de plek van de dropdown";
            yield break;
        }

        var expected = Enumerable.Range(1, gaps.Count).ToList();
        if (!gaps.SequenceEqual(expected))
            yield return $"de gaten moeten oplopend genummerd zijn vanaf {{{{1}}}} (gevonden: {string.Join(", ", gaps.Select(g => $"{{{{{g}}}}}"))})";

        foreach (var gap in gaps)
        {
            var options = question.Options.Where(o => o.GapNumber == gap).ToList();
            if (options.Count < 2)
                yield return $"gat {{{{{gap}}}}} heeft minimaal twee keuzes nodig";
            var correct = options.Count(o => o.IsCorrect);
            if (correct == 0) yield return $"gat {{{{{gap}}}}}: geen goed antwoord ingevuld";
            else if (correct > 1) yield return $"gat {{{{{gap}}}}}: precies één goed antwoord verwacht, maar er zijn er {correct}";
            if (options.Any(o => string.IsNullOrWhiteSpace(o.Text)))
                yield return $"gat {{{{{gap}}}}}: een keuze is leeg";
            foreach (var dup in options.GroupBy(o => o.Text.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1 && g.Key != ""))
                yield return $"gat {{{{{gap}}}}}: keuze \"{dup.Key}\" staat er meer dan één keer in";
        }

        foreach (var orphan in question.Options.Where(o => o.GapNumber is null || !gaps.Contains(o.GapNumber.Value)).Select(o => o.GapNumber).Distinct())
            yield return orphan is null
                ? "er zijn keuzes zonder gatnummer"
                : $"er zijn keuzes voor gat {{{{{orphan}}}}}, maar dat gat staat niet in de tekst";
    }
}

/// <summary>Hulpfuncties om dropdowntekst met {{n}}-gaten te lezen.</summary>
public static partial class DropdownText
{
    [GeneratedRegex(@"\{\{\s*(\d+)\s*\}\}")]
    public static partial Regex GapRegex();

    /// <summary>Unieke gatnummers in volgorde van eerste voorkomen.</summary>
    public static List<int> GapNumbers(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : GapRegex().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();

    public static int NextGapNumber(string? text)
    {
        var gaps = GapNumbers(text);
        return gaps.Count == 0 ? 1 : gaps.Max() + 1;
    }

    /// <summary>Deelt tekst op in stukken tekst en gaten.</summary>
    public static List<Segment> Split(string text)
    {
        var result = new List<Segment>();
        var last = 0;
        foreach (Match m in GapRegex().Matches(text))
        {
            if (m.Index > last) result.Add(new Segment(text[last..m.Index], null));
            result.Add(new Segment(null, int.Parse(m.Groups[1].Value)));
            last = m.Index + m.Length;
        }
        if (last < text.Length) result.Add(new Segment(text[last..], null));
        return result;
    }

    /// <summary>Deelt Markdown op in blokken (alinea's), waarbij codeblokken heel blijven.</summary>
    public static List<string> SplitBlocks(string text)
    {
        var blocks = new List<string>();
        var current = new List<string>();
        var inFence = false;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~"))
                inFence = !inFence;
            if (!inFence && string.IsNullOrWhiteSpace(line))
            {
                if (current.Count > 0) blocks.Add(string.Join('\n', current));
                current.Clear();
                continue;
            }
            current.Add(line);
        }
        if (current.Count > 0) blocks.Add(string.Join('\n', current));
        return blocks;
    }

    public readonly record struct Segment(string? Text, int? Gap);
}
