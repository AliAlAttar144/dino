using Microsoft.AspNetCore.Components;
using Toetsr.Domain;

namespace Toetsr.Components.Questions;

/// <summary>Een klik op een optie (of keuze in een dropdown).</summary>
public readonly record struct OptionSelection(int OptionId, bool Selected);

/// <summary>Basis voor invoercomponenten per vraagtype.</summary>
public abstract class QuestionInputBase : ComponentBase
{
    [Parameter, EditorRequired] public Question Question { get; set; } = default!;
    [Parameter] public IReadOnlyList<int> Selected { get; set; } = [];

    /// <summary>Nakijkmodus: geen invoer, wel goed/fout tonen.</summary>
    [Parameter] public bool Review { get; set; }

    /// <summary>Invoer uitgeschakeld (bijv. in een voorbeeld).</summary>
    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool ShowOptionPoints { get; set; }

    /// <summary>Optie die met het toetsenbord is aangewezen (pijltjes / W en S).</summary>
    [Parameter] public int? CursorOptionId { get; set; }
    [Parameter] public string IdPrefix { get; set; } = "q";
    [Parameter] public EventCallback<OptionSelection> OnSelect { get; set; }

    protected bool IsSelected(AnswerOption o) => Selected.Contains(o.Id);
    protected string InputName => $"{IdPrefix}-{Question.Id}-{Question.Order}";
}
