using Toetsr.Domain;
using Toetsr.Services.QuestionTypes;

namespace Toetsr.Services;

/// <summary>Validatie die voor elke vraag geldt, plus de type-specifieke regels van de handler.</summary>
public class QuestionValidator(QuestionTypeRegistry registry)
{
    public const double MaxPoints = 1000;

    public List<string> Validate(Question question)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(question.Text))
            errors.Add("vraagtekst ontbreekt");
        if (double.IsNaN(question.Points) || question.Points <= 0)
            errors.Add("punten moeten groter dan 0 zijn");
        else if (question.Points > MaxPoints)
            errors.Add($"punten mogen niet hoger zijn dan {MaxPoints}");
        errors.AddRange(registry.Get(question.Type).Validate(question));
        return errors;
    }

    /// <summary>Valideert alle vragen van een toets. Meldingen beginnen met "Vraag n:".</summary>
    public List<string> Validate(Quiz quiz)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(quiz.Title))
            errors.Add("Titel ontbreekt");
        else if (quiz.Title.Length > 300)
            errors.Add("Titel is te lang (maximaal 300 tekens)");
        for (var i = 0; i < quiz.Questions.Count; i++)
            errors.AddRange(Validate(quiz.Questions[i]).Select(e => $"Vraag {i + 1}: {e}"));
        return errors;
    }
}

public static class Grading
{
    /// <summary>Nederlands cijfer 1,0 – 10,0 op basis van het percentage behaalde punten.</summary>
    public static double ToGrade(double score, double max) =>
        max <= 0 ? 1 : Math.Round(1 + 9 * Math.Clamp(score / max, 0, 1), 1);

    public const double PassGrade = 5.5;

    public static bool Passed(double score, double max) => ToGrade(score, max) >= PassGrade;
}
