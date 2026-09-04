using Godot;
using GodotFileAccess = Godot.FileAccess;

namespace SoloVsMortal.Presentation;

public static class GodotSaveStore
{
    public static void Write(string path, string json) { using var file = GodotFileAccess.Open(path, GodotFileAccess.ModeFlags.Write) ?? throw new IOException($"Cannot open save path: {path}"); file.StoreString(json); }
    public static string? Read(string path) { if (!GodotFileAccess.FileExists(path)) return null; using var file = GodotFileAccess.Open(path, GodotFileAccess.ModeFlags.Read) ?? throw new IOException($"Cannot open save path: {path}"); return file.GetAsText(); }
}
