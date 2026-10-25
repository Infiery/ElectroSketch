using System.IO;
using System.Text.Json;

namespace ElectroSketch.Core;

public sealed class GlobalNotesData
{
    public string Text { get; set; } = "";
    public List<NoteImageItem> Images { get; set; } = [];
}

public sealed class NoteImageItem
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "Image";
}

public static class NotesStore
{
    private static readonly string Root = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ElectroSketch");
    private static readonly string ImagesRoot = System.IO.Path.Combine(Root, "NotesImages");
    private static readonly string NotesPath = System.IO.Path.Combine(Root, "notes.json");

    public static GlobalNotesData Load()
    {
        try
        {
            if (!File.Exists(NotesPath)) return new GlobalNotesData();
            var data = JsonSerializer.Deserialize<GlobalNotesData>(File.ReadAllText(NotesPath), DocumentJson.Options) ?? new GlobalNotesData();
            data.Images ??= [];
            data.Images.RemoveAll(item => string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path));
            return data;
        }
        catch
        {
            return new GlobalNotesData();
        }
    }

    public static void Save(GlobalNotesData data)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(NotesPath, JsonSerializer.Serialize(data, DocumentJson.Options));
    }

    public static IReadOnlyList<NoteImageItem> ImportImages(IEnumerable<string> paths)
    {
        Directory.CreateDirectory(ImagesRoot);
        var imported = new List<NoteImageItem>();
        foreach (var source in paths.Where(File.Exists))
        {
            var extension = System.IO.Path.GetExtension(source);
            var destination = System.IO.Path.Combine(ImagesRoot, $"{Guid.NewGuid():N}{extension}");
            File.Copy(source, destination, false);
            imported.Add(new NoteImageItem { Path = destination, Name = System.IO.Path.GetFileName(source) });
        }
        return imported;
    }
}
