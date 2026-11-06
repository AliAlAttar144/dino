using Toetsr.Domain;

namespace Toetsr.Components.Questions;

/// <summary>Koppelt een vraagtype aan het invoercomponent. Nieuw vraagtype? Voeg hier een regel toe.</summary>
public static class QuestionComponents
{
    /// <param name="Component">Blazor-component dat van <see cref="QuestionInputBase"/> erft.</param>
    /// <param name="RendersText">True als het component de vraagtekst zelf tekent (zoals bij dropdowns).</param>
    public record Entry(Type Component, bool RendersText);

    private static readonly Dictionary<QuestionType, Entry> Map = new()
    {
        [QuestionType.SingleChoice] = new(typeof(ChoiceInput), false),
        [QuestionType.MultipleChoice] = new(typeof(ChoiceInput), false),
        [QuestionType.Dropdown] = new(typeof(DropdownInput), true),
    };

    public static Entry For(QuestionType type) =>
        Map.TryGetValue(type, out var e) ? e : throw new InvalidOperationException($"Geen component voor vraagtype {type}.");
}
