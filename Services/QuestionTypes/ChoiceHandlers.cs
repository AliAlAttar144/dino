using Toetsr.Domain;

namespace Toetsr.Services.QuestionTypes;

/// <summary>Gedeelde validatie voor meerkeuzevragen.</summary>
public abstract class ChoiceHandlerBase : IQuestionTypeHandler
{
    public abstract QuestionType Type { get; }
    public abstract string JsonName { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }

    public abstract double Score(Question question, IReadOnlyCollection<int> selectedOptionIds);
    public abstract List<int> ApplySelection(Question question, IReadOnlyCollection<int> current, int optionId, bool selected);

    public bool IsAnswered(Question question, IReadOnlyCollection<int> selectedOptionIds) =>
        question.Options.Any(o => selectedOptionIds.Contains(o.Id));

    public virtual IEnumerable<string> Validate(Question question)
    {
        if (question.Options.Count < 2)
            yield return "minimaal twee antwoordopties nodig";
        for (var i = 0; i < question.Options.Count; i++)
        {
            var o = question.Options[i];
            if (string.IsNullOrWhiteSpace(o.Text) && string.IsNullOrWhiteSpace(o.ImagePath))
                yield return $"antwoordoptie {i + 1} is leeg";
        }
        var dupes = question.Options
            .Where(o => !string.IsNullOrWhiteSpace(o.Text))
            .GroupBy(o => o.Text.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        foreach (var d in dupes)
            yield return $"antwoordoptie \"{d}\" staat er meer dan één keer in";
        if (question.Options.Any(o => o.GapNumber is not null))
            yield return "meerkeuzevragen hebben geen gaten; gebruik type dropdown";
    }
}

/// <summary>Meerkeuze met precies één goed antwoord. Scoreregel: alles of niets.</summary>
public class SingleChoiceHandler : ChoiceHandlerBase
{
    public override QuestionType Type => QuestionType.SingleChoice;
    public override string JsonName => "single";
    public override string DisplayName => "Meerkeuze (één antwoord)";
    public override string Description => "Precies één antwoord is goed. Goed = alle punten, anders 0.";

    public override double Score(Question question, IReadOnlyCollection<int> selectedOptionIds)
    {
        var chosen = question.Options.Where(o => selectedOptionIds.Contains(o.Id)).ToList();
        return chosen.Count == 1 && chosen[0].IsCorrect ? question.Points : 0;
    }

    public override List<int> ApplySelection(Question question, IReadOnlyCollection<int> current, int optionId, bool selected) =>
        selected ? [optionId] : current.Where(id => id != optionId).ToList();

    public override IEnumerable<string> Validate(Question question)
    {
        foreach (var e in base.Validate(question)) yield return e;
        var correct = question.Options.Count(o => o.IsCorrect);
        if (correct == 0) yield return "geen goed antwoord ingevuld";
        else if (correct > 1) yield return $"precies één goed antwoord verwacht, maar er zijn er {correct} (gebruik type multiple voor meerdere goede antwoorden)";
    }
}

/// <summary>
/// Meerkeuze met één of meer goede antwoorden (checkboxes).
/// Scoreregel (deelpunten met correctie voor gokken):
///   score = punten × max(0, goedAangevinkt / aantalGoed − foutAangevinkt / aantalFout)
/// Alles aanvinken of niets aanvinken levert dus 0 op; alleen alle goede antwoorden levert de volle punten op.
/// </summary>
public class MultipleChoiceHandler : ChoiceHandlerBase
{
    public override QuestionType Type => QuestionType.MultipleChoice;
    public override string JsonName => "multiple";
    public override string DisplayName => "Meerkeuze (meerdere antwoorden)";
    public override string Description =>
        "Eén of meer antwoorden zijn goed. Deelpunten: (goed aangevinkt ÷ aantal goed) − (fout aangevinkt ÷ aantal fout), minimaal 0.";

    public override double Score(Question question, IReadOnlyCollection<int> selectedOptionIds)
    {
        var totalCorrect = question.Options.Count(o => o.IsCorrect);
        var totalWrong = question.Options.Count - totalCorrect;
        if (totalCorrect == 0) return 0;

        var chosen = question.Options.Where(o => selectedOptionIds.Contains(o.Id)).ToList();
        var goodChosen = chosen.Count(o => o.IsCorrect);
        var wrongChosen = chosen.Count - goodChosen;

        var fraction = (double)goodChosen / totalCorrect - (totalWrong == 0 ? 0 : (double)wrongChosen / totalWrong);
        return Math.Round(question.Points * Math.Clamp(fraction, 0, 1), 2);
    }

    public override List<int> ApplySelection(Question question, IReadOnlyCollection<int> current, int optionId, bool selected)
    {
        var set = current.Where(id => id != optionId).ToList();
        if (selected) set.Add(optionId);
        // Houd de volgorde van de opties aan, zodat de opgeslagen waarde stabiel is.
        return question.Options.Where(o => set.Contains(o.Id)).Select(o => o.Id).ToList();
    }

    public override IEnumerable<string> Validate(Question question)
    {
        foreach (var e in base.Validate(question)) yield return e;
        if (!question.Options.Any(o => o.IsCorrect)) yield return "geen goed antwoord ingevuld";
    }
}
