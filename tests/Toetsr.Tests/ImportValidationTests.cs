using System.Text.Json;
using Toetsr.Domain;
using Toetsr.Services.ImportExport;
using static Toetsr.Tests.TestData;

namespace Toetsr.Tests;

public class ImportValidationTests
{
    private static readonly Func<string, bool> NoImages = _ => false;

    private static ImportPreview ReadOne(string json, Func<string, bool>? images = null) =>
        Assert.Single(Reader().Read(json, "test.json", images ?? NoImages));

    private const string Valid = """
        {
          "title": "Toets",
          "questions": [
            { "type": "single", "text": "Een", "options": [ { "text": "A", "correct": true }, { "text": "B" } ] },
            { "type": "multiple", "text": "Twee", "points": 2, "options": [ { "text": "A", "correct": true }, { "text": "B", "correct": true }, { "text": "C" } ] },
            { "type": "dropdown", "text": "Hoofdstad: {{1}}", "gaps": [ { "options": [ { "text": "Parijs", "correct": true }, { "text": "Lyon" } ] } ] }
          ]
        }
        """;

    [Fact]
    public void Valid_quiz_has_no_errors_and_maps_all_types()
    {
        var p = ReadOne(Valid);

        Assert.True(p.IsValid, string.Join("; ", p.Errors));
        Assert.Equal("Toets", p.Quiz!.Title);
        Assert.Equal([QuestionType.SingleChoice, QuestionType.MultipleChoice, QuestionType.Dropdown], p.Quiz.Questions.Select(q => q.Type));
        Assert.Equal(1, p.Quiz.Questions[0].Points); // standaard 1 punt
        Assert.All(p.Quiz.Questions[2].Options, o => Assert.Equal(1, o.GapNumber));
    }

    [Fact]
    public void Missing_correct_answer_is_reported_with_question_number()
    {
        var p = ReadOne("""{ "title": "T", "questions": [ { "type": "single", "text": "x", "options": [ { "text": "a" }, { "text": "b" } ] } ] }""");
        Assert.Contains("Vraag 1: geen goed antwoord ingevuld.", p.Errors);
    }

    [Fact]
    public void Single_with_two_correct_answers_is_an_error()
    {
        var p = ReadOne("""{ "title": "T", "questions": [ { "type": "single", "text": "x", "options": [ { "text": "a", "correct": true }, { "text": "b", "correct": true } ] } ] }""");
        Assert.Contains(p.Errors, e => e.StartsWith("Vraag 1: precies één goed antwoord verwacht"));
    }

    [Fact]
    public void Unknown_type_is_reported()
    {
        var p = ReadOne("""{ "title": "T", "questions": [ { "type": "essay", "text": "x" } ] }""");
        Assert.Contains(p.Errors, e => e.Contains("Vraag 1: type \"essay\" is onbekend"));
    }

    [Fact]
    public void Missing_title_and_questions_are_reported()
    {
        var p = ReadOne("""{ "questions": [] }""");
        Assert.Contains(p.Errors, e => e.StartsWith("Titel ontbreekt"));
        Assert.Contains(p.Errors, e => e.Contains("geen vragen"));
    }

    [Fact]
    public void Invalid_json_reports_line_number()
    {
        var p = ReadOne("{\n  \"title\": \"T\",\n  \"questions\": [ oops ]\n}");
        Assert.False(p.IsValid);
        Assert.Contains("rond regel 3", Assert.Single(p.Errors));
    }

    [Fact]
    public void Gap_in_text_without_options_is_reported()
    {
        var p = ReadOne("""{ "title": "T", "questions": [ { "type": "dropdown", "text": "{{1}} en {{2}}", "gaps": [ { "options": [ { "text": "a", "correct": true }, { "text": "b" } ] } ] } ] }""");
        Assert.Contains(p.Errors, e => e.Contains("gat {{2}} heeft minimaal twee keuzes nodig"));
        Assert.Contains(p.Errors, e => e.Contains("gat {{2}}: geen goed antwoord ingevuld"));
    }

    [Fact]
    public void Gaps_without_placeholder_in_text_are_reported()
    {
        var p = ReadOne("""{ "title": "T", "questions": [ { "type": "dropdown", "text": "Geen gaten", "gaps": [ { "options": [ { "text": "a", "correct": true }, { "text": "b" } ] } ] } ] }""");
        Assert.Contains(p.Errors, e => e.Contains("geen gaten gevonden in de tekst"));
    }

    [Fact]
    public void Code_fences_and_surrounding_text_are_stripped()
    {
        var p = ReadOne("Hier is je toets:\n```json\n" + Valid + "\n```\nSucces!");
        Assert.True(p.IsValid, string.Join("; ", p.Errors));
    }

    [Fact]
    public void Unknown_fields_give_a_warning_not_an_error()
    {
        var p = ReadOne("""{ "title": "T", "author": "x", "questions": [ { "type": "single", "text": "x", "answer": 1, "options": [ { "text": "a", "correct": true }, { "text": "b" } ] } ] }""");
        Assert.True(p.IsValid);
        Assert.Contains(p.Warnings, w => w.Contains("\"author\""));
        Assert.Contains(p.Warnings, w => w.Contains("Vraag 1") && w.Contains("\"answer\""));
    }

    [Fact]
    public void Missing_image_is_a_warning_and_image_is_dropped()
    {
        var p = ReadOne("""{ "title": "T", "questions": [ { "type": "single", "text": "x", "image": "kaart.png", "options": [ { "text": "a", "correct": true }, { "text": "b" } ] } ] }""");
        Assert.True(p.IsValid);
        Assert.Null(p.Quiz!.Questions[0].ImagePath);
        Assert.Contains(p.Warnings, w => w.Contains("kaart.png"));
    }

    [Fact]
    public void Existing_image_is_kept_by_file_name()
    {
        var p = ReadOne("""{ "title": "T", "questions": [ { "type": "single", "text": "x", "image": "sub/kaart.PNG", "options": [ { "text": "a", "correct": true }, { "text": "b" } ] } ] }""",
            name => name == "kaart.PNG");
        Assert.Equal("kaart.PNG", p.Quiz!.Questions[0].ImagePath);
        Assert.Contains("kaart.PNG", p.ImageNames);
    }

    [Fact]
    public void Array_root_reads_multiple_quizzes()
    {
        var previews = Reader().Read($"[{Valid}, {Valid}]", "lijst.json", NoImages);
        Assert.Equal(2, previews.Count);
        Assert.All(previews, p => Assert.True(p.IsValid));
    }

    [Fact]
    public void Property_names_are_case_insensitive_and_trailing_commas_allowed()
    {
        var p = ReadOne("""{ "Title": "T", "QUESTIONS": [ { "Type": "SINGLE", "Text": "x", "Options": [ { "Text": "a", "Correct": true }, { "Text": "b" }, ], }, ], }""");
        Assert.True(p.IsValid, string.Join("; ", p.Errors));
    }

    [Fact]
    public void Wrong_value_type_is_reported_clearly()
    {
        var p = ReadOne("""{ "title": "T", "questions": [ { "type": "single", "text": "x", "points": "veel", "options": [] } ] }""");
        Assert.False(p.IsValid);
        Assert.Contains(p.Errors, e => e.Contains("verkeerd type"));
    }

    [Theory]
    [InlineData("voorbeeld-webdevelopment.json")]
    [InlineData("voorbeeld-aardrijkskunde.json")]
    public void Bundled_sample_quizzes_are_valid(string file)
    {
        var folder = Path.Combine(RepoRoot(), "Toetsen");
        var images = new FolderImageSource(Path.Combine(folder, "afbeeldingen"));
        var p = ReadOne(File.ReadAllText(Path.Combine(folder, file)), images.Exists);

        Assert.True(p.IsValid, string.Join("; ", p.Errors));
        Assert.Empty(p.Warnings);
    }

    [Fact]
    public void Export_and_reimport_gives_the_same_quiz()
    {
        var original = ReadOne(Valid).Quiz!;
        original.Questions[0].Explanation = "Uitleg";
        original.Questions[0].Excluded = true;

        var json = ExportService.ToJson(original, Registry);
        var again = ReadOne(json).Quiz!;

        Assert.Equal(original.Title, again.Title);
        Assert.Equal(original.Questions.Count, again.Questions.Count);
        for (var i = 0; i < original.Questions.Count; i++)
        {
            Assert.Equal(original.Questions[i].Type, again.Questions[i].Type);
            Assert.Equal(original.Questions[i].Text, again.Questions[i].Text);
            Assert.Equal(original.Questions[i].Points, again.Questions[i].Points);
            Assert.Equal(original.Questions[i].Explanation, again.Questions[i].Explanation);
            Assert.Equal(original.Questions[i].Excluded, again.Questions[i].Excluded);
            Assert.Equal(
                original.Questions[i].Options.Select(o => (o.Text, o.IsCorrect, o.GapNumber)),
                again.Questions[i].Options.Select(o => (o.Text, o.IsCorrect, o.GapNumber)));
        }
    }

    [Fact]
    public void Export_writes_readable_camel_case_json_without_empty_fields()
    {
        var json = ExportService.ToJson(ReadOne(Valid).Quiz!, Registry);
        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("questions")[0];

        Assert.Equal("single", first.GetProperty("type").GetString());
        Assert.False(first.TryGetProperty("gaps", out _));
        Assert.False(first.GetProperty("options")[1].TryGetProperty("correct", out _));
    }

    [Theory]
    [InlineData("Mijn Toets: Hoofdstuk 1!", "mijn-toets-hoofdstuk-1")]
    [InlineData("Één ‘rare’ titel", "een-rare-titel")]
    [InlineData("!!!", "toets")]
    public void Slug_makes_safe_file_names(string title, string expected) =>
        Assert.Equal(expected, ExportService.Slug(title));
}
