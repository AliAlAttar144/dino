namespace Toetsr.Services;

/// <summary>Instellingen uit appsettings.json (sectie "Toetsr"). Paden zijn relatief aan de app-map.</summary>
public class ToetsrOptions
{
    public string DataFolder { get; set; } = "App_Data";
    public string QuizFolder { get; set; } = "Toetsen";
    public string DatabaseFile { get; set; } = "toetsr.db";
    public bool ImportQuizFolderOnStartup { get; set; } = true;
    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    public long MaxImportBytes { get; set; } = 50 * 1024 * 1024;
}

/// <summary>Alle mappen die de app gebruikt, absoluut gemaakt t.o.v. de content root.</summary>
public class AppPaths
{
    public AppPaths(string contentRoot, ToetsrOptions options)
    {
        ContentRoot = contentRoot;
        DataFolder = Resolve(contentRoot, options.DataFolder);
        ImagesFolder = Path.Combine(DataFolder, "afbeeldingen");
        DatabasePath = Path.Combine(DataFolder, options.DatabaseFile);
        QuizFolder = Resolve(contentRoot, options.QuizFolder);
        QuizImagesFolder = Path.Combine(QuizFolder, "afbeeldingen");
    }

    public string ContentRoot { get; }
    public string DataFolder { get; }
    public string ImagesFolder { get; }
    public string DatabasePath { get; }
    public string QuizFolder { get; }
    public string QuizImagesFolder { get; }

    public void EnsureFolders()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(ImagesFolder);
        Directory.CreateDirectory(QuizFolder);
        Directory.CreateDirectory(QuizImagesFolder);
    }

    private static string Resolve(string root, string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
}
