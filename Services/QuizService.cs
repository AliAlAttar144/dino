using Microsoft.EntityFrameworkCore;
using Toetsr.Data;
using Toetsr.Domain;

namespace Toetsr.Services;

public record QuizSummary(
    int Id,
    string Title,
    string? Description,
    int QuestionCount,
    double TotalPoints,
    int AttemptCount,
    double? BestPercentage,
    double? AveragePercentage,
    DateTime? LastAttemptAt,
    int? InProgressAttemptId,
    DateTime UpdatedAt);

/// <summary>Uitkomst van een opslagactie; bij fouten staan de meldingen in <see cref="Errors"/>.</summary>
public record SaveResult(bool Success, IReadOnlyList<string> Errors, int Id = 0)
{
    public static SaveResult Ok(int id = 0) => new(true, [], id);
    public static SaveResult Fail(IEnumerable<string> errors) => new(false, errors.ToList());
}

/// <summary>Toetsen en vragen aanmaken, bewerken, herordenen, dupliceren en verwijderen.</summary>
public class QuizService(IDbContextFactory<ToetsrDbContext> dbFactory, QuestionValidator validator)
{
    /// <summary>Gaat af nadat de inhoud van een toets is opgeslagen (argument: id van de toets).</summary>
    public event Func<int, Task>? Changed;

    /// <summary>Gaat af vlak voordat een toets wordt verwijderd (argument: id van de toets).</summary>
    public event Func<int, Task>? Deleting;

    private static async Task RaiseAsync(Func<int, Task>? handlers, int quizId)
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<int, Task>>())
            await handler(quizId);
    }
    public async Task<List<QuizSummary>> GetSummariesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quizzes = await db.Quizzes.AsNoTracking()
            .Select(q => new
            {
                q.Id,
                q.Title,
                q.Description,
                q.UpdatedAt,
                QuestionCount = q.Questions.Count,
                TotalPoints = q.Questions.Sum(x => (double?)x.Points) ?? 0,
            })
            .ToListAsync(ct);

        var attempts = await db.Attempts.AsNoTracking()
            .Select(a => new { a.Id, a.QuizId, a.Status, a.Score, a.MaxScore, a.SubmittedAt, a.UpdatedAt })
            .ToListAsync(ct);
        var byQuiz = attempts.ToLookup(a => a.QuizId);

        return quizzes.Select(q =>
        {
            var done = byQuiz[q.Id].Where(a => a.Status == AttemptStatus.Submitted).ToList();
            var pct = done.Select(a => a.MaxScore <= 0 ? 0 : a.Score / a.MaxScore * 100).ToList();
            var inProgress = byQuiz[q.Id].Where(a => a.Status == AttemptStatus.InProgress).OrderByDescending(a => a.UpdatedAt).FirstOrDefault();
            return new QuizSummary(
                q.Id, q.Title, q.Description, q.QuestionCount, q.TotalPoints,
                done.Count,
                pct.Count == 0 ? null : Math.Round(pct.Max(), 1),
                pct.Count == 0 ? null : Math.Round(pct.Average(), 1),
                done.Count == 0 ? null : done.Max(a => a.SubmittedAt),
                inProgress?.Id,
                q.UpdatedAt);
        })
        .OrderByDescending(s => s.LastAttemptAt ?? s.UpdatedAt)
        .ToList();
    }

    public async Task<Quiz?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quiz = await db.Quizzes.AsNoTracking()
            .Include(q => q.Questions).ThenInclude(q => q.Options)
            .AsSplitQuery()
            .FirstOrDefaultAsync(q => q.Id == id, ct);
        if (quiz is null) return null;
        Normalize(quiz);
        return quiz;
    }

    /// <summary>Sorteert vragen en opties op volgorde.</summary>
    public static void Normalize(Quiz quiz)
    {
        quiz.Questions = quiz.Questions.OrderBy(q => q.Order).ThenBy(q => q.Id).ToList();
        foreach (var q in quiz.Questions)
            q.Options = q.Options.OrderBy(o => o.GapNumber).ThenBy(o => o.Order).ThenBy(o => o.Id).ToList();
    }

    public async Task<SaveResult> CreateAsync(string title, string? description, bool shuffleQuestions, bool shuffleOptions, CancellationToken ct = default)
    {
        var errors = ValidateMeta(title);
        if (errors.Count > 0) return SaveResult.Fail(errors);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quiz = new Quiz
        {
            Title = title.Trim(),
            Description = Clean(description),
            ShuffleQuestions = shuffleQuestions,
            ShuffleOptions = shuffleOptions,
        };
        db.Quizzes.Add(quiz);
        await db.SaveChangesAsync(ct);
        return SaveResult.Ok(quiz.Id);
    }

    public async Task<SaveResult> UpdateMetaAsync(int id, string title, string? description, bool shuffleQuestions, bool shuffleOptions, CancellationToken ct = default)
    {
        var errors = ValidateMeta(title);
        if (errors.Count > 0) return SaveResult.Fail(errors);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quiz = await db.Quizzes.FindAsync([id], ct);
        if (quiz is null) return SaveResult.Fail(["Toets niet gevonden."]);
        quiz.Title = title.Trim();
        quiz.Description = Clean(description);
        quiz.ShuffleQuestions = shuffleQuestions;
        quiz.ShuffleOptions = shuffleOptions;
        quiz.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await RaiseAsync(Changed, id);
        return SaveResult.Ok(id);
    }

    /// <summary>Voegt een vraag toe (Id = 0) of werkt een bestaande vraag bij. Opties worden in hun geheel vervangen.</summary>
    public async Task<SaveResult> SaveQuestionAsync(int quizId, Question input, CancellationToken ct = default)
    {
        var question = Sanitize(input);
        var errors = validator.Validate(question);
        if (errors.Count > 0) return SaveResult.Fail(errors.Select(Capitalize));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quiz = await db.Quizzes.FindAsync([quizId], ct);
        if (quiz is null) return SaveResult.Fail(["Toets niet gevonden."]);

        Question entity;
        if (question.Id == 0)
        {
            var maxOrder = await db.Questions.Where(q => q.QuizId == quizId).MaxAsync(q => (int?)q.Order, ct) ?? -1;
            entity = new Question { QuizId = quizId, Order = maxOrder + 1 };
            db.Questions.Add(entity);
        }
        else
        {
            entity = await db.Questions.Include(q => q.Options).FirstOrDefaultAsync(q => q.Id == question.Id && q.QuizId == quizId, ct)
                     ?? throw new InvalidOperationException("Vraag niet gevonden.");
            db.AnswerOptions.RemoveRange(entity.Options);
            entity.Options = [];
        }

        entity.Type = question.Type;
        entity.Title = question.Title;
        entity.Text = question.Text;
        entity.ImagePath = question.ImagePath;
        entity.Points = question.Points;
        entity.Explanation = question.Explanation;
        foreach (var o in question.Options)
            entity.Options.Add(new AnswerOption
            {
                GapNumber = o.GapNumber,
                Order = o.Order,
                Text = o.Text,
                IsCorrect = o.IsCorrect,
                ImagePath = o.ImagePath,
            });

        quiz.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await RaiseAsync(Changed, quizId);
        return SaveResult.Ok(entity.Id);
    }

    public async Task DeleteQuestionAsync(int quizId, int questionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == questionId && q.QuizId == quizId, ct);
        if (question is null) return;
        db.Questions.Remove(question);
        await db.SaveChangesAsync(ct);
        await RenumberAsync(db, quizId, null, ct);
    }

    /// <summary>Zet de vragen in de opgegeven volgorde.</summary>
    public async Task ReorderQuestionsAsync(int quizId, IReadOnlyList<int> orderedIds, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await RenumberAsync(db, quizId, orderedIds, ct);
    }

    /// <summary>Verplaatst een vraag één plek omhoog (-1) of omlaag (+1).</summary>
    public async Task MoveQuestionAsync(int quizId, int questionId, int delta, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = await db.Questions.Where(q => q.QuizId == quizId).OrderBy(q => q.Order).ThenBy(q => q.Id).Select(q => q.Id).ToListAsync(ct);
        var index = ids.IndexOf(questionId);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ids.Count) return;
        (ids[index], ids[target]) = (ids[target], ids[index]);
        await RenumberAsync(db, quizId, ids, ct);
    }

    private async Task RenumberAsync(ToetsrDbContext db, int quizId, IReadOnlyList<int>? orderedIds, CancellationToken ct)
    {
        var questions = await db.Questions.Where(q => q.QuizId == quizId).OrderBy(q => q.Order).ThenBy(q => q.Id).ToListAsync(ct);
        if (orderedIds is not null)
        {
            var position = orderedIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
            questions = questions.OrderBy(q => position.TryGetValue(q.Id, out var p) ? p : int.MaxValue).ThenBy(q => q.Order).ToList();
        }
        for (var i = 0; i < questions.Count; i++) questions[i].Order = i;
        var quiz = await db.Quizzes.FindAsync([quizId], ct);
        if (quiz is not null) quiz.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await RaiseAsync(Changed, quizId);
    }

    public async Task<int?> DuplicateAsync(int id, CancellationToken ct = default)
    {
        var source = await GetAsync(id, ct);
        if (source is null) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var copy = new Quiz
        {
            Title = TrimTitle($"{source.Title} (kopie)"),
            Description = source.Description,
            ShuffleQuestions = source.ShuffleQuestions,
            ShuffleOptions = source.ShuffleOptions,
            Questions = source.Questions.Select(q => new Question
            {
                Order = q.Order,
                Type = q.Type,
                Title = q.Title,
                Text = q.Text,
                ImagePath = q.ImagePath,
                Points = q.Points,
                Explanation = q.Explanation,
                Options = q.Options.Select(o => new AnswerOption
                {
                    GapNumber = o.GapNumber,
                    Order = o.Order,
                    Text = o.Text,
                    IsCorrect = o.IsCorrect,
                    ImagePath = o.ImagePath,
                }).ToList(),
            }).ToList(),
        };
        db.Quizzes.Add(copy);
        await db.SaveChangesAsync(ct);
        await RaiseAsync(Changed, copy.Id);
        return copy.Id;
    }

    /// <summary>Verwijdert een toets met al zijn vragen en pogingen.</summary>
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quiz = await db.Quizzes.FindAsync([id], ct);
        if (quiz is null) return;
        await RaiseAsync(Deleting, id);
        db.Quizzes.Remove(quiz);
        foreach (var f in await db.ImportedFiles.Where(f => f.QuizId == id).ToListAsync(ct))
            f.QuizId = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Maakt een losse kopie van een vraag om in de editor te bewerken.</summary>
    public static Question Clone(Question q) => new()
    {
        Id = q.Id,
        QuizId = q.QuizId,
        Order = q.Order,
        Type = q.Type,
        Title = q.Title,
        Text = q.Text,
        ImagePath = q.ImagePath,
        Points = q.Points,
        Explanation = q.Explanation,
        Options = q.Options.Select(o => new AnswerOption
        {
            Id = o.Id,
            GapNumber = o.GapNumber,
            Order = o.Order,
            Text = o.Text,
            IsCorrect = o.IsCorrect,
            ImagePath = o.ImagePath,
        }).ToList(),
    };

    private static Question Sanitize(Question input)
    {
        var q = Clone(input);
        q.Title = Clean(q.Title);
        q.Text = q.Text?.Trim() ?? "";
        q.Explanation = Clean(q.Explanation);
        q.Points = Math.Round(q.Points, 2);
        q.Options = q.Options
            .Select(o => { o.Text = o.Text?.Trim() ?? ""; return o; })
            .Where(o => o.Text.Length > 0 || o.ImagePath is not null)
            .ToList();
        if (q.Type != QuestionType.Dropdown)
            foreach (var o in q.Options) o.GapNumber = null;
        else
            foreach (var o in q.Options) o.ImagePath = null;

        foreach (var group in q.Options.GroupBy(o => o.GapNumber))
        {
            var i = 0;
            foreach (var o in group) o.Order = i++;
        }
        return q;
    }

    private static List<string> ValidateMeta(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return ["Vul een titel in."];
        if (title.Trim().Length > 300) return ["De titel mag maximaal 300 tekens zijn."];
        return [];
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string TrimTitle(string s) => s.Length <= 300 ? s : s[..300];
    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..] + ".";
}
