using Toetsr.Domain;

namespace Toetsr.Services.QuestionTypes;

/// <summary>
/// Strategie per vraagtype: scoring, validatie en hoe een klik de selectie verandert.
/// Nieuw vraagtype toevoegen:
///   1. waarde toevoegen aan <see cref="QuestionType"/>;
///   2. een klasse maken die deze interface implementeert en registreren in Program.cs;
///   3. een invoercomponent maken (Components/Questions) en koppelen in QuestionComponents.cs.
/// </summary>
public interface IQuestionTypeHandler
{
    QuestionType Type { get; }

    /// <summary>Naam in het JSON-formaat, bijv. "single".</summary>
    string JsonName { get; }

    /// <summary>Naam in de interface, bijv. "Meerkeuze (één antwoord)".</summary>
    string DisplayName { get; }

    /// <summary>Korte uitleg van het type en de scoreregel.</summary>
    string Description { get; }

    /// <summary>Behaalde punten (0 t/m <see cref="Question.Points"/>) voor de gegeven selectie.</summary>
    double Score(Question question, IReadOnlyCollection<int> selectedOptionIds);

    /// <summary>Of de vraag als beantwoord telt.</summary>
    bool IsAnswered(Question question, IReadOnlyCollection<int> selectedOptionIds);

    /// <summary>Nieuwe selectie nadat de gebruiker optie <paramref name="optionId"/> (de)selecteert.</summary>
    List<int> ApplySelection(Question question, IReadOnlyCollection<int> current, int optionId, bool selected);

    /// <summary>Type-specifieke validatie. Geeft foutmeldingen terug (leeg = geldig).</summary>
    IEnumerable<string> Validate(Question question);
}

/// <summary>Zoekt de juiste handler bij een vraagtype.</summary>
public class QuestionTypeRegistry
{
    private readonly Dictionary<QuestionType, IQuestionTypeHandler> _byType;
    private readonly Dictionary<string, IQuestionTypeHandler> _byJsonName;

    public QuestionTypeRegistry(IEnumerable<IQuestionTypeHandler> handlers)
    {
        All = handlers.OrderBy(h => h.Type).ToList();
        _byType = All.ToDictionary(h => h.Type);
        _byJsonName = All.ToDictionary(h => h.JsonName, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IQuestionTypeHandler> All { get; }

    public IQuestionTypeHandler Get(QuestionType type) =>
        _byType.TryGetValue(type, out var h) ? h : throw new InvalidOperationException($"Geen handler voor vraagtype {type}.");

    public bool TryGetByJsonName(string? name, out IQuestionTypeHandler handler) =>
        _byJsonName.TryGetValue(name?.Trim() ?? "", out handler!);

    public string JsonNames => string.Join(", ", All.Select(h => $"\"{h.JsonName}\""));

    /// <summary>Standaard-registry met de ingebouwde typen (handig voor tests en tools).</summary>
    public static QuestionTypeRegistry CreateDefault() =>
        new([new SingleChoiceHandler(), new MultipleChoiceHandler(), new DropdownHandler()]);
}
