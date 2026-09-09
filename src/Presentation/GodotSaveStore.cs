using Godot;
using SoloVsMortal.Application.Persistence.V25;

namespace SoloVsMortal.Presentation;

public static class GodotSaveStore
{
    public static void Write(string path, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(json);
        var physicalPath = ProjectSettings.GlobalizePath(path);
        DurableSaveFiles.CommitText(physicalPath, json);
    }

    public static string? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var physicalPath = ProjectSettings.GlobalizePath(path);
        return File.Exists(physicalPath) ? File.ReadAllText(physicalPath) : null;
    }
}
