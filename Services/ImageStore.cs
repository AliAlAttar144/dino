using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Toetsr.Data;

namespace Toetsr.Services;

/// <summary>Slaat afbeeldingen op schijf op. De bestandsnaam is gebaseerd op de inhoud (hash),
/// zodat dezelfde afbeelding nooit dubbel wordt opgeslagen. In de database staat alleen de bestandsnaam.</summary>
public partial class ImageStore(AppPaths paths, ToetsrOptions options, IDbContextFactory<ToetsrDbContext> dbFactory, ILogger<ImageStore> logger)
{
    public const string UrlPrefix = "/afbeeldingen";

    public static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    public static bool IsAllowedImage(string fileName) => AllowedExtensions.Contains(Path.GetExtension(fileName));

    public string AcceptAttribute => string.Join(",", AllowedExtensions);

    /// <summary>Slaat een afbeelding op en geeft de bestandsnaam terug.</summary>
    /// <exception cref="InvalidOperationException">Bij een ongeldig type of te groot bestand.</exception>
    public async Task<string> SaveAsync(Stream content, string originalFileName, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException($"'{originalFileName}' is geen ondersteunde afbeelding. Toegestaan: {string.Join(", ", AllowedExtensions)}.");
        if (ext == ".jpeg") ext = ".jpg";

        using var buffer = new MemoryStream();
        var block = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(block, ct)) > 0)
        {
            if (buffer.Length + read > options.MaxImageBytes)
                throw new InvalidOperationException($"'{originalFileName}' is groter dan {options.MaxImageBytes / 1024 / 1024} MB.");
            buffer.Write(block, 0, read);
        }

        var bytes = buffer.ToArray();
        var name = Convert.ToHexStringLower(SHA256.HashData(bytes))[..20] + ext;
        var target = Path.Combine(paths.ImagesFolder, name);
        if (!File.Exists(target))
        {
            Directory.CreateDirectory(paths.ImagesFolder);
            await File.WriteAllBytesAsync(target, bytes, ct);
        }
        return name;
    }

    public static string Url(string? fileName) =>
        string.IsNullOrEmpty(fileName) ? "" : $"{UrlPrefix}/{Uri.EscapeDataString(fileName)}";

    public string? GetFullPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName)) return null;
        var path = Path.Combine(paths.ImagesFolder, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Verwijdert afbeeldingen waar niets meer naar verwijst (ook niet in oude pogingen).</summary>
    public async Task<int> CleanupOrphansAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(paths.ImagesFolder)) return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        used.UnionWith(await db.Questions.Where(q => q.ImagePath != null).Select(q => q.ImagePath!).ToListAsync(ct));
        used.UnionWith(await db.AnswerOptions.Where(o => o.ImagePath != null).Select(o => o.ImagePath!).ToListAsync(ct));
        foreach (var snapshot in await db.Attempts.Select(a => a.SnapshotJson).ToListAsync(ct))
            foreach (Match m in SnapshotImageRegex().Matches(snapshot))
                used.Add(m.Groups[1].Value);

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(paths.ImagesFolder))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.') || used.Contains(name)) continue;
            try
            {
                File.Delete(file);
                removed++;
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Kon ongebruikte afbeelding {File} niet verwijderen", name);
            }
        }
        return removed;
    }

    [GeneratedRegex("\"ImagePath\"\\s*:\\s*\"([^\"]+)\"")]
    private static partial Regex SnapshotImageRegex();
}
