using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Toetsr.Data;
using Toetsr.Domain;
using Toetsr.Services.QuestionTypes;

namespace Toetsr.Services;

public enum ResultState { Correct, Partial, Wrong, Unanswered }

public record QuestionResult(double Score, double MaxScore, ResultState State);

/// <summary>Alles wat de toets- en resultaatpagina nodig hebben van één poging.</summary>
public class AttemptSession
{
    public required Attempt Attempt { get; init; }
    public required string QuizTitle { get; init; }
    public required List<Question> Questions { get; init; }
    public required Dictionary<int, AttemptAnswer> Answers { get; init; }
    public Dictionary<int, QuestionResult> Results { get; init; } = [];

    public bool IsSubmitted => Attempt.Status == AttemptStatus.Submitted;

    public IReadOnlyList<int> Selected(int questionId) =>
        Answers.TryGetValue(questionId, out var a) ? a.SelectedOptionIds : [];

    public bool IsFlagged(int questionId) => Answers.TryGetValue(questionId, out var a) && a.Flagged;
}

public record AttemptListItem(int Id, int Number, AttemptStatus Status, DateTime StartedAt, DateTime? SubmittedAt, double Score, double MaxScore)
{
    public double Percentage => MaxScore <= 0 ? 0 : Math.Round(Score / MaxScore * 100, 1);
    public double Grade => Grading.ToGrade(Score, MaxScore);
    public TimeSpan? Duration => SubmittedAt - StartedAt;
}

/// <summary>Pogingen starten, antwoorden bewaren, inleveren en nakijken.</summary>
public class AttemptService(IDbContextFactory<ToetsrDbContext> dbFactory, QuestionTypeRegistry registry)
{
    private static readonly JsonSerializerOptions SnapshotJson = new() { WriteIndented = false };

    /// <summary>Start een nieuwe poging met een snapshot van de huidige toets.</summary>
    public async Task<int> StartAsync(int quizId, bool shuffleQuestions, bool shuffleOptions, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quiz = await db.Quizzes.AsNoTracking()
            .Include(q => q.Questions).ThenInclude(q => q.Options)
            .AsSplitQuery()
            .FirstOrDefaultAsync(q => q.Id == quizId, ct)
            ?? throw new InvalidOperationException("Toets niet gevonden.");
        QuizService.Normalize(quiz);
        if (quiz.Questions.Count == 0)
            throw new InvalidOperationException("Deze toets heeft nog geen vragen.");

        var included = quiz.Questions.Where(q => !q.Excluded).ToList();
        if (included.Count == 0)
            throw new InvalidOperationException("Alle vragen van deze toets zijn uitgesloten. Neem er minstens één weer op via Bewerken.");

        var questions = BuildSnapshot(included, shuffleQuestions, shuffleOptions, Random.Shared);
        var attempt = new Attempt
        {
            QuizId = quizId,
            Status = AttemptStatus.InProgress,
            MaxScore = Math.Round(questions.Sum(q => q.Points), 2),
            SnapshotJson = JsonSerializer.Serialize(questions, SnapshotJson),
            Answers = questions.Select(q => new AttemptAnswer { QuestionId = q.Id }).ToList(),
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return attempt.Id;
    }

    /// <summary>Kopieert de vragen en shufflet ze desgewenst. Opties van dropdowns worden binnen hun gat geshuffeld.</summary>
    public static List<Question> BuildSnapshot(IEnumerable<Question> source, bool shuffleQuestions, bool shuffleOptions, Random random)
    {
        var questions = source.Select(QuizService.Clone).ToList();
        if (shuffleQuestions) random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(questions));
        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            q.Order = i;
            if (!shuffleOptions) continue;
            q.Options = q.Options.GroupBy(o => o.GapNumber).OrderBy(g => g.Key).SelectMany(g =>
            {
                var list = g.ToArray();
                random.Shuffle(list);
                for (var j = 0; j < list.Length; j++) list[j].Order = j;
                return list;
            }).ToList();
        }
        return questions;
    }

    public async Task<AttemptSession?> GetSessionAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attempt = await db.Attempts.AsNoTracking()
            .Include(a => a.Answers)
            .Include(a => a.Quiz)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null) return null;

        var questions = JsonSerializer.Deserialize<List<Question>>(attempt.SnapshotJson, SnapshotJson) ?? [];
        var answers = attempt.Answers.ToDictionary(a => a.QuestionId);
        var session = new AttemptSession
        {
            Attempt = attempt,
            QuizTitle = attempt.Quiz?.Title ?? "Toets",
            Questions = questions,
            Answers = answers,
        };

        if (attempt.Status == AttemptStatus.Submitted)
            foreach (var q in questions)
                session.Results[q.Id] = Evaluate(q, answers.TryGetValue(q.Id, out var a) ? a : null);
        return session;
    }

    /// <summary>Verwerkt een klik op een optie en slaat de nieuwe selectie op.</summary>
    public async Task<AttemptAnswer> SelectOptionAsync(int attemptId, Question question, int optionId, bool selected, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (attempt, answer) = await LoadForUpdateAsync(db, attemptId, question.Id, ct);
        answer.SelectedOptionIds = registry.Get(question.Type).ApplySelection(question, answer.SelectedOptionIds, optionId, selected);
        attempt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return answer;
    }

    public async Task<AttemptAnswer> ClearAnswerAsync(int attemptId, int questionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (attempt, answer) = await LoadForUpdateAsync(db, attemptId, questionId, ct);
        answer.SelectedOptionIds = [];
        attempt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return answer;
    }

    public async Task<AttemptAnswer> SetFlagAsync(int attemptId, int questionId, bool flagged, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (attempt, answer) = await LoadForUpdateAsync(db, attemptId, questionId, ct);
        answer.Flagged = flagged;
        attempt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return answer;
    }

    private static async Task<(Attempt, AttemptAnswer)> LoadForUpdateAsync(ToetsrDbContext db, int attemptId, int questionId, CancellationToken ct)
    {
        var attempt = await db.Attempts.FirstOrDefaultAsync(a => a.Id == attemptId, ct)
                      ?? throw new InvalidOperationException("Poging niet gevonden.");
        if (attempt.Status != AttemptStatus.InProgress)
            throw new InvalidOperationException("Deze poging is al ingeleverd.");
        var answer = await db.AttemptAnswers.FirstOrDefaultAsync(a => a.AttemptId == attemptId && a.QuestionId == questionId, ct);
        if (answer is null)
        {
            answer = new AttemptAnswer { AttemptId = attemptId, QuestionId = questionId };
            db.AttemptAnswers.Add(answer);
        }
        return (attempt, answer);
    }

    /// <summary>Levert de poging in: berekent per vraag de score en het totaal.</summary>
    public async Task SubmitAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var attempt = await db.Attempts.Include(a => a.Answers).FirstOrDefaultAsync(a => a.Id == attemptId, ct)
                      ?? throw new InvalidOperationException("Poging niet gevonden.");
        if (attempt.Status == AttemptStatus.Submitted) return;

        var questions = JsonSerializer.Deserialize<List<Question>>(attempt.SnapshotJson, SnapshotJson) ?? [];
        var (total, max) = Score(questions, attempt.Answers);

        attempt.Score = total;
        attempt.MaxScore = max;
        attempt.Status = AttemptStatus.Submitted;
        attempt.SubmittedAt = DateTime.UtcNow;
        attempt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Berekent de score per antwoord (zet <see cref="AttemptAnswer.Score"/>) en geeft het totaal terug.</summary>
    public (double Total, double Max) Score(IReadOnlyList<Question> questions, ICollection<AttemptAnswer> answers)
    {
        var byQuestion = answers.ToDictionary(a => a.QuestionId);
        double total = 0;
        foreach (var q in questions)
        {
            if (!byQuestion.TryGetValue(q.Id, out var answer))
            {
                answer = new AttemptAnswer { QuestionId = q.Id };
                answers.Add(answer);
            }
            answer.Score = registry.Get(q.Type).Score(q, answer.SelectedOptionIds);
            total += answer.Score;
        }
        return (Math.Round(total, 2), Math.Round(questions.Sum(q => q.Points), 2));
    }

    public QuestionResult Evaluate(Question question, AttemptAnswer? answer)
    {
        var selected = answer?.SelectedOptionIds ?? [];
        var score = answer?.Score ?? 0;
        var state = !registry.Get(question.Type).IsAnswered(question, selected) ? ResultState.Unanswered
            : score >= question.Points - 0.0001 ? ResultState.Correct
            : score > 0 ? ResultState.Partial
            : ResultState.Wrong;
        return new QuestionResult(score, question.Points, state);
    }

    public bool IsAnswered(Question question, IReadOnlyCollection<int> selected) =>
        registry.Get(question.Type).IsAnswered(question, selected);

    public async Task<List<AttemptListItem>> GetAttemptsAsync(int quizId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Attempts.AsNoTracking()
            .Where(a => a.QuizId == quizId)
            .OrderBy(a => a.StartedAt)
            .Select(a => new { a.Id, a.Status, a.StartedAt, a.SubmittedAt, a.Score, a.MaxScore })
            .ToListAsync(ct);
        return rows.Select((a, i) => new AttemptListItem(a.Id, i + 1, a.Status, a.StartedAt, a.SubmittedAt, a.Score, a.MaxScore))
            .OrderByDescending(a => a.StartedAt)
            .ToList();
    }

    public async Task DeleteAttemptAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Attempts.Where(a => a.Id == attemptId).ExecuteDeleteAsync(ct);
    }
}
