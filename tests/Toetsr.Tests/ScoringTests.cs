using Toetsr.Domain;
using Toetsr.Services;
using Toetsr.Services.QuestionTypes;
using static Toetsr.Tests.TestData;

namespace Toetsr.Tests;

public class SingleChoiceScoringTests
{
    private readonly SingleChoiceHandler _handler = new();
    private readonly Question _q = Choice(QuestionType.SingleChoice, 2, false, true, false, false);

    [Fact]
    public void Correct_answer_gives_full_points() => Assert.Equal(2, _handler.Score(_q, [2]));

    [Fact]
    public void Wrong_answer_gives_zero() => Assert.Equal(0, _handler.Score(_q, [1]));

    [Fact]
    public void No_answer_gives_zero() => Assert.Equal(0, _handler.Score(_q, []));

    [Fact]
    public void Multiple_selected_options_give_zero() => Assert.Equal(0, _handler.Score(_q, [1, 2]));

    [Fact]
    public void Selecting_replaces_previous_choice() =>
        Assert.Equal([3], _handler.ApplySelection(_q, [2], 3, true));
}

public class MultipleChoiceScoringTests
{
    private readonly MultipleChoiceHandler _handler = new();

    // Opties 1 en 2 goed, 3 en 4 fout. 2 punten.
    private readonly Question _q = Choice(QuestionType.MultipleChoice, 2, true, true, false, false);

    [Fact]
    public void All_correct_and_no_wrong_gives_full_points() => Assert.Equal(2, _handler.Score(_q, [1, 2]));

    [Fact]
    public void Half_of_the_correct_answers_gives_half_points() => Assert.Equal(1, _handler.Score(_q, [1]));

    [Fact]
    public void Wrong_answer_cancels_part_of_the_score() =>
        // 2/2 goed − 1/2 fout = 0,5 → 1 punt
        Assert.Equal(1, _handler.Score(_q, [1, 2, 3]));

    [Fact]
    public void Selecting_everything_gives_zero() => Assert.Equal(0, _handler.Score(_q, [1, 2, 3, 4]));

    [Fact]
    public void Selecting_nothing_gives_zero() => Assert.Equal(0, _handler.Score(_q, []));

    [Fact]
    public void Score_never_goes_below_zero() => Assert.Equal(0, _handler.Score(_q, [3, 4]));

    [Fact]
    public void Partial_score_is_rounded_to_two_decimals()
    {
        var q = Choice(QuestionType.MultipleChoice, 1, true, true, true, false);
        Assert.Equal(0.33, _handler.Score(q, [1]));
    }

    [Fact]
    public void When_all_options_are_correct_there_is_no_penalty()
    {
        var q = Choice(QuestionType.MultipleChoice, 3, true, true, true);
        Assert.Equal(3, _handler.Score(q, [1, 2, 3]));
        Assert.Equal(2, _handler.Score(q, [1, 2]));
    }

    [Fact]
    public void Toggle_adds_and_removes_in_option_order()
    {
        var afterAdd = _handler.ApplySelection(_q, [3], 1, true);
        Assert.Equal([1, 3], afterAdd);
        Assert.Equal([1], _handler.ApplySelection(_q, afterAdd, 3, false));
    }

    [Fact]
    public void Unknown_option_ids_are_ignored_for_scoring() => Assert.Equal(2, _handler.Score(_q, [1, 2, 999]));
}

public class DropdownScoringTests
{
    private readonly DropdownHandler _handler = new();

    [Fact]
    public void All_gaps_correct_gives_full_points() =>
        Assert.Equal(3, _handler.Score(Dropdown(3, 3), [11, 21, 31]));

    [Fact]
    public void Each_gap_is_worth_an_equal_share() =>
        Assert.Equal(1, _handler.Score(Dropdown(2, 2), [11, 22]));

    [Fact]
    public void Empty_answer_gives_zero() => Assert.Equal(0, _handler.Score(Dropdown(2), []));

    [Fact]
    public void Choosing_in_a_gap_replaces_only_that_gap()
    {
        var q = Dropdown(2);
        var selection = _handler.ApplySelection(q, [11, 21], 12, true);
        Assert.Equal([21, 12], selection);
    }

    [Fact]
    public void Clearing_a_gap_removes_its_choice() =>
        Assert.Equal([21], _handler.ApplySelection(Dropdown(2), [11, 21], 11, false));
}

public class AttemptScoringTests
{
    [Fact]
    public void Score_sums_question_scores_and_sets_answer_scores()
    {
        var service = new AttemptService(null!, Registry);
        var single = Choice(QuestionType.SingleChoice, 1, true, false);
        single.Id = 1;
        var multi = Choice(QuestionType.MultipleChoice, 2, true, true, false, false);
        multi.Id = 2;
        var dropdown = Dropdown(2, 2);
        dropdown.Id = 3;

        var answers = new List<AttemptAnswer>
        {
            new() { QuestionId = 1, SelectedOptionIds = [1] },
            new() { QuestionId = 2, SelectedOptionIds = [1] },
        };

        var (total, max) = service.Score([single, multi, dropdown], answers);

        Assert.Equal(2, total);
        Assert.Equal(5, max);
        Assert.Equal(3, answers.Count); // ontbrekend antwoord voor de dropdown is aangevuld
        Assert.Equal(0, answers.Single(a => a.QuestionId == 3).Score);
    }

    [Theory]
    [InlineData(0, 10, 1.0)]
    [InlineData(10, 10, 10.0)]
    [InlineData(5, 10, 5.5)]
    [InlineData(7, 10, 7.3)]
    public void Grade_runs_from_one_to_ten(double score, double max, double expected) =>
        Assert.Equal(expected, Grading.ToGrade(score, max));

    [Fact]
    public void Snapshot_shuffle_keeps_dropdown_options_within_their_gap()
    {
        var q = Dropdown(3);
        var snapshot = AttemptService.BuildSnapshot([q], shuffleQuestions: true, shuffleOptions: true, new Random(42));
        var copy = Assert.Single(snapshot);

        Assert.NotSame(q, copy);
        Assert.Equal(q.Options.Select(o => o.Id).Order(), copy.Options.Select(o => o.Id).Order());
        foreach (var o in copy.Options)
            Assert.Equal(o.Id / 10, o.GapNumber);
        foreach (var gap in copy.Options.GroupBy(o => o.GapNumber))
            Assert.Equal([0, 1, 2], gap.Select(o => o.Order).Order());
    }
}
