using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Data.Definitions;

public sealed record NamedDefinition(string Id, string DisplayName);
public sealed record SoulCostProfileDefinition(string Id, string DisplayName, int Cost);
public sealed record DevourXpProfileDefinition(string Id, string DisplayName, double BaseXp, double XpPerSoulLevel, double XpPerOriginRank);
public sealed record ModifierMilestoneDefinition(int RequiredPoints, string DisplayName, StatModifiers Modifiers);
public sealed record ModifierProfileDefinition(string Id, string DisplayName, IReadOnlyList<ModifierMilestoneDefinition> Milestones);
public sealed record PossessionProfileDefinition(string Id, string DisplayName, double DurationSeconds, double CooldownSeconds, StatModifiers Modifiers, IReadOnlyList<string> CapabilityIds);
public sealed record DevourDefinition(string? XpProfileId, string? EssenceProfileId, double? EssenceContribution, string? BloodlineProfileId, double? BloodlineContribution);
public sealed record SoulNatureDefinition(string Id, string DisplayName, IReadOnlyList<string> TraitIds, string SoulCostProfileId, DevourDefinition? Devour, string? PossessionProfileId);

public sealed record SoulNatureDefinitions(
    int SchemaVersion,
    IReadOnlyDictionary<string, NamedDefinition> Traits,
    IReadOnlyDictionary<string, NamedDefinition> Capabilities,
    IReadOnlyDictionary<string, SoulCostProfileDefinition> CostProfiles,
    IReadOnlyDictionary<string, DevourXpProfileDefinition> DevourXpProfiles,
    IReadOnlyDictionary<string, ModifierProfileDefinition> EssenceProfiles,
    IReadOnlyDictionary<string, ModifierProfileDefinition> BloodlineProfiles,
    IReadOnlyDictionary<string, PossessionProfileDefinition> PossessionProfiles,
    IReadOnlyDictionary<string, SoulNatureDefinition> Natures);

public static partial class SoulNatureDefinitionLoader
{
    private static readonly HashSet<string> ModifierNames =
    ["hpPercent", "atkPercent", "defPercent", "speedPercent", "hpFlat", "atkFlat", "defFlat", "speedFlat", "scalar"];

    public static SoulNatureDefinitions Load(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = Object(document.RootElement, "Soul Nature manifest");
            var schemaVersion = Integer(root, "schemaVersion", 1);
            var traits = NamedCollection(root, "traits");
            var capabilities = NamedCollection(root, "capabilities");
            var costs = Unique(root, "costProfiles", item => new SoulCostProfileDefinition(Id(item), Text(item, "displayName"), Integer(item, "cost", 1)));
            var xpProfiles = Unique(root, "devourXpProfiles", item => new DevourXpProfileDefinition(Id(item), Text(item, "displayName"), Number(item, "baseXp", 0), Number(item, "xpPerSoulLevel", 0), Number(item, "xpPerOriginRank", 0)));
            var essence = Unique(root, "essenceProfiles", ParseModifierProfile);
            var bloodlines = Unique(root, "bloodlineProfiles", ParseModifierProfile);
            var possessions = Unique(root, "possessionProfiles", ParsePossession);
            var natures = Unique(root, "natures", ParseNature);

            foreach (var possession in possessions.Values)
                foreach (var capabilityId in possession.CapabilityIds)
                    RequireReference(capabilities, capabilityId, $"possession '{possession.Id}' capability");

            foreach (var nature in natures.Values)
            {
                foreach (var traitId in nature.TraitIds) RequireReference(traits, traitId, $"nature '{nature.Id}' trait");
                RequireReference(costs, nature.SoulCostProfileId, $"nature '{nature.Id}' cost profile");
                if (nature.Devour is { } devour)
                {
                    OptionalReference(xpProfiles, devour.XpProfileId, $"nature '{nature.Id}' XP profile");
                    OptionalReference(essence, devour.EssenceProfileId, $"nature '{nature.Id}' Essence profile");
                    OptionalReference(bloodlines, devour.BloodlineProfileId, $"nature '{nature.Id}' Bloodline profile");
                    RequirePaired(devour.EssenceProfileId, devour.EssenceContribution, "Essence", nature.Id);
                    RequirePaired(devour.BloodlineProfileId, devour.BloodlineContribution, "Bloodline", nature.Id);
                }
                OptionalReference(possessions, nature.PossessionProfileId, $"nature '{nature.Id}' Possession profile");
            }

            return new SoulNatureDefinitions(schemaVersion, traits, capabilities, costs, xpProfiles, essence, bloodlines, possessions, natures);
        }
        catch (DefinitionException) { throw; }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new DefinitionException($"Cannot load Soul Nature definitions from '{path}'.", exception);
        }
    }

    private static ModifierProfileDefinition ParseModifierProfile(JsonElement item)
    {
        var milestonesElement = Array(item, "milestones");
        var milestones = new List<ModifierMilestoneDefinition>();
        var previous = 0;
        foreach (var milestone in milestonesElement.EnumerateArray())
        {
            var required = Integer(milestone, "requiredPoints", 1);
            if (required <= previous) throw new DefinitionException("Modifier milestones must have strictly ascending thresholds.");
            previous = required;
            milestones.Add(new ModifierMilestoneDefinition(required, Text(milestone, "displayName"), Modifiers(ObjectProperty(milestone, "modifiers"))));
        }
        if (milestones.Count == 0) throw new DefinitionException("Modifier profile must contain milestones.");
        return new ModifierProfileDefinition(Id(item), Text(item, "displayName"), milestones.AsReadOnly());
    }

    private static PossessionProfileDefinition ParsePossession(JsonElement item)
    {
        var capabilities = StringArray(item, "capabilityIds", allowEmpty: true);
        EnsureUnique(capabilities, "Possession capability ID");
        return new PossessionProfileDefinition(Id(item), Text(item, "displayName"), Number(item, "durationSeconds", 0.1), Number(item, "cooldownSeconds", 0), Modifiers(ObjectProperty(item, "modifiers")), capabilities);
    }

    private static SoulNatureDefinition ParseNature(JsonElement item)
    {
        var traits = StringArray(item, "traitIds", allowEmpty: false);
        EnsureUnique(traits, "Soul trait ID");
        DevourDefinition? devour = null;
        if (item.TryGetProperty("devour", out var value))
        {
            var input = Object(value, "devour");
            devour = new DevourDefinition(OptionalId(input, "xpProfileId"), OptionalId(input, "essenceProfileId"), OptionalNumber(input, "essenceContribution", 1), OptionalId(input, "bloodlineProfileId"), OptionalNumber(input, "bloodlineContribution", 1));
        }
        return new SoulNatureDefinition(Id(item), Text(item, "displayName"), traits, Id(item, "soulCostProfileId"), devour, OptionalId(item, "possessionProfileId"));
    }

    private static StatModifiers Modifiers(JsonElement item)
    {
        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
        {
            if (!ModifierNames.Contains(property.Name) || property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetDouble(out var number) || !double.IsFinite(number))
                throw new DefinitionException($"Invalid modifier '{property.Name}'.");
            values.Add(property.Name, number);
        }
        double Get(string name, double fallback = 0) => values.TryGetValue(name, out var value) ? value : fallback;
        return new StatModifiers(Get("hpPercent"), Get("atkPercent"), Get("defPercent"), Get("speedPercent"), Get("hpFlat"), Get("atkFlat"), Get("defFlat"), Get("speedFlat"), Get("scalar", 1));
    }

    private static IReadOnlyDictionary<string, NamedDefinition> NamedCollection(JsonElement root, string property) =>
        Unique(root, property, item => new NamedDefinition(Id(item), Text(item, "displayName")));

    private static IReadOnlyDictionary<string, T> Unique<T>(JsonElement root, string property, Func<JsonElement, T> parse) where T : notnull
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in Array(root, property).EnumerateArray())
        {
            var parsed = parse(Object(item, property));
            var id = parsed switch
            {
                NamedDefinition value => value.Id,
                SoulCostProfileDefinition value => value.Id,
                DevourXpProfileDefinition value => value.Id,
                ModifierProfileDefinition value => value.Id,
                PossessionProfileDefinition value => value.Id,
                SoulNatureDefinition value => value.Id,
                _ => throw new InvalidOperationException("Unsupported definition type."),
            };
            if (!result.TryAdd(id, parsed)) throw new DefinitionException($"Duplicate ID '{id}' in '{property}'.");
        }
        return new ReadOnlyDictionary<string, T>(result);
    }

    private static string Id(JsonElement item, string property = "id")
    {
        var id = Text(item, property);
        if (!StableIdRegex().IsMatch(id)) throw new DefinitionException($"'{property}' must be a stable uppercase English ID: {id}.");
        return id;
    }

    private static string? OptionalId(JsonElement item, string property) => item.TryGetProperty(property, out _) ? Id(item, property) : null;
    private static JsonElement Object(JsonElement item, string label) => item.ValueKind == JsonValueKind.Object ? item : throw new DefinitionException($"{label} must be an object.");
    private static JsonElement ObjectProperty(JsonElement item, string property) => item.TryGetProperty(property, out var value) ? Object(value, property) : throw new DefinitionException($"Missing object '{property}'.");
    private static JsonElement Array(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? value : throw new DefinitionException($"'{property}' must be an array.");
    private static string Text(JsonElement item, string property) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()) ? value.GetString()! : throw new DefinitionException($"'{property}' must be a non-empty string.");
    private static double Number(JsonElement item, string property, double minimum) => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= minimum ? number : throw new DefinitionException($"'{property}' must be a finite number >= {minimum}.");
    private static double? OptionalNumber(JsonElement item, string property, double minimum) => item.TryGetProperty(property, out _) ? Number(item, property, minimum) : null;
    private static int Integer(JsonElement item, string property, int minimum) { var number = Number(item, property, minimum); return number == System.Math.Truncate(number) && number <= int.MaxValue ? (int)number : throw new DefinitionException($"'{property}' must be an integer."); }
    private static IReadOnlyList<string> StringArray(JsonElement item, string property, bool allowEmpty) { var values = Array(item, property).EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "").ToArray(); if ((!allowEmpty && values.Length == 0) || values.Any(value => !StableIdRegex().IsMatch(value))) throw new DefinitionException($"'{property}' must contain stable IDs."); return System.Array.AsReadOnly(values); }
    private static void EnsureUnique(IReadOnlyList<string> values, string label) { if (values.Distinct(StringComparer.Ordinal).Count() != values.Count) throw new DefinitionException($"Duplicate {label}."); }
    private static void RequireReference<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> values, TKey key, string label) where TKey : notnull { if (!values.ContainsKey(key)) throw new DefinitionException($"Unknown {label} '{key}'."); }
    private static void OptionalReference<T>(IReadOnlyDictionary<string, T> values, string? key, string label) { if (key is not null) RequireReference(values, key, label); }
    private static void RequirePaired(string? profile, double? contribution, string label, string nature) { if ((profile is null) != (contribution is null)) throw new DefinitionException($"{label} profile and contribution must be configured together in '{nature}'."); }

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex StableIdRegex();
}
