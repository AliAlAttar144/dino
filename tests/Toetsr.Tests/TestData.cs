using Toetsr.Domain;
using Toetsr.Services;
using Toetsr.Services.ImportExport;
using Toetsr.Services.QuestionTypes;

namespace Toetsr.Tests;

/// <summary>Hulpfuncties om testvragen op te bouwen.</summary>
internal static class TestData
{
    public static readonly QuestionTypeRegistry Registry = QuestionTypeRegistry.CreateDefault();
    public static readonly QuestionValidator Validator = new(Registry);
    public static QuizFileReader Reader() => new(Registry, Validator);

    /// <summary>Meerkeuzevraag; <paramref name="correct"/> geeft per optie aan of hij goed is. Optie-ids zijn 1..n.</summary>
    public static Question Choice(QuestionType type, double points, params bool[] correct) => new()
    {
        Id = 100,
        Type = type,
        Text = "Vraag",
        Points = points,
        Options = correct.Select((c, i) => new AnswerOption { Id = i + 1, Order = i, Text = $"Optie {i + 1}", IsCorrect = c }).ToList(),
    };

    /// <summary>Dropdownvraag met <paramref name="gaps"/> gaten van elk drie keuzes; de eerste keuze van elk gat is goed.
    /// Optie-id = gat * 10 + index (1, 2, 3).</summary>
    public static Question Dropdown(int gaps, double points = 1) => new()
    {
        Id = 200,
        Type = QuestionType.Dropdown,
        Text = string.Join(" en ", Enumerable.Range(1, gaps).Select(g => $"gat {{{{{g}}}}}")),
        Points = points,
        Options = Enumerable.Range(1, gaps).SelectMany(g => Enumerable.Range(1, 3).Select(i => new AnswerOption
        {
            Id = g * 10 + i,
            GapNumber = g,
            Order = i - 1,
            Text = $"Keuze {g}.{i}",
            IsCorrect = i == 1,
        })).ToList(),
    };

    /// <summary>Zoekt de map met Toetsr.csproj (de repo-root) vanaf de testmap.</summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Toetsr.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repo-root niet gevonden.");
    }
}
