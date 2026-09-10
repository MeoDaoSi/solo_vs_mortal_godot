using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace SoloVsMortal.Presentation;

/// <summary>
/// Presentation-only contract for an Art export.  It deliberately has no lookup through the
/// prototype manifest: a missing V2.5 AssetId is a missing visual, never another species' art.
/// </summary>
public sealed record CanonicalAssetFrame(Rect2 Region, int DurationMs);
public sealed record CanonicalAssetLayering(int ZIndex, bool YSortEnabled);
public sealed record CanonicalAssetEntry(
    string AssetId,
    string AbsoluteFile,
    string Sha256,
    string Role,
    string Representation,
    int Rank,
    string Direction,
    string Clip,
    Vector2I FrameSize,
    IReadOnlyList<CanonicalAssetFrame> Frames,
    Vector2 Pivot,
    IReadOnlyList<string> Sockets,
    CanonicalAssetLayering Layering,
    bool TechnicalQa,
    bool VisualQa,
    bool InEngineQa,
    string ApprovalStatus);

public sealed class CanonicalAssetCatalog
{
    private static readonly HashSet<string> RootFields = new(StringComparer.Ordinal) { "schemaVersion", "catalogId", "catalogVersion", "complete", "sourceExport", "assets" };
    private static readonly HashSet<string> AssetFields = new(StringComparer.Ordinal) { "assetId", "relativeFile", "sha256", "role", "representation", "rank", "direction", "clip", "frameSize", "frames", "pivot", "sockets", "layering", "qa", "approvalStatus" };
    private static readonly HashSet<string> FrameFields = new(StringComparer.Ordinal) { "rect", "durationMs" };
    private static readonly HashSet<string> LayerFields = new(StringComparer.Ordinal) { "zIndex", "ySortEnabled" };
    private static readonly HashSet<string> QaFields = new(StringComparer.Ordinal) { "technical", "visual", "inEngine" };
    private readonly IReadOnlyDictionary<string, CanonicalAssetEntry> _assets;
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);

    private CanonicalAssetCatalog(string catalogVersion, bool complete, IReadOnlyDictionary<string, CanonicalAssetEntry> assets)
    {
        CatalogVersion = catalogVersion;
        Complete = complete;
        _assets = assets;
    }

    public string CatalogVersion { get; }
    public bool Complete { get; }
    public IReadOnlyDictionary<string, CanonicalAssetEntry> Assets => _assets;

    public static CanonicalAssetCatalog Load(string projectRoot, string manifestPath)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(projectRoot);
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            EnsureObjectFields(root, RootFields, "asset catalog");
            if (RequiredInt(root, "schemaVersion", 1) != 1) throw new InvalidDataException("Unsupported canonical asset catalog schema.");
            _ = RequiredText(root, "catalogId");
            var version = RequiredText(root, "catalogVersion");
            var complete = RequiredBool(root, "complete");
            _ = RequiredText(root, "sourceExport");
            var entries = RequiredArray(root, "assets");
            var assets = new Dictionary<string, CanonicalAssetEntry>(StringComparer.Ordinal);
            foreach (var item in entries.EnumerateArray())
            {
                EnsureObjectFields(item, AssetFields, "asset entry");
                var assetId = RequiredAssetId(item, "assetId");
                var relativeFile = RequiredSafeRelativePath(item, "relativeFile");
                var absoluteFile = Path.GetFullPath(Path.Combine(normalizedRoot, relativeFile));
                var rootPrefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar) ? normalizedRoot : normalizedRoot + Path.DirectorySeparatorChar;
                if (!absoluteFile.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Asset '{assetId}' resolves outside the project root.");
                if (!File.Exists(absoluteFile)) throw new InvalidDataException($"Asset '{assetId}' file is missing: {relativeFile}.");
                var sha256 = RequiredSha256(item, "sha256");
                var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(absoluteFile))).ToLowerInvariant();
                if (!string.Equals(actualHash, sha256, StringComparison.Ordinal)) throw new InvalidDataException($"Asset '{assetId}' hash does not match catalog provenance.");
                var frameSize = RequiredPair(item, "frameSize", 1, "frameSize");
                var pivot = RequiredPair(item, "pivot", 0, "pivot");
                var frames = ParseFrames(RequiredArray(item, "frames"), frameSize, assetId);
                var sockets = RequiredArray(item, "sockets").EnumerateArray().Select((value, index) => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new InvalidDataException($"Asset '{assetId}' sockets[{index}] must be non-empty text.")).ToArray();
                var layering = ParseLayering(RequiredObject(item, "layering"), assetId);
                var qa = RequiredObject(item, "qa"); EnsureObjectFields(qa, QaFields, $"asset '{assetId}' qa");
                var entry = new CanonicalAssetEntry(assetId, absoluteFile, sha256, RequiredText(item, "role"), RequiredText(item, "representation"), RequiredInt(item, "rank", 1), RequiredText(item, "direction"), RequiredText(item, "clip"), new Vector2I(frameSize.X, frameSize.Y), frames, new Vector2(pivot.X, pivot.Y), sockets, layering, RequiredBool(qa, "technical"), RequiredBool(qa, "visual"), RequiredBool(qa, "inEngine"), RequiredText(item, "approvalStatus"));
                if (!assets.TryAdd(assetId, entry)) throw new InvalidDataException($"Duplicate canonical asset ID '{assetId}'.");
            }
            return new CanonicalAssetCatalog(version, complete, new ReadOnlyDictionary<string, CanonicalAssetEntry>(assets));
        }
        catch (JsonException exception) { throw new InvalidDataException($"Cannot parse canonical asset catalog '{manifestPath}'.", exception); }
    }

    public bool TryGet(string assetId, out CanonicalAssetEntry entry) => _assets.TryGetValue(assetId, out entry!);

    public Texture2D Texture(CanonicalAssetEntry entry)
    {
        if (_textures.TryGetValue(entry.AssetId, out var texture)) return texture;
        var image = Image.LoadFromFile(entry.AbsoluteFile);
        if (image.IsEmpty()) throw new InvalidDataException($"Cannot load canonical image '{entry.AssetId}'.");
        foreach (var frame in entry.Frames)
            if (frame.Region.Position.X < 0 || frame.Region.Position.Y < 0 || frame.Region.End.X > image.GetWidth() || frame.Region.End.Y > image.GetHeight())
                throw new InvalidDataException($"Frame rectangle for '{entry.AssetId}' is outside its image.");
        texture = ImageTexture.CreateFromImage(image);
        _textures.Add(entry.AssetId, texture);
        return texture;
    }

    public SpriteFrames BuildFrames(CanonicalAssetEntry entry, string animationName = "static")
    {
        var frames = new SpriteFrames();
        frames.RemoveAnimation("default");
        var name = new StringName(animationName);
        frames.AddAnimation(name);
        frames.SetAnimationSpeed(name, 1000.0);
        frames.SetAnimationLoopMode(name, entry.Clip is "idle" or "move" ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);
        var texture = Texture(entry);
        foreach (var frame in entry.Frames)
            frames.AddFrame(name, new AtlasTexture { Atlas = texture, Region = frame.Region, FilterClip = true }, frame.DurationMs);
        return frames;
    }

    /// <summary>
    /// Composes explicitly named canonical clips into one presentation-only actor timeline.
    /// Every frame still comes through the same hash-validated catalog entry; this is not a
    /// fallback loader and callers must request the exact AssetIds they need.
    /// </summary>
    public SpriteFrames BuildFrames(IReadOnlyDictionary<string, CanonicalAssetEntry> animations)
    {
        if (animations.Count == 0) throw new ArgumentException("At least one canonical animation is required.", nameof(animations));
        var frames = new SpriteFrames();
        frames.RemoveAnimation("default");
        foreach (var (animationName, entry) in animations.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var name = new StringName(animationName);
            frames.AddAnimation(name);
            frames.SetAnimationSpeed(name, 1000.0);
            frames.SetAnimationLoopMode(name, entry.Clip is "idle" or "move" ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);
            var texture = Texture(entry);
            foreach (var frame in entry.Frames)
                frames.AddFrame(name, new AtlasTexture { Atlas = texture, Region = frame.Region, FilterClip = true }, frame.DurationMs);
        }
        return frames;
    }

    private static IReadOnlyList<CanonicalAssetFrame> ParseFrames(JsonElement values, Vector2I frameSize, string assetId)
    {
        if (values.GetArrayLength() == 0) throw new InvalidDataException($"Asset '{assetId}' must declare at least one frame.");
        var frames = new List<CanonicalAssetFrame>();
        foreach (var item in values.EnumerateArray())
        {
            EnsureObjectFields(item, FrameFields, $"asset '{assetId}' frame");
            var rect = RequiredQuad(item, "rect", 1, "rect");
            if (rect.Width != frameSize.X || rect.Height != frameSize.Y) throw new InvalidDataException($"Asset '{assetId}' frame rect must match frameSize.");
            frames.Add(new CanonicalAssetFrame(new Rect2(rect.X, rect.Y, rect.Width, rect.Height), RequiredInt(item, "durationMs", 1)));
        }
        return frames;
    }

    private static CanonicalAssetLayering ParseLayering(JsonElement item, string assetId)
    {
        EnsureObjectFields(item, LayerFields, $"asset '{assetId}' layering");
        return new CanonicalAssetLayering(RequiredInt(item, "zIndex", -1024), RequiredBool(item, "ySortEnabled"));
    }

    private static void EnsureObjectFields(JsonElement item, IReadOnlySet<string> allowed, string scope)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{scope} must be an object.");
        foreach (var property in item.EnumerateObject()) if (!allowed.Contains(property.Name)) throw new InvalidDataException($"Unknown field '{property.Name}' in {scope}.");
    }
    private static JsonElement RequiredObject(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : throw new InvalidDataException($"Missing object '{property}'.");
    private static JsonElement RequiredArray(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? value : throw new InvalidDataException($"Missing array '{property}'.");
    private static string RequiredText(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new InvalidDataException($"Missing text '{property}'.");
    private static bool RequiredBool(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new InvalidDataException($"Missing boolean '{property}'.");
    private static int RequiredInt(JsonElement item, string property, int minimum) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) && result >= minimum ? result : throw new InvalidDataException($"'{property}' must be an integer >= {minimum}.");
    private static string RequiredAssetId(JsonElement item, string property)
    {
        var value = RequiredText(item, property);
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z][a-z0-9_.-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new InvalidDataException($"Invalid AssetId '{value}'.");
        return value;
    }
    private static string RequiredSha256(JsonElement item, string property)
    {
        var value = RequiredText(item, property);
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[0-9a-f]{64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new InvalidDataException($"'{property}' must be a lowercase SHA-256.");
        return value;
    }
    private static string RequiredSafeRelativePath(JsonElement item, string property)
    {
        var value = RequiredText(item, property).Replace('\\', '/');
        if (Path.IsPathRooted(value) || value.Split('/').Any(segment => segment is "" or "." or "..")) throw new InvalidDataException($"'{property}' must be a project-relative path.");
        return value;
    }
    private static Vector2I RequiredPair(JsonElement item, string property, int minimum, string label)
    {
        var values = RequiredArray(item, property);
        if (values.GetArrayLength() != 2 || !values[0].TryGetInt32(out var x) || !values[1].TryGetInt32(out var y) || x < minimum || y < minimum) throw new InvalidDataException($"'{label}' must be two integers >= {minimum}.");
        return new Vector2I(x, y);
    }
    private static (int X, int Y, int Width, int Height) RequiredQuad(JsonElement item, string property, int minimum, string label)
    {
        var values = RequiredArray(item, property);
        if (values.GetArrayLength() != 4 || !values[0].TryGetInt32(out var x) || !values[1].TryGetInt32(out var y) || !values[2].TryGetInt32(out var width) || !values[3].TryGetInt32(out var height) || x < 0 || y < 0 || width < minimum || height < minimum) throw new InvalidDataException($"'{label}' must be [x, y, width, height] with non-negative origin.");
        return (x, y, width, height);
    }
}
