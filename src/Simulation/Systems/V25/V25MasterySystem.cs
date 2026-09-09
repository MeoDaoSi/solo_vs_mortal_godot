using SoloVsMortal.Core.Events;
using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Events;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Simulation.Systems;

namespace SoloVsMortal.Simulation.Systems.V25;

/// <summary>One immutable mastery ledger row. Amount is stored in micro-units for all
/// distance, time, damage and healing metrics; MeaningfulUseCount uses one micro-unit per use.
/// The row is the audit identity for a combat release and is never recomputed after commit.</summary>
public sealed record V25MasteryCredit(
    string AwardId,
    string SkillId,
    string Metric,
    string CastId,
    string TargetLifeUid,
    long AmountMicro,
    long Tick,
    string SourceVersion);

public sealed record V25MasterySkillState(
    string SkillId,
    string Metric,
    int PromotedRank,
    long ProgressMicro,
    IReadOnlyList<V25MasteryCredit> Credits);

/// <summary>Per target-life budget. A monster returning home or a failed boss attempt never
/// resets this budget; only a new authored life identity creates a new row.</summary>
public sealed record V25MasteryTargetBudget(
    string TargetLifeUid,
    string EncounterId,
    long SpawnMaxHpMicro,
    long RemainingMicro,
    bool RewardEligible,
    V25EncounterType EncounterType);

public sealed record V25MasteryHealingDebt(string DebtId, long AmountMicro, long CreatedTick, long ExpireTick);

public sealed record V25MasterySnapshot(
    IReadOnlyList<V25MasterySkillState> Skills,
    IReadOnlyList<V25MasteryTargetBudget> TargetBudgets,
    IReadOnlyList<V25MasteryHealingDebt> HealingDebts);

/// <summary>
/// Credits mastery from simulation release results. It deliberately subscribes to combat result
/// events rather than animation callbacks. The system has no UI or reward side effects, so it can
/// be restored as part of the same V2.5 staged save boundary as the live cast state.
/// </summary>
public sealed class V25MasterySystem : IDisposable
{
    private const long MicroScale = V25FixedPoint.DensitySyncScale;
    private const int MaxMasteryRank = 6;
    private readonly EventBus _events;
    private readonly CanonicalContentRegistry _canonical;
    private readonly PlayerSystem _player;
    private readonly MonsterSystem _monsters;
    private readonly Func<string, bool> _isLearnedActive;
    private readonly Func<string, int> _effectiveRank;
    private readonly Func<long> _tick;
    private readonly Dictionary<string, V25MasterySkillState> _skills = new(StringComparer.Ordinal);
    private readonly Dictionary<string, V25MasteryTargetBudget> _budgets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _effectSkillIds = new(StringComparer.Ordinal);
    private readonly List<V25MasteryHealingDebt> _healingDebts = [];
    private readonly IDisposable _damageSubscription;
    private readonly IDisposable _effectSubscription;

    public V25MasterySystem(CanonicalContentRegistry canonical, EventBus events, PlayerSystem player, MonsterSystem monsters,
        Func<string, bool> isLearnedActive, Func<string, int> effectiveRank, Func<long> tick)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _monsters = monsters ?? throw new ArgumentNullException(nameof(monsters));
        _isLearnedActive = isLearnedActive ?? throw new ArgumentNullException(nameof(isLearnedActive));
        _effectiveRank = effectiveRank ?? throw new ArgumentNullException(nameof(effectiveRank));
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _damageSubscription = events.Subscribe<CanonicalDamageResolvedEvent>(OnDamageResolved);
        _effectSubscription = events.Subscribe<CanonicalEffectResolvedEvent>(OnEffectResolved);
        InitializeNewGame();
    }

    public IReadOnlyList<V25MasterySkillState> Skills => _skills.Values.OrderBy(item => item.SkillId, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<V25MasteryTargetBudget> TargetBudgets => _budgets.Values.OrderBy(item => item.TargetLifeUid, StringComparer.Ordinal).ToArray();
    public IReadOnlyList<V25MasteryHealingDebt> HealingDebts => _healingDebts.OrderBy(item => item.CreatedTick).ThenBy(item => item.DebtId, StringComparer.Ordinal).ToArray();

    public V25MasterySkillState? Skill(string skillId) => _skills.GetValueOrDefault(skillId);

    public void InitializeNewGame()
    {
        _skills.Clear();
        // Only the canonical NewGame learned list is eligible. A definition being present in the
        // profile does not silently create a mastery track before the player learns/equips it.
        foreach (var skillId in _canonical.Content.NewGame.LearnedSkillIds)
        {
            var skill = FindSkill(skillId);
            if (skill is null || skill.DefaultSourceKind != "PermanentLearned" || skill.MasteryMetric == "None") continue;
            _skills.Add(skill.Id, new(skill.Id, skill.MasteryMetric, 1, 0, Array.Empty<V25MasteryCredit>()));
        }
        _budgets.Clear();
        _effectSkillIds.Clear();
        _healingDebts.Clear();
    }

    /// <summary>Creates a track only after a real permanent learned grant succeeds.</summary>
    public bool EnsureLearnedTrack(string skillId)
    {
        var skill = FindSkill(skillId);
        if (skill is null || skill.DefaultSourceKind != "PermanentLearned" || skill.MasteryMetric == "None") return false;
        if (!_isLearnedActive(skillId)) return false;
        if (_skills.ContainsKey(skillId)) return true;
        _skills.Add(skillId, new(skillId, skill.MasteryMetric, 1, 0, Array.Empty<V25MasteryCredit>()));
        return true;
    }

    public int PromotedRank(string skillId) => _skills.GetValueOrDefault(skillId)?.PromotedRank ?? 0;
    public long ProgressMicro(string skillId) => _skills.GetValueOrDefault(skillId)?.ProgressMicro ?? 0;

    /// <summary>Records an authored mobility credit from a fixed simulation movement result.</summary>
    public bool RecordBoostedDistance(string skillId, string castId, string targetLifeUid, double distanceUnits, long? atTick = null)
    {
        if (!Eligible(skillId, "EligibleBoostedDistanceMeters") || !double.IsFinite(distanceUnits) || distanceUnits <= 0) return false;
        var amount = ToMicro(distanceUnits);
        return AddCredit(skillId, "EligibleBoostedDistanceMeters", castId, targetLifeUid, amount, atTick ?? _tick());
    }

    /// <summary>Records only time during which the authored veil state was actually active.</summary>
    public bool RecordStealthSeconds(string skillId, string castId, double seconds, long? atTick = null)
    {
        if (!Eligible(skillId, "EligibleStealthSeconds") || !double.IsFinite(seconds) || seconds <= 0) return false;
        var amount = ToMicro(seconds);
        return AddCredit(skillId, "EligibleStealthSeconds", castId, _player.State.Uid, amount, atTick ?? _tick());
    }

    /// <summary>Promotes one authored mastery rank when both progress and player gate are ready.
    /// Progress is reset at the threshold and never carries into the next rank.</summary>
    public bool TryPromote(string skillId)
    {
        if (!_skills.TryGetValue(skillId, out var state)) return false;
        var skill = FindSkill(skillId) ?? throw new InvalidDataException($"Unknown mastery skill '{skillId}'.");
        if (state.PromotedRank >= Math.Min(MaxMasteryRank, skill.MaxMasteryRank)) return false;
        var threshold = ThresholdMicro(state.Metric, state.PromotedRank + 1);
        if (state.ProgressMicro < threshold || _player.State.Rank < state.PromotedRank + 1) return false;
        _skills[skillId] = state with { PromotedRank = state.PromotedRank + 1, ProgressMicro = 0 };
        return true;
    }

    public V25MasterySnapshot Snapshot() => new(
        Skills.Select(skill => skill with { Credits = skill.Credits.Select(CloneCredit).ToArray() }).ToArray(),
        TargetBudgets.Select(CloneBudget).ToArray(),
        HealingDebts.Select(CloneDebt).ToArray());

    /// <summary>Validates every row into temporary collections before replacing live mastery.
    /// No definition formula is used to recalculate historical credit amounts.</summary>
    public void Restore(V25MasterySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var stagedSkills = new Dictionary<string, V25MasterySkillState>(StringComparer.Ordinal);
        foreach (var state in snapshot.Skills ?? throw new InvalidDataException("Canonical mastery skills are missing."))
        {
            if (state is null || !stagedSkills.TryAdd(state.SkillId, state)) throw new InvalidDataException("Canonical mastery skill rows are duplicated or null.");
            var skill = FindSkill(state.SkillId);
            if (skill is null || skill.DefaultSourceKind != "PermanentLearned" || skill.MasteryMetric == "None" || state.Metric != skill.MasteryMetric || state.PromotedRank is < 1 or > MaxMasteryRank || state.PromotedRank > skill.MaxMasteryRank || state.ProgressMicro < 0 || state.ProgressMicro > ThresholdMicro(state.Metric, state.PromotedRank + 1) && state.PromotedRank < skill.MaxMasteryRank)
                throw new InvalidDataException($"Canonical mastery skill '{state.SkillId}' is invalid.");
            ValidateCredits(state.Credits, state.SkillId);
        }
        var stagedBudgets = new Dictionary<string, V25MasteryTargetBudget>(StringComparer.Ordinal);
        foreach (var budget in snapshot.TargetBudgets ?? throw new InvalidDataException("Canonical mastery target budgets are missing."))
        {
            if (budget is null || !stagedBudgets.TryAdd(budget.TargetLifeUid, budget) || string.IsNullOrWhiteSpace(budget.TargetLifeUid) || string.IsNullOrWhiteSpace(budget.EncounterId) || budget.SpawnMaxHpMicro <= 0 || budget.RemainingMicro < 0 || budget.RemainingMicro > budget.SpawnMaxHpMicro || !Enum.IsDefined(budget.EncounterType))
                throw new InvalidDataException("Canonical mastery target budget is invalid or duplicated.");
        }
        var stagedDebts = new List<V25MasteryHealingDebt>();
        foreach (var debt in snapshot.HealingDebts ?? throw new InvalidDataException("Canonical mastery healing debt is missing."))
        {
            if (debt is null || string.IsNullOrWhiteSpace(debt.DebtId) || debt.AmountMicro <= 0 || debt.CreatedTick < 0 || debt.ExpireTick <= debt.CreatedTick || debt.ExpireTick - debt.CreatedTick > 1800 || stagedDebts.Any(item => item.DebtId == debt.DebtId))
                throw new InvalidDataException("Canonical mastery healing debt is invalid or duplicated.");
            stagedDebts.Add(debt);
        }
        _skills.Clear(); foreach (var item in stagedSkills) _skills.Add(item.Key, item.Value with { Credits = item.Value.Credits.Select(CloneCredit).ToArray() });
        _budgets.Clear(); foreach (var item in stagedBudgets) _budgets.Add(item.Key, CloneBudget(item.Value));
        _effectSkillIds.Clear();
        _healingDebts.Clear(); _healingDebts.AddRange(stagedDebts.Select(CloneDebt));
    }

    private void OnDamageResolved(CanonicalDamageResolvedEvent result)
    {
        var now = _tick();
        ExpireDebts(now);
        if (result.Rejected) return;
        if (result.TargetUid == _player.State.Uid && result.AttackerUid != _player.State.Uid && result.HpLoss > 0)
        {
            var debtAmount = ToMicro(result.HpLoss);
            if (debtAmount > 0)
            {
                var debtId = $"debt.{result.HitKey.CastId}.{result.HitKey.TargetLifeUid}.{result.HitKey.HitIndex}";
                if (_healingDebts.All(item => item.DebtId != debtId)) _healingDebts.Add(new(debtId, debtAmount, now, checked(now + 1800)));
            }
        }
        if (result.TargetUid == _player.State.Uid && result.AttackerUid != _player.State.Uid)
        {
            var playerStatuses = _player.State.Statuses.Snapshot(_player.State.Uid);
            if (result.ShieldAbsorbed > 0)
            {
                var ward = playerStatuses.Where(status => status.EffectId == "ward")
                    .Select(status => (Status: status, SkillId: _effectSkillIds.GetValueOrDefault(status.SourceId)))
                    .FirstOrDefault(item => item.SkillId is not null && Eligible(item.SkillId, "EffectiveShieldAbsorbed"));
                if (ward.SkillId is not null)
                    AddCredit(ward.SkillId, "EffectiveShieldAbsorbed", $"{ward.Status.SourceId}.{result.HitKey.CastId}.{result.HitKey.HitIndex}", result.HitKey.TargetLifeUid, ToMicro(result.ShieldAbsorbed), now);
            }
            var prevented = ToMicro(result.RawDamage - result.IncomingDamage);
            if (prevented > 0)
            {
                var guard = playerStatuses.Where(status => status.EffectId == "guard")
                    .Select(status => (Status: status, SkillId: _effectSkillIds.GetValueOrDefault(status.SourceId)))
                    .FirstOrDefault(item => item.SkillId is not null && Eligible(item.SkillId, "EffectiveDamagePrevented"));
                if (guard.SkillId is not null)
                    AddCredit(guard.SkillId, "EffectiveDamagePrevented", $"{guard.Status.SourceId}.{result.HitKey.CastId}.{result.HitKey.HitIndex}", result.HitKey.TargetLifeUid, prevented, now);
            }
        }
        if (result.AttackerUid != _player.State.Uid || result.TargetUid == _player.State.Uid || result.SkillId is null) return;
        var skill = FindSkill(result.SkillId);
        if (skill is null || skill.DefaultSourceKind != "PermanentLearned" || skill.MasteryMetric == "None" || !_isLearnedActive(skill.Id)) return;
        var monster = _monsters.Get(result.TargetUid);
        if (monster is null || !monster.RewardEligible || monster.EncounterType is V25EncounterType.Arena or V25EncounterType.Debug) return;
        var budget = GetBudget(monster);
        var creditAmount = Math.Max(0, ToMicro(result.HpLoss));
        if (skill.MasteryMetric == "EffectiveDamage")
            creditAmount = Math.Min(creditAmount, budget.RemainingMicro);
        else if (skill.MasteryMetric == "EffectiveDamagePrevented")
            creditAmount = Math.Min(Math.Max(0, ToMicro(result.RawDamage - result.IncomingDamage)), budget.RemainingMicro);
        else creditAmount = 0;
        if (creditAmount <= 0) return;
        var before = _skills[skill.Id].ProgressMicro;
        if (AddCredit(skill.Id, skill.MasteryMetric, $"{result.HitKey.CastId}.{result.HitKey.HitIndex}", result.HitKey.TargetLifeUid, creditAmount, now))
        {
            var credited = _skills[skill.Id].ProgressMicro - before;
            _budgets[budget.TargetLifeUid] = budget with { RemainingMicro = Math.Max(0, budget.RemainingMicro - credited) };
        }
    }

    private void OnEffectResolved(CanonicalEffectResolvedEvent result)
    {
        var now = _tick();
        ExpireDebts(now);
        var skill = FindSkill(result.SkillId);
        if (result.EffectId is "ward" or "guard" or "veil" or "cleanse") _effectSkillIds[result.CastId] = result.SkillId;
        if (skill is null || skill.DefaultSourceKind != "PermanentLearned" || skill.MasteryMetric == "None" || !_isLearnedActive(skill.Id) || !result.Meaningful) return;
        switch (skill.MasteryMetric)
        {
            case "EffectiveHealing" when result.EffectId == "heal" && result.TargetUid == _player.State.Uid:
            {
                var amount = ConsumeHealingDebt(ToMicro(result.Amount), now);
                if (amount > 0) AddCredit(skill.Id, skill.MasteryMetric, result.CastId, result.TargetUid, amount, now);
                break;
            }
            case "EffectiveShieldAbsorbed" when result.EffectId == "ward":
                // Ward capacity is recorded at release; actual shield absorption is credited by
                // a later damage result through the target-life budget, so grant no fake amount.
                break;
            case "MeaningfulUseCount" when result.EffectId is "cleanse" or "stun":
                AddCredit(skill.Id, skill.MasteryMetric, result.CastId, result.TargetUid, MicroScale, now);
                break;
        }
    }

    private bool AddCredit(string skillId, string metric, string castId, string targetLifeUid, long amountMicro, long tick)
    {
        if (amountMicro <= 0 || !_skills.TryGetValue(skillId, out var state) || state.Metric != metric) return false;
        var awardId = $"mastery.{skillId}.{metric}.{castId}.{targetLifeUid}";
        if (state.Credits.Any(credit => credit.AwardId == awardId)) return false;
        var ceiling = state.PromotedRank >= MaxMasteryRank ? state.ProgressMicro : ThresholdMicro(metric, state.PromotedRank + 1);
        var accepted = Math.Min(amountMicro, Math.Max(0, ceiling - state.ProgressMicro));
        if (accepted <= 0) return false;
        var credit = new V25MasteryCredit(awardId, skillId, metric, castId, targetLifeUid, accepted, tick, _canonical.Content.ContentVersion);
        var nextProgress = checked(state.ProgressMicro + accepted);
        _skills[skillId] = state with { ProgressMicro = nextProgress, Credits = state.Credits.Append(credit).ToArray() };
        return true;
    }

    private V25MasteryTargetBudget GetBudget(MonsterState monster)
    {
        if (_budgets.TryGetValue(monster.TargetLifeUid, out var existing)) return existing;
        var budget = new V25MasteryTargetBudget(monster.TargetLifeUid, monster.EncounterId, ToMicro(monster.MaxHp), ToMicro(monster.MaxHp), monster.RewardEligible, monster.EncounterType);
        _budgets.Add(budget.TargetLifeUid, budget);
        return budget;
    }

    private long ConsumeHealingDebt(long amount, long now)
    {
        if (amount <= 0) return 0;
        var remaining = amount;
        var consumed = 0L;
        foreach (var debt in _healingDebts.OrderBy(item => item.CreatedTick).ThenBy(item => item.DebtId, StringComparer.Ordinal).ToArray())
        {
            if (remaining <= 0) break;
            var take = Math.Min(remaining, debt.AmountMicro);
            consumed = checked(consumed + take); remaining -= take;
            var left = debt.AmountMicro - take;
            var index = _healingDebts.FindIndex(item => item.DebtId == debt.DebtId);
            if (index >= 0) { if (left <= 0) _healingDebts.RemoveAt(index); else _healingDebts[index] = debt with { AmountMicro = left }; }
        }
        return consumed;
    }

    private void ExpireDebts(long now) => _healingDebts.RemoveAll(debt => debt.ExpireTick <= now || debt.AmountMicro <= 0);

    private bool Eligible(string skillId, string metric) => _skills.TryGetValue(skillId, out var state) && state.Metric == metric && _isLearnedActive(skillId);

    private CanonicalSkillDefinition? FindSkill(string skillId) => _canonical.Content.Skills.FirstOrDefault(skill => skill.Id == skillId);

    private static long ThresholdMicro(string metric, int rank)
    {
        if (rank is < 1 or > MaxMasteryRank) return long.MaxValue;
        var threshold = metric switch
        {
            "EffectiveDamage" or "EffectiveDamagePrevented" => new[] { 0L, 10_000, 100_000, 500_000, 2_500_000, 10_000_000 },
            "EffectiveHealing" or "EffectiveShieldAbsorbed" => new[] { 0L, 5_000, 50_000, 250_000, 1_250_000, 5_000_000 },
            "EligibleBoostedDistanceMeters" => new[] { 0L, 5_000, 30_000, 120_000, 400_000, 1_200_000 },
            "EligibleStealthSeconds" => new[] { 0L, 600, 3_600, 14_400, 43_200, 108_000 },
            "MeaningfulUseCount" => new[] { 0L, 100, 500, 2_000, 7_500, 25_000 },
            _ => Array.Empty<long>(),
        };
        if (threshold.Length == 0) return long.MaxValue;
        return checked(threshold[Math.Min(rank - 1, threshold.Length - 1)] * MicroScale);
    }

    private static long ToMicro(double value)
    {
        if (!double.IsFinite(value) || value <= 0) return 0;
        return checked((long)Math.Round(value * MicroScale, MidpointRounding.AwayFromZero));
    }

    private static void ValidateCredits(IReadOnlyList<V25MasteryCredit>? credits, string skillId)
    {
        if (credits is null) throw new InvalidDataException($"Mastery credits for '{skillId}' are missing.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var credit in credits)
            if (credit is null || credit.SkillId != skillId || string.IsNullOrWhiteSpace(credit.AwardId) || !ids.Add(credit.AwardId) || string.IsNullOrWhiteSpace(credit.Metric) || string.IsNullOrWhiteSpace(credit.CastId) || string.IsNullOrWhiteSpace(credit.TargetLifeUid) || credit.AmountMicro <= 0 || credit.Tick < 0 || string.IsNullOrWhiteSpace(credit.SourceVersion))
                throw new InvalidDataException($"Mastery credit for '{skillId}' is invalid or duplicated.");
    }

    private static V25MasteryCredit CloneCredit(V25MasteryCredit value) => value with { };
    private static V25MasteryTargetBudget CloneBudget(V25MasteryTargetBudget value) => value with { };
    private static V25MasteryHealingDebt CloneDebt(V25MasteryHealingDebt value) => value with { };

    public void Dispose() { _damageSubscription.Dispose(); _effectSubscription.Dispose(); }
}
