using Toetsr.Domain;
using Toetsr.Services;
using static Toetsr.Tests.TestData;

namespace Toetsr.Tests;

public class AiPromptTests
{
    [Fact]
    public void Choice_lists_options_with_letters_and_asks_for_explanation()
    {
        var text = AiPrompt.ForQuestion(Choice(QuestionType.SingleChoice, 1, false, true, false), includeAnswer: false);
        Assert.Contains("exactly one answer is correct", text);
        Assert.Contains("A. Optie 1", text);
        Assert.Contains("C. Optie 3", text);
        Assert.DoesNotContain("Correct answer", text);
        Assert.EndsWith("Give a short but complete explanation.", text);
    }

    [Fact]
    public void Answer_is_included_only_when_asked()
    {
        var text = AiPrompt.ForQuestion(Choice(QuestionType.MultipleChoice, 1, true, false, true), includeAnswer: true);
        Assert.Contains("one or more answers are correct", text);
        Assert.Contains("Correct answer: A, C", text);
    }

    [Fact]
    public void Dropdown_lists_choices_per_gap()
    {
        var text = AiPrompt.ForQuestion(Dropdown(2), includeAnswer: true);
        Assert.Contains("gat {{1}} en gat {{2}}", text);
        Assert.Contains("{{1}}: Keuze 1.1 / Keuze 1.2 / Keuze 1.3", text);
        Assert.Contains("Correct answer: {{1}} = Keuze 1.1, {{2}} = Keuze 2.1", text);
    }
}
