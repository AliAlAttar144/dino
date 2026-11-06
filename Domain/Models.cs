using System.Text.Json.Serialization;

namespace Toetsr.Domain;

/// <summary>De beschikbare vraagtypen. Voeg hier een waarde toe en registreer een
/// <see cref="Toetsr.Services.QuestionTypes.IQuestionTypeHandler"/> om een nieuw type te maken.</summary>
public enum QuestionType
{
    SingleChoice = 0,
    MultipleChoice = 1,
    Dropdown = 2,
}

public class Quiz
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool ShuffleQuestions { get; set; }
    public bool ShuffleOptions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Question> Questions { get; set; } = [];
    public List<Attempt> Attempts { get; set; } = [];
}

public class Question
{
    public int Id { get; set; }
    [JsonIgnore] public int QuizId { get; set; }
    [JsonIgnore] public Quiz? Quiz { get; set; }

    public int Order { get; set; }
    public QuestionType Type { get; set; }

    /// <summary>Optionele kop boven de vraag, bijv. "Hoofdstuk 2 - Routing".</summary>
    public string? Title { get; set; }

    /// <summary>Vraagtekst in Markdown. Bij dropdownvragen staan gaten als {{1}}, {{2}}, ...</summary>
    public string Text { get; set; } = "";
    public string? ImagePath { get; set; }
    public double Points { get; set; } = 1;
    public string? Explanation { get; set; }

    public List<AnswerOption> Options { get; set; } = [];
}

public class AnswerOption
{
    public int Id { get; set; }
    [JsonIgnore] public int QuestionId { get; set; }
    [JsonIgnore] public Question? Question { get; set; }

    /// <summary>Alleen bij dropdownvragen: bij welk gat ({{n}}) deze optie hoort.</summary>
    public int? GapNumber { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
    public string? ImagePath { get; set; }
}

public enum AttemptStatus
{
    InProgress = 0,
    Submitted = 1,
}

/// <summary>Een poging. Bevat een snapshot van de toets op het moment van starten,
/// zodat latere wijzigingen aan de toets oude pogingen niet veranderen.</summary>
public class Attempt
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz? Quiz { get; set; }

    public AttemptStatus Status { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }

    public double Score { get; set; }
    public double MaxScore { get; set; }

    /// <summary>JSON van de (eventueel geshuffelde) vragen zoals ze in deze poging getoond worden.</summary>
    public string SnapshotJson { get; set; } = "[]";

    public List<AttemptAnswer> Answers { get; set; } = [];

    public double Percentage => MaxScore <= 0 ? 0 : Math.Round(Score / MaxScore * 100, 1);
}

public class AttemptAnswer
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public Attempt? Attempt { get; set; }

    /// <summary>Id van de vraag in de snapshot (geen foreign key).</summary>
    public int QuestionId { get; set; }

    /// <summary>Gekozen optie-ids. Bij dropdowns één optie per gat.</summary>
    public List<int> SelectedOptionIds { get; set; } = [];
    public bool Flagged { get; set; }
    public double Score { get; set; }
}

/// <summary>Houdt bij welke bestanden uit de map Toetsen/ al geïmporteerd zijn.</summary>
public class ImportedFile
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string Hash { get; set; } = "";
    public int? QuizId { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
