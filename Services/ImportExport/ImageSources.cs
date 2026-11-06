using System.IO.Compression;

namespace Toetsr.Services.ImportExport;

/// <summary>Waar afbeeldingen bij een import vandaan komen (map, zip, ...).</summary>
public interface IImageSource
{
    bool Exists(string fileName);
    Stream Open(string fileName);
}

/// <summary>Zoekt afbeeldingen in één of meer mappen (eerste treffer wint).</summary>
public class FolderImageSource(params string[] folders) : IImageSource
{
    public bool Exists(string fileName) => Find(fileName) is not null;

    public Stream Open(string fileName) =>
        File.OpenRead(Find(fileName) ?? throw new FileNotFoundException($"Afbeelding '{fileName}' niet gevonden."));

    private string? Find(string fileName)
    {
        if (fileName != Path.GetFileName(fileName)) return null;
        foreach (var folder in folders)
        {
            var path = Path.Combine(folder, fileName);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}

/// <summary>Afbeeldingen uit een zip-bestand, op bestandsnaam (de map in de zip maakt niet uit).</summary>
public class InMemoryImageSource : IImageSource
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly IImageSource? _fallback;

    public InMemoryImageSource(IImageSource? fallback = null) => _fallback = fallback;

    public void Add(string fileName, byte[] content) => _files[fileName] = content;

    public bool Exists(string fileName) => _files.ContainsKey(fileName) || (_fallback?.Exists(fileName) ?? false);

    public Stream Open(string fileName) =>
        _files.TryGetValue(fileName, out var bytes) ? new MemoryStream(bytes, writable: false)
        : _fallback?.Open(fileName) ?? throw new FileNotFoundException($"Afbeelding '{fileName}' niet gevonden.");
}

/// <summary>Inhoud van een geüploade zip: de JSON-bestanden en de afbeeldingen.</summary>
public record ZipContents(List<(string Name, string Json)> JsonFiles, InMemoryImageSource Images, List<string> Warnings);

public static class ZipReader
{
    public static async Task<ZipContents> ReadAsync(Stream zipStream, long maxBytes, IImageSource? fallback, CancellationToken ct = default)
    {
        var jsonFiles = new List<(string, string)>();
        var images = new InMemoryImageSource(fallback);
        var warnings = new List<string>();
        long total = 0;

        await using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) || entry.FullName.Contains("__MACOSX") || entry.Name.StartsWith('.'))
                continue;

            total += entry.Length;
            if (total > maxBytes)
                throw new InvalidOperationException($"De zip is uitgepakt groter dan {maxBytes / 1024 / 1024} MB.");

            var ext = Path.GetExtension(entry.Name);
            if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                await using var s = await entry.OpenAsync(ct);
                using var reader = new StreamReader(s);
                jsonFiles.Add((entry.FullName, await reader.ReadToEndAsync(ct)));
            }
            else if (ImageStore.IsAllowedImage(entry.Name))
            {
                await using var s = await entry.OpenAsync(ct);
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms, ct);
                images.Add(entry.Name, ms.ToArray());
            }
            else
            {
                warnings.Add($"Bestand \"{entry.FullName}\" in de zip wordt genegeerd (geen JSON of afbeelding).");
            }
        }
        return new ZipContents(jsonFiles, images, warnings);
    }
}
