using System.Text.Json;
using System.Text.RegularExpressions;
using SoloVsMortal.Data.Definitions;

namespace SoloVsMortal.Application.Persistence;

public enum LegacySaveRecognitionStatus
{
    RecognizedV1 = 1,
    RecognizedV2 = 2,
    RecognizedV3 = 3,
    RecognizedV4 = 4,
    RecognizedV5 = 5,
    RecognizedV6 = 6,
    Rejected = 99,
}

public sealed record LegacySaveRecognition(
    LegacySaveRecognitionStatus Status,
    int? Version,
    string? Error)
{
    public bool IsRecognized => Status is not LegacySaveRecognitionStatus.Rejected;
}

/// <summary>
/// Boundary for the prototype save namespace. Versions 1–6 are recognized as legacy records and are
/// validated for the legacy runtime only. This adapter never wraps a legacy payload as a V2.5 save and
/// never synthesizes missing V2.5 Density, Sync, receipt, or milestone state.
/// </summary>
public static class LegacySaveAdapter
{
    public static LegacySaveRecognition Recognize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new(LegacySaveRecognitionStatus.Rejected, null, "Legacy save is empty; the original file must be preserved.");
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(LegacySaveRecognitionStatus.Rejected, null, "Legacy save root must be an object; the original file must be preserved.");
            if (!root.TryGetProperty("version", out var versionValue) || !versionValue.TryGetInt32(out var version))
                return new(LegacySaveRecognitionStatus.Rejected, null, "Legacy save has no integral version; the original file must be preserved.");
            if (version is < 1 or > GameSaveCodec.CurrentVersion)
                return new(LegacySaveRecognitionStatus.Rejected, version, $"Legacy save version {version} is unsupported; the original file must be preserved.");
            return new((LegacySaveRecognitionStatus)version, version, null);
        }
        catch (JsonException exception)
        {
            return new(LegacySaveRecognitionStatus.Rejected, null, $"Legacy save JSON is malformed; the original file must be preserved: {exception.Message}");
        }
    }

    public static GameSaveData DeserializeForRuntime(string json, GameDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var recognition = Recognize(json);
        if (!recognition.IsRecognized)
            throw new InvalidDataException(recognition.Error ?? "Legacy save is unsupported; the original file must be preserved.");
        try
        {
            var save = GameSaveCodec.Deserialize(json);
            LegacySaveValidator.Validate(save, definitions);
            return save;
        }
        catch (InvalidDataException) { throw; }
        catch (JsonException exception) { throw new InvalidDataException("Legacy save JSON is malformed; the original file must be preserved.", exception); }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            throw new InvalidDataException($"Legacy save references unsupported data; the original file must be preserved: {exception.Message}", exception);
        }
    }
}

internal static class LegacySaveValidator
{
    private static readonly Regex IdPattern = new("^[A-Za-z0-9][A-Za-z0-9_.:-]*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void Validate(GameSaveData save, GameDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(definitions);
        if (save.Player is null) Reject("player is missing.");
        if (save.Souls is null) Reject("souls collection is missing.");
        if (save.SoulBanner is null) Reject("Soul Banner collection is missing.");
        ValidatePlayer(save.Player!);

        var soulIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var soul in save.Souls!)
        {
            if (soul is null) Reject("souls contains a null record.");
            RequireId(soul!.Id, "soul.id");
            if (!soulIds.Add(soul.Id)) Reject($"duplicate soul ID '{soul.Id}'.");
            if (soul.Origin is null) Reject($"soul '{soul.Id}' origin is missing.");
            RequireId(soul.SoulNatureId, $"soul '{soul.Id}' soulNatureId");
            RequireId(soul.Origin!.MonsterUid, $"soul '{soul.Id}' monsterUid");
            RequireId(soul.Origin.ConfigId, $"soul '{soul.Id}' configId");
            RequireId(soul.Origin.Species, $"soul '{soul.Id}' species");
            RequireText(soul.Origin.DisplayName, $"soul '{soul.Id}' displayName");
            RequireText(soul.Origin.RankKey, $"soul '{soul.Id}' rankKey");
            RequireText(soul.Origin.RankDisplayName, $"soul '{soul.Id}' rankDisplayName");
            RequireRange(soul.Level, 1, BalanceDefinition.MaximumLevel, $"soul '{soul.Id}' level");
            RequireNonNegative(soul.Xp, $"soul '{soul.Id}' xp");
            RequireRange(soul.Origin.Rank, 1, BalanceDefinition.MaximumRank, $"soul '{soul.Id}' origin rank");
            MonsterDefinition monster;
            try { monster = definitions.Monster(soul.Origin.ConfigId); }
            catch (KeyNotFoundException exception) { Reject($"soul '{soul.Id}' references unknown monster '{soul.Origin.ConfigId}'.", exception); return; }
            if (!definitions.SoulNatures.Natures.ContainsKey(soul.SoulNatureId!))
                Reject($"soul '{soul.Id}' references unknown legacy Soul Nature '{soul.SoulNatureId}'.");
            if (!string.Equals(monster.SoulNatureId, soul.SoulNatureId, StringComparison.Ordinal))
                Reject($"soul '{soul.Id}' Soul Nature does not match monster '{monster.Id}'.");
            if (!string.Equals(monster.SpeciesId, soul.Origin.Species, StringComparison.Ordinal))
                Reject($"soul '{soul.Id}' species does not match monster '{monster.Id}'.");
        }

        var bannerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var banner in save.SoulBanner!)
        {
            if (banner is null) Reject("Soul Banner collection contains a null record.");
            RequireId(banner!.Id, "Soul Banner ID");
            if (!bannerIds.Add(banner.Id)) Reject($"duplicate Soul Banner ID '{banner.Id}'.");
            if (banner.BoundSouls is null) Reject($"Soul Banner '{banner.Id}' bindings are missing.");
            if (banner.BoundSouls!.Any(boundId => !soulIds.Contains(boundId))) Reject($"Soul Banner '{banner.Id}' references an unknown soul.");
            if (banner.BoundSouls!.Count != banner.BoundSouls.Distinct(StringComparer.Ordinal).Count()) Reject($"Soul Banner '{banner.Id}' contains duplicate bindings.");
            if (banner.Tier is not ("NHAP_MON" or "LINH_NGOC" or "THIEN_LINH")) Reject($"Soul Banner '{banner.Id}' has unsupported tier '{banner.Tier}'.");
            RequireRange(banner.Level, 1, definitions.SoulBanner(banner.Tier switch { "NHAP_MON" => SoulBannerTier.NhapMon, "LINH_NGOC" => SoulBannerTier.LinhNgoc, "THIEN_LINH" => SoulBannerTier.ThienLinh, _ => SoulBannerTier.NhapMon }).MaximumLevel, $"Soul Banner '{banner.Id}' level");
        }

        ValidateProgression(save.Progression);
        ValidateRuntime(save.SoulRuntime, soulIds);
        ValidatePoints(save.Essence, "essence");
        ValidatePoints(save.Bloodline, "bloodline");
        ValidatePossession(save.Possession, save.Souls!, save.SoulBanner!, save.SoulRuntime, soulIds, definitions);
        ValidateWorld(save.World, definitions);
    }

    private static void ValidatePlayer(PlayerSaveData player)
    {
        RequireFinite(player.CurrentHp, "player.currentHp");
        RequireFinite(player.MaxHp, "player.maxHp");
        if (player.MaxHp <= 0 || player.CurrentHp < 0 || player.CurrentHp > player.MaxHp) Reject("player HP is outside its valid range.");
        if (player.Level is { } level) RequireRange(level, 1, BalanceDefinition.MaximumLevel, "player.level");
        if (player.Rank is { } rank) RequireRange(rank, 1, BalanceDefinition.MaximumRank, "player.rank");
        if (player.Xp is { } xp) RequireNonNegative(xp, "player.xp");
    }

    private static void ValidateProgression(ProgressionSaveData? progression)
    {
        if (progression is null) return;
        if (progression.Inventory is null || progression.PlayerBuffs is null) Reject("progression collections are missing.");
        foreach (var item in progression.Inventory!)
        {
            RequireId(item.Key, "inventory item ID");
            if (item.Value < 0 || !IsKnownItem(item.Key)) Reject($"unsupported inventory item '{item.Key}'.");
        }
        var seenBuffs = new HashSet<PillId>();
        foreach (var buff in progression.PlayerBuffs!)
        {
            if (buff is null) Reject("progression contains a null player buff.");
            var pill = BalanceDefinition.PillByStableId(buff!.PillId);
            if (!seenBuffs.Add(pill.Id)) Reject($"duplicate player buff '{buff.PillId}'.");
            RequireFinite(buff.RemainingSeconds, $"player buff '{buff.PillId}' remainingSeconds");
            if (pill.DurationSeconds is null || buff.RemainingSeconds <= 0 || buff.RemainingSeconds > pill.DurationSeconds.Value)
                Reject($"player buff '{buff.PillId}' duration is outside its authored range.");
        }
    }

    private static void ValidateRuntime(IReadOnlyList<SoulRuntimeSaveData>? runtime, IReadOnlySet<string> soulIds)
    {
        if (runtime is null) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in runtime)
        {
            if (entry is null) Reject("soulRuntime contains a null record.");
            RequireId(entry!.SoulId, "soulRuntime.soulId");
            if (!soulIds.Contains(entry.SoulId) || !seen.Add(entry.SoulId)) Reject($"invalid or duplicate soulRuntime ID '{entry.SoulId}'.");
            RequireFinite(entry.RecoverySeconds, $"soulRuntime '{entry.SoulId}' recoverySeconds");
            if (entry.RecoverySeconds < 0) Reject($"soulRuntime '{entry.SoulId}' recoverySeconds cannot be negative.");
            if (entry.RecoveryDurationSeconds is { } duration)
            {
                RequireFinite(duration, $"soulRuntime '{entry.SoulId}' recoveryDurationSeconds");
                if (duration <= 0 || entry.RecoverySeconds > duration) Reject($"soulRuntime '{entry.SoulId}' recovery duration is invalid.");
            }
        }
    }

    private static void ValidatePoints(PointProgressSaveData? points, string label)
    {
        if (points is null) return;
        if (points.Points is null) Reject($"{label}.points is missing.");
        foreach (var item in points.Points!)
        {
            RequireId(item.Key, $"{label} point ID");
            if (item.Value < 0) Reject($"{label} point '{item.Key}' cannot be negative.");
        }
    }

    private static void ValidatePossession(PossessionRuntimeSaveData? possession, IReadOnlyList<OwnedSoulSaveData> souls, IReadOnlyList<SoulBannerSaveData> banners, IReadOnlyList<SoulRuntimeSaveData>? runtime, IReadOnlySet<string> soulIds, GameDefinitions definitions)
    {
        if (possession is null) return;
        RequireId(possession.SoulId, "possession.soulId");
        RequireId(possession.ProfileId, "possession.profileId");
        if (!soulIds.Contains(possession.SoulId)) Reject($"possession references unknown soul '{possession.SoulId}'.");
        if (runtime?.Any(entry => string.Equals(entry.SoulId, possession.SoulId, StringComparison.Ordinal)) == true)
            Reject($"possession Soul '{possession.SoulId}' is also marked dispersed.");
        if (!banners.Any(banner => banner.Tier == "NHAP_MON" && banner.BoundSouls.Contains(possession.SoulId)))
            Reject($"possession Soul '{possession.SoulId}' is not bound to the starter Soul Banner.");
        RequireFinite(possession.RemainingSeconds, "possession.remainingSeconds");
        if (possession.RemainingSeconds <= 0) Reject("possession.remainingSeconds must be positive.");
        var savedSoul = souls.First(soul => string.Equals(soul.Id, possession.SoulId, StringComparison.Ordinal));
        if (!definitions.SoulNatures.Natures.TryGetValue(savedSoul.SoulNatureId!, out var nature) || nature.PossessionProfileId is null)
            Reject($"possession Soul '{possession.SoulId}' has no supported possession profile.");
        if (!string.Equals(nature!.PossessionProfileId, possession.ProfileId, StringComparison.Ordinal))
            Reject($"possession profile does not match Soul '{possession.SoulId}'.");
        if (!definitions.SoulNatures.PossessionProfiles.TryGetValue(possession.ProfileId, out var profile))
            Reject($"possession references unknown profile '{possession.ProfileId}'.");
        if (possession.RemainingSeconds > profile!.DurationSeconds)
            Reject("possession.remainingSeconds exceeds the authored profile duration.");
    }

    private static void ValidateWorld(WorldSaveData? world, GameDefinitions definitions)
    {
        if (world is null) return;
        if (world.DestroyedObjectIds is null) Reject("world.destroyedObjectIds is missing.");
        var objects = definitions.Maps.Values.SelectMany(map => map.Objects.Keys).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in world.DestroyedObjectIds!)
        {
            RequireId(id, "world.destroyedObjectId");
            if (!objects.Contains(id) || !seen.Add(id)) Reject($"world references an unknown or duplicate object '{id}'.");
        }
        if (world.CurrentRegionId is not null && !definitions.WorldMap.Regions.ContainsKey(world.CurrentRegionId))
            Reject($"world references unknown region '{world.CurrentRegionId}'.");
    }

    private static bool IsKnownItem(string id) =>
        id is "SPIRIT_CRYSTAL" or "BEAST_CORE" || BalanceDefinition.Pills.Values.Any(pill => pill.StableId == id);

    private static void RequireId(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !IdPattern.IsMatch(value)) Reject($"{label} is missing or invalid.");
    }

    private static void RequireText(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) Reject($"{label} is missing.");
    }

    private static void RequireFinite(double value, string label)
    {
        if (!double.IsFinite(value)) Reject($"{label} must be finite.");
    }

    private static void RequireNonNegative(int value, string label)
    {
        if (value < 0) Reject($"{label} cannot be negative.");
    }

    private static void RequireRange(int value, int minimum, int maximum, string label)
    {
        if (value < minimum || value > maximum) Reject($"{label} is outside [{minimum}..{maximum}].");
    }

    private static void Reject(string message) => throw new InvalidDataException($"Legacy save rejected; original file must be preserved: {message}");
    private static void Reject(string message, Exception innerException) => throw new InvalidDataException($"Legacy save rejected; original file must be preserved: {message}", innerException);
}
