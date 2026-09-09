using SoloVsMortal.Data.Definitions.V25;
using SoloVsMortal.Simulation.Rules;
using SoloVsMortal.Simulation.State;
using SoloVsMortal.Simulation.State.V25;
using SoloVsMortal.Simulation.Systems;

namespace SoloVsMortal.Simulation.Systems.V25;

/// <summary>
/// Owns the canonical Player Spirit resource and the Soul drain transaction.  Spirit is
/// represented as integer milli-points in PlayerState; the small remainder keeps a fixed
/// 60 Hz tick from losing fractional per-minute rates at every tick boundary.
/// </summary>
public sealed class V25SpiritSystem
{
    private const long MicroPerMilli = 1_000;
    private const long MicroPerMinuteScale = 1_000_000;
    private readonly CanonicalContentRegistry _canonical;
    private readonly PlayerSystem _player;
    private readonly SoulSystem _souls;
    private readonly SummonSystem _summons;
    private readonly Func<bool> _atShrine;
    private readonly Func<bool> _combatOrHazard;
    private long _remainderMicro;
    // Remainder of the per-minute rate division by 3600 (60 seconds x 60 ticks). Keeping this separate from the
    // resource's sub-milli remainder avoids silently losing fraction of a point every tick.
    private long _rateRemainderMicro;
    private long _lastTick = -1;

    public V25SpiritSystem(CanonicalContentRegistry canonical, PlayerSystem player, SoulSystem souls, SummonSystem summons,
        Func<bool> atShrine, Func<bool> combatOrHazard)
    {
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _souls = souls ?? throw new ArgumentNullException(nameof(souls));
        _summons = summons ?? throw new ArgumentNullException(nameof(summons));
        _atShrine = atShrine ?? throw new ArgumentNullException(nameof(atShrine));
        _combatOrHazard = combatOrHazard ?? throw new ArgumentNullException(nameof(combatOrHazard));
        if (!_player.CanonicalMode || !_souls.CanonicalMode) throw new InvalidOperationException("Canonical Spirit requires canonical Player and Soul systems.");
    }

    public long RemainderMicro => _remainderMicro;
    public long RateRemainderMicro => _rateRemainderMicro;
    public long CurrentMilli => V25FixedPoint.RoundMilli(_player.State.CurrentSpirit);
    public long MaximumMilli => V25FixedPoint.RoundMilli(_player.State.MaxSpirit);

    public double RegenPerMinute
    {
        get
        {
            var max = _player.State.MaxSpirit;
            var rank = _player.State.Rank;
            return (0.10 * max + 2 * rank) * (1 + _player.SpiritRegenPercent) + _player.SpiritRegenFlat;
        }
    }

    public double DrainPerMinute => _summons.CanonicalSnapshot()
        .Where(state => state.Mode == V25SoulRuntimeMode.Summoned)
        .Sum(state =>
        {
            var ownership = _souls.CanonicalDensity?.GetOwnership(state.SpeciesId);
            var tier = _canonical.SpeciesForProfile(_canonical.ActiveProfileId).FirstOrDefault(species => species.Id == state.SpeciesId)?.PowerTier ?? 1;
            var density = Math.Min(800, V25FixedPoint.FromMicro(ownership?.CommittedDensityMicro ?? 0));
            return 3.5 * Math.Pow(tier, 1.20) * (1 + 0.35 * Math.Log(1 + density / 100));
        });

    public double NetPerMinute => RegenPerMinute - DrainPerMinute;

    /// <summary>Restores only the fractional fixed-point carry. Resource values are restored by the actor snapshot.</summary>
    public void RestoreRemainder(long remainderMicro)
    {
        if (remainderMicro is < -MicroPerMilli or >= MicroPerMilli) throw new InvalidDataException("Spirit fractional remainder is outside one milli-point.");
        _remainderMicro = remainderMicro;
    }

    public void RestoreRateRemainder(long remainderMicro)
    {
        if (remainderMicro is < -3599 or > 3599) throw new InvalidDataException("Spirit rate remainder is outside one fixed-tick division.");
        _rateRemainderMicro = remainderMicro;
    }

    /// <summary>Runs one canonical fixed tick, including the one-time zero-crossing recall.</summary>
    public void Tick(long simulationTick)
    {
        if (simulationTick < 0) throw new ArgumentOutOfRangeException(nameof(simulationTick));
        if (_lastTick == simulationTick) return;
        _lastTick = simulationTick;
        if (!_player.State.Alive) return;

        var tickSeconds = 1.0 / 60.0;
        var net = NetPerMinute;
        var currentMicro = checked(CurrentMilli * MicroPerMilli + _remainderMicro);
        var deltaMicro = RateToTickMicro(net);
        if (net < 0 && currentMicro >= 0 && currentMicro + deltaMicro <= 0)
        {
            // Integrate precisely to zero before recalling. The remaining fraction of this
            // fixed tick is integrated using the post-recall rates, so a summon cannot get
            // another full tick of drain after the crossing.
            var secondsToZero = currentMicro * 60.0 / (Math.Abs(net) * MicroPerMinuteScale);
            _player.State.CurrentSpirit = 0;
            _remainderMicro = 0;
            _ = _summons.RecallAllLivingForSpirit();
            var remainingSeconds = Math.Max(0, tickSeconds - secondsToZero);
            if (remainingSeconds > 0 && _player.State.Alive)
                ApplyForSeconds(NetPerMinute, remainingSeconds);
            return;
        }

        ApplyMicroDelta(currentMicro + deltaMicro);
    }

    /// <summary>Applies authored Rest regeneration; callers enforce the interaction command boundary.</summary>
    public bool TryRest(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || !_player.State.Alive || !_atShrine() || _combatOrHazard()) return false;
        ApplyForSeconds(0, seconds, rest: true);
        return true;
    }

    private long RateToTickMicro(double pointsPerMinute)
    {
        if (!double.IsFinite(pointsPerMinute)) throw new InvalidDataException("Canonical Spirit rate is not finite.");
        var perMinute = checked((long)Math.Round(pointsPerMinute * MicroPerMinuteScale, MidpointRounding.AwayFromZero));
        var total = checked(perMinute + _rateRemainderMicro);
        var whole = total / 3600;
        _rateRemainderMicro = total % 3600;
        return whole;
    }

    private void ApplyForSeconds(double netPerMinute, double seconds, bool rest = false)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        var rate = rest ? _canonical.Balance.Summon.RestVitalityPerSecond : netPerMinute / 60;
        if (rest)
        {
            var hp = Math.Min(_player.State.MaxHp, _player.State.CurrentHp + _player.State.MaxHp * rate * seconds);
            var spirit = Math.Min(_player.State.MaxSpirit, _player.State.CurrentSpirit + _player.State.MaxSpirit * rate * seconds);
            _player.State.CurrentHp = V25FixedPoint.QuantizeMilli(hp);
            ApplyMicroDelta(checked(V25FixedPoint.RoundMilli(spirit) * MicroPerMilli + _remainderMicro));
            return;
        }
        var delta = checked((long)Math.Round(rate * seconds * MicroPerMinuteScale, MidpointRounding.AwayFromZero));
        ApplyMicroDelta(checked(CurrentMilli * MicroPerMilli + _remainderMicro + delta));
    }

    private void ApplyMicroDelta(long totalMicro)
    {
        var maxMicro = checked(MaximumMilli * MicroPerMilli);
        var clamped = Math.Clamp(totalMicro, 0, maxMicro);
        var milli = clamped / MicroPerMilli;
        _remainderMicro = clamped % MicroPerMilli;
        _player.State.CurrentSpirit = V25FixedPoint.FromMilli(milli);
    }
}
