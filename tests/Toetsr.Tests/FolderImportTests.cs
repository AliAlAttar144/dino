using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Toetsr.Data;
using Toetsr.Domain;
using Toetsr.Services;
using Toetsr.Services.ImportExport;
using static Toetsr.Tests.TestData;

namespace Toetsr.Tests;

/// <summary>Integratietests met een echte SQLite-database in een tijdelijke map.</summary>
public sealed class FolderImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "toetsr-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly TestDbFactory _db;
    private readonly FolderImportService _folderImport;
    private readonly QuizService _quizzes;
    private readonly AttemptService _attempts;

    private const string QuizJson = """
        { "title": "Map-toets", "questions": [
          { "type": "single", "text": "Vraag", "options": [ { "text": "Goed", "correct": true }, { "text": "Fout" } ] }
        ] }
        """;

    public FolderImportTests()
    {
        var options = new ToetsrOptions();
        _paths = new AppPaths(_root, options);
        _paths.EnsureFolders();
        _db = new TestDbFactory(_paths.DatabasePath);
        using (var ctx = _db.CreateDbContext()) ctx.Database.Migrate();

        var images = new ImageStore(_paths, options, _db, NullLogger<ImageStore>.Instance);
        var reader = Reader();
        var import = new ImportService(_db, reader, images, _paths, options);
        _folderImport = new FolderImportService(_db, reader, import, Registry, _paths, new FolderImportReport(), NullLogger<FolderImportService>.Instance);
        _quizzes = new QuizService(_db, Validator);
        _attempts = new AttemptService(_db, Registry);

        // Zelfde koppeling als in Program.cs.
        _quizzes.Changed += id => _folderImport.WriteQuizAsync(id);
        _quizzes.Deleting += id => _folderImport.MoveToDeletedAsync(id);
    }

    private string[] JsonFilesInFolder() =>
        Directory.GetFiles(_paths.QuizFolder, "*.json").Select(Path.GetFileName).Order().ToArray()!;

    /// <summary>Nieuwe (nog niet opgeslagen) meerkeuzevraag voor de editor.</summary>
    private static Question NewQuestion()
    {
        var q = Choice(QuestionType.SingleChoice, 1, true, false);
        q.Id = 0;
        return q;
    }

    [Fact]
    public async Task Quiz_made_in_app_is_written_to_folder_and_not_reimported()
    {
        var created = await _quizzes.CreateAsync("Mijn eigen toets", null, false, false);
        Assert.Empty(JsonFilesInFolder()); // nog geen vragen → nog geen geldig bestand

        var saved = await _quizzes.SaveQuestionAsync(created.Id, NewQuestion());
        Assert.True(saved.Success);

        Assert.Equal(["mijn-eigen-toets.json"], JsonFilesInFolder());
        var json = await File.ReadAllTextAsync(Path.Combine(_paths.QuizFolder, "mijn-eigen-toets.json"));
        Assert.Contains("\"Mijn eigen toets\"", json);

        // Het geschreven bestand is meteen "ongewijzigd": geen dubbele toets.
        var entry = Assert.Single(await _folderImport.ImportAllAsync());
        Assert.Equal(FolderImportStatus.Unchanged, entry.Status);
        Assert.Equal(created.Id, entry.QuizId);
        Assert.Single(await _quizzes.GetSummariesAsync());
    }

    [Fact]
    public async Task Editing_imported_quiz_in_app_updates_its_file()
    {
        WriteQuiz(QuizJson);
        var quizId = Assert.Single(await _folderImport.ImportAllAsync()).QuizId!.Value;

        await _quizzes.UpdateMetaAsync(quizId, "Hernoemd in de app", null, false, false);

        Assert.Equal(["map.json"], JsonFilesInFolder()); // zelfde bestandsnaam
        Assert.Contains("Hernoemd in de app", await File.ReadAllTextAsync(Path.Combine(_paths.QuizFolder, "map.json")));
        Assert.Equal(FolderImportStatus.Unchanged, Assert.Single(await _folderImport.ImportAllAsync()).Status);
    }

    [Fact]
    public async Task Existing_quizzes_without_file_are_exported()
    {
        // Toets van vóór de koppeling: zonder events opgeslagen.
        var plain = new QuizService(_db, Validator);
        var created = await plain.CreateAsync("Oude toets", null, false, false);
        await plain.SaveQuestionAsync(created.Id, NewQuestion());
        Assert.Empty(JsonFilesInFolder());

        await _folderImport.ExportMissingAsync();
        await _folderImport.ExportMissingAsync();

        Assert.Equal(["oude-toets.json"], JsonFilesInFolder());
        Assert.Equal(FolderImportStatus.Unchanged, Assert.Single(await _folderImport.ImportAllAsync()).Status);
    }

    [Fact]
    public async Task Duplicate_gets_its_own_file()
    {
        WriteQuiz(QuizJson);
        var quizId = Assert.Single(await _folderImport.ImportAllAsync()).QuizId!.Value;

        await _quizzes.DuplicateAsync(quizId);

        Assert.Equal(["map-toets-kopie.json", "map.json"], JsonFilesInFolder());
        Assert.All(await _folderImport.ImportAllAsync(), e => Assert.Equal(FolderImportStatus.Unchanged, e.Status));
    }

    private void WriteQuiz(string json, string name = "map.json") => File.WriteAllText(Path.Combine(_paths.QuizFolder, name), json);

    [Fact]
    public async Task Importing_twice_does_not_create_duplicates()
    {
        WriteQuiz(QuizJson);

        var first = Assert.Single(await _folderImport.ImportAllAsync());
        var second = Assert.Single(await _folderImport.ImportAllAsync());

        Assert.Equal(FolderImportStatus.Imported, first.Status);
        Assert.Equal(FolderImportStatus.Unchanged, second.Status);
        Assert.Single(await _quizzes.GetSummariesAsync());
    }

    [Fact]
    public async Task Changed_file_updates_quiz_and_keeps_attempts()
    {
        WriteQuiz(QuizJson);
        var quizId = Assert.Single(await _folderImport.ImportAllAsync()).QuizId!.Value;
        var attemptId = await _attempts.StartAsync(quizId, false, false);
        await _attempts.SubmitAsync(attemptId);

        WriteQuiz(QuizJson.Replace("Map-toets", "Map-toets v2").Replace("\"Vraag\"", "\"Nieuwe vraag\""));
        var result = Assert.Single(await _folderImport.ImportAllAsync());

        Assert.Equal(FolderImportStatus.Updated, result.Status);
        Assert.Equal(quizId, result.QuizId);
        var quiz = await _quizzes.GetAsync(quizId);
        Assert.Equal("Map-toets v2", quiz!.Title);
        Assert.Equal("Nieuwe vraag", Assert.Single(quiz.Questions).Text);

        // De oude poging toont nog de oude vraag (snapshot).
        var session = await _attempts.GetSessionAsync(attemptId);
        Assert.Equal("Vraag", Assert.Single(session!.Questions).Text);
    }

    [Fact]
    public async Task Flagged_questions_carry_over_and_can_be_practiced_alone()
    {
        WriteQuiz("""
            { "title": "Markeren", "questions": [
              { "type": "single", "text": "A", "options": [ { "text": "Goed", "correct": true }, { "text": "Fout" } ] },
              { "type": "single", "text": "B", "options": [ { "text": "Goed", "correct": true }, { "text": "Fout" } ] }
            ] }
            """);
        var quizId = Assert.Single(await _folderImport.ImportAllAsync()).QuizId!.Value;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _attempts.StartAsync(quizId, false, false, onlyFlagged: true));

        var first = await _attempts.StartAsync(quizId, false, false);
        var b = (await _attempts.GetSessionAsync(first))!.Questions.Single(q => q.Text == "B");
        await _attempts.SetFlagAsync(first, b.Id, true);
        await _attempts.SubmitAsync(first);
        Assert.Equal([b.Id], await _attempts.GetFlaggedQuestionIdsAsync(quizId));

        // Alleen de gemarkeerde vraag, en die is nog steeds gemarkeerd.
        var second = await _attempts.StartAsync(quizId, false, false, onlyFlagged: true);
        var session = (await _attempts.GetSessionAsync(second))!;
        Assert.Equal(b.Id, Assert.Single(session.Questions).Id);
        Assert.True(session.IsFlagged(b.Id));

        // Markering weghalen in de nieuwste poging haalt hem uit de set.
        await _attempts.SetFlagAsync(second, b.Id, false);
        Assert.Empty(await _attempts.GetFlaggedQuestionIdsAsync(quizId));
    }

    [Fact]
    public async Task Deleting_quiz_in_app_moves_file_to_deleted_folder()
    {
        WriteQuiz(QuizJson);
        var quizId = Assert.Single(await _folderImport.ImportAllAsync()).QuizId!.Value;
        await _quizzes.DeleteAsync(quizId);

        Assert.Empty(JsonFilesInFolder());
        Assert.True(File.Exists(Path.Combine(_folderImport.DeletedFolder, "map.json")));
        Assert.Empty(await _folderImport.ImportAllAsync());
        Assert.Empty(await _quizzes.GetSummariesAsync());

        // Bestand terugzetten = toets terug.
        File.Move(Path.Combine(_folderImport.DeletedFolder, "map.json"), Path.Combine(_paths.QuizFolder, "map.json"));
        Assert.Equal(FolderImportStatus.Imported, Assert.Single(await _folderImport.ImportAllAsync()).Status);
    }

    [Fact]
    public async Task Invalid_file_is_reported_and_skipped()
    {
        WriteQuiz("""{ "title": "Kapot", "questions": [ { "type": "single", "text": "x", "options": [ { "text": "a" }, { "text": "b" } ] } ] }""", "kapot.json");
        WriteQuiz(QuizJson);

        var results = await _folderImport.ImportAllAsync();

        var broken = results.Single(r => r.FileName == "kapot.json");
        Assert.Equal(FolderImportStatus.Failed, broken.Status);
        Assert.Contains("Vraag 1: geen goed antwoord ingevuld.", broken.Messages);
        Assert.Single(await _quizzes.GetSummariesAsync());
    }

    [Fact]
    public async Task Full_attempt_flow_scores_and_lists_results()
    {
        WriteQuiz(QuizJson);
        var quizId = Assert.Single(await _folderImport.ImportAllAsync()).QuizId!.Value;

        var attemptId = await _attempts.StartAsync(quizId, false, false);
        var session = await _attempts.GetSessionAsync(attemptId);
        var question = Assert.Single(session!.Questions);
        var correct = question.Options.Single(o => o.IsCorrect);
        await _attempts.SelectOptionAsync(attemptId, question, correct.Id, true);
        await _attempts.SubmitAsync(attemptId);

        var result = await _attempts.GetSessionAsync(attemptId);
        Assert.True(result!.IsSubmitted);
        Assert.Equal(1, result.Attempt.Score);
        Assert.Equal(ResultState.Correct, result.Results[question.Id].State);

        var summary = Assert.Single(await _quizzes.GetSummariesAsync());
        Assert.Equal(1, summary.AttemptCount);
        Assert.Equal(100, summary.BestPercentage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _attempts.SelectOptionAsync(attemptId, question, correct.Id, false));
    }

    [Fact]
    public async Task Editor_rejects_question_without_correct_answer()
    {
        var created = await _quizzes.CreateAsync("Handmatig", null, false, false);
        var result = await _quizzes.SaveQuestionAsync(created.Id, Choice(QuestionType.SingleChoice, 1, false, false));

        Assert.False(result.Success);
        Assert.Contains("Geen goed antwoord ingevuld.", result.Errors);
    }

    [Fact]
    public async Task Reorder_and_duplicate_work()
    {
        var quizId = (await _quizzes.CreateAsync("Volgorde", null, false, false)).Id;
        foreach (var text in new[] { "A", "B", "C" })
        {
            var q = Choice(QuestionType.SingleChoice, 1, true, false);
            q.Id = 0;
            q.Text = text;
            Assert.True((await _quizzes.SaveQuestionAsync(quizId, q)).Success);
        }

        var quiz = (await _quizzes.GetAsync(quizId))!;
        var c = quiz.Questions.Single(q => q.Text == "C");
        await _quizzes.MoveQuestionAsync(quizId, c.Id, -1);
        Assert.Equal(["A", "C", "B"], (await _quizzes.GetAsync(quizId))!.Questions.Select(q => q.Text));

        var copyId = await _quizzes.DuplicateAsync(quizId);
        var copy = (await _quizzes.GetAsync(copyId!.Value))!;
        Assert.Equal("Volgorde (kopie)", copy.Title);
        Assert.Equal(["A", "C", "B"], copy.Questions.Select(q => q.Text));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class TestDbFactory(string path) : IDbContextFactory<ToetsrDbContext>
    {
        private readonly DbContextOptions<ToetsrDbContext> _options =
            new DbContextOptionsBuilder<ToetsrDbContext>().UseSqlite($"Data Source={path}").Options;

        public ToetsrDbContext CreateDbContext() => new(_options);
    }
}
