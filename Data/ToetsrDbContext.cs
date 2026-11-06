using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Toetsr.Domain;

namespace Toetsr.Data;

public class ToetsrDbContext(DbContextOptions<ToetsrDbContext> options) : DbContext(options)
{
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<AttemptAnswer> AttemptAnswers => Set<AttemptAnswer>();
    public DbSet<ImportedFile> ImportedFiles => Set<ImportedFile>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Quiz>(e =>
        {
            e.Property(q => q.Title).HasMaxLength(300).IsRequired();
            e.HasMany(q => q.Questions).WithOne(q => q.Quiz).HasForeignKey(q => q.QuizId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(q => q.Attempts).WithOne(a => a.Quiz).HasForeignKey(a => a.QuizId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Question>(e =>
        {
            e.Property(q => q.Type).HasConversion<string>().HasMaxLength(40);
            e.HasMany(q => q.Options).WithOne(o => o.Question).HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(q => new { q.QuizId, q.Order });
        });

        b.Entity<Attempt>(e =>
        {
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            e.Ignore(a => a.Percentage);
            e.HasMany(a => a.Answers).WithOne(a => a.Attempt).HasForeignKey(a => a.AttemptId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.QuizId, a.Status });
        });

        b.Entity<AttemptAnswer>(e =>
        {
            e.Property(a => a.SelectedOptionIds)
                .HasConversion(
                    v => string.Join(',', v),
                    v => string.IsNullOrEmpty(v) ? new List<int>() : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList())
                .Metadata.SetValueComparer(new ValueComparer<List<int>>(
                    (x, y) => x!.SequenceEqual(y!),
                    v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i)),
                    v => v.ToList()));
            e.HasIndex(a => new { a.AttemptId, a.QuestionId }).IsUnique();
        });

        b.Entity<ImportedFile>(e =>
        {
            e.HasIndex(f => f.FileName).IsUnique();
        });
    }
}
