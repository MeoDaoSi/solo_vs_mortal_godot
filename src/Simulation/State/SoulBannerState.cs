using SoloVsMortal.Data.Definitions;
using SoloVsMortal.Simulation.Rules;

namespace SoloVsMortal.Simulation.State;

public sealed class SoulBannerState
{
    internal SoulBannerState(string id, string definitionId, SoulBannerTier tier, int level, SoulBannerComputed computed) { Id = id; DefinitionId = definitionId; Tier = tier; Level = level; Computed = computed; }
    private readonly List<string> _boundSoulIds = [];
    public string Id { get; }
    public string DefinitionId { get; }
    public SoulBannerTier Tier { get; }
    public int Level { get; }
    public SoulBannerComputed Computed { get; }
    public IReadOnlyList<string> BoundSoulIds => _boundSoulIds.AsReadOnly();
    internal List<string> MutableBoundSoulIds => _boundSoulIds;
}
