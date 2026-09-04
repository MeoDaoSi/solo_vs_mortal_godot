namespace SoloVsMortal.Application.Persistence;

public sealed record TimedBuffSaveData(string PillId, double RemainingSeconds);
public sealed record ProgressionSaveData(
    IReadOnlyDictionary<string, int> Inventory,
    IReadOnlyList<TimedBuffSaveData> PlayerBuffs);
