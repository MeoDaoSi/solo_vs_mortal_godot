using System.Text;
using System.Text.Json;
using SoloVsMortal.Data.Definitions.V25;

namespace SoloVsMortal.Application.Persistence.V25;

public enum V25RecoveryStatus
{
    NoSave,
    CurrentValid,
    RecoveredStagedCommit,
    RecoveredAlreadySwapped,
    RecoveredPrevious,
    RejectedPreserved,
}

public sealed record V25CommitResult(bool Committed, bool AlreadyCommitted, long CommitSequence, string TransactionId);
public sealed record V25RecoveryResult(V25RecoveryStatus Status, V25SaveEnvelope? Save, string? Error);
public sealed record V25CommitHistoryEntry(string SaveId, string TransactionId, long CommitSequence, string Checksum);

/// <summary>
/// Durable V2.5 save boundary. The temp file is validated before swap, the old current file is retained as
/// .previous, the committed current file as .backup, and the WAL makes a staged swap retryable by transaction ID.
/// </summary>
public sealed class V25SaveStore
{
    private static readonly JsonSerializerOptions WalOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow, WriteIndented = false };

    public V25SaveStore(string currentPath, CanonicalContentRegistry? canonical = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPath);
        CurrentPath = Path.GetFullPath(currentPath);
        TemporaryPath = CurrentPath + ".tmp";
        PreviousPath = CurrentPath + ".previous";
        BackupPath = CurrentPath + ".backup";
        WalPath = CurrentPath + ".wal";
        RejectedPath = CurrentPath + ".rejected";
        HistoryPath = CurrentPath + ".history";
        Canonical = canonical;
    }

    public string CurrentPath { get; }
    public string TemporaryPath { get; }
    public string PreviousPath { get; }
    public string BackupPath { get; }
    public string WalPath { get; }
    public string RejectedPath { get; }
    public string HistoryPath { get; }
    public CanonicalContentRegistry? Canonical { get; }

    public long NextCommitSequence(string saveId)
    {
        var maximum = ReadHistory().Where(item => item.SaveId == saveId).Select(item => item.CommitSequence).DefaultIfEmpty(-1).Max();
        foreach (var path in new[] { CurrentPath, PreviousPath, BackupPath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var saved = V25SaveCodec.Deserialize(File.ReadAllText(path, Encoding.UTF8), Canonical);
                if (saved.SaveId == saveId) maximum = Math.Max(maximum, saved.CommitSequence);
            }
            catch (InvalidDataException) { /* Preserve invalid candidates; they are not sequence authority. */ }
        }
        return checked(maximum + 1);
    }

    public V25CommitResult Commit(V25SaveEnvelope envelope)
    {
        var normalized = V25SaveCodec.Normalize(envelope);
        V25SaveCodec.Validate(normalized, Canonical);
        var payload = V25SaveCodec.Serialize(normalized);
        var checksum = normalized.Checksum;
        // Resolve an uncertain previous swap before checking sequence fences. Otherwise a
        // successful swap followed by an I/O failure makes its exact retry look stale.
        var wal = ReadWal();
        if (wal is { State: "staged" })
        {
            var recovery = Recover();
            if (recovery.Save is null || recovery.Status == V25RecoveryStatus.RejectedPreserved)
                throw new IOException($"An unresolved V2.5 save transaction is preserved: {recovery.Error}.");
            wal = ReadWal();
        }
        if (TryReadCurrent(out var committed, out _) && committed is not null &&
            committed.SaveId == normalized.SaveId && committed.TransactionId == normalized.TransactionId &&
            committed.CommitSequence == normalized.CommitSequence && committed.Checksum == checksum)
        {
            WriteWal(new V25WalRecord(V25SaveFormat.FormatId, V25SaveFormat.SchemaVersion, committed.TransactionId, "committed", committed.CommitSequence, checksum, committed.SaveId));
            AppendHistory(new V25CommitHistoryEntry(committed.SaveId, committed.TransactionId, committed.CommitSequence, checksum));
            return new(false, true, committed.CommitSequence, committed.TransactionId);
        }
        var history = ReadHistory();
        var historical = history.FirstOrDefault(item => string.Equals(item.SaveId, normalized.SaveId, StringComparison.Ordinal) && string.Equals(item.TransactionId, normalized.TransactionId, StringComparison.Ordinal));
        if (historical is not null)
        {
            if (string.Equals(historical.Checksum, checksum, StringComparison.OrdinalIgnoreCase)) return new(false, true, historical.CommitSequence, historical.TransactionId);
            throw new IOException($"Transaction '{normalized.TransactionId}' was already committed with a different payload.");
        }
        var highestHistoricalSequence = history.Where(item => string.Equals(item.SaveId, normalized.SaveId, StringComparison.Ordinal)).Select(item => item.CommitSequence).DefaultIfEmpty(-1).Max();
        if (normalized.CommitSequence <= highestHistoricalSequence)
            throw new InvalidDataException($"Save commit sequence {normalized.CommitSequence} is not newer than durable history {highestHistoricalSequence}.");
        foreach (var candidatePath in new[] { PreviousPath, BackupPath })
        {
            if (!File.Exists(candidatePath)) continue;
            V25SaveEnvelope? candidate = null;
            try
            {
                candidate = V25SaveCodec.Deserialize(File.ReadAllText(candidatePath, Encoding.UTF8), Canonical);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                // An invalid fallback is preserved and does not establish a sequence fence.
            }
            if (candidate is not null && string.Equals(candidate.SaveId, normalized.SaveId, StringComparison.Ordinal) && candidate.CommitSequence >= normalized.CommitSequence)
                throw new InvalidDataException($"Save commit sequence {normalized.CommitSequence} is not newer than preserved {Path.GetFileName(candidatePath)} sequence {candidate.CommitSequence}.");
        }
        if (wal is { State: "committed", TransactionId: var committedId } && string.Equals(committedId, normalized.TransactionId, StringComparison.Ordinal) && string.Equals(wal.SaveId, normalized.SaveId, StringComparison.Ordinal))
        {
            if (TryReadCurrent(out var current, out _) && current is not null && string.Equals(current.Checksum, checksum, StringComparison.OrdinalIgnoreCase))
                return new(false, true, current.CommitSequence, current.TransactionId);
            throw new IOException($"Transaction '{normalized.TransactionId}' was already committed with a different payload.");
        }
        if (File.Exists(CurrentPath))
        {
            if (!TryReadCurrent(out var existing, out var currentError) || existing is null)
                throw new IOException($"Current V2.5 save is invalid; explicit recovery is required and the original was preserved: {currentError ?? "unknown read error"}.");
            if (string.Equals(existing.SaveId, normalized.SaveId, StringComparison.Ordinal) && string.Equals(existing.TransactionId, normalized.TransactionId, StringComparison.Ordinal) && existing.CommitSequence == normalized.CommitSequence && string.Equals(existing.Checksum, checksum, StringComparison.OrdinalIgnoreCase))
                return new(false, true, existing.CommitSequence, existing.TransactionId);
            if (!string.Equals(existing.SaveId, normalized.SaveId, StringComparison.Ordinal)) throw new InvalidDataException($"Save ID '{normalized.SaveId}' does not match current slot '{existing.SaveId}'.");
            if (normalized.CommitSequence <= existing.CommitSequence) throw new InvalidDataException($"Save commit sequence {normalized.CommitSequence} is not newer than current {existing.CommitSequence}.");
        }

        DurableSaveFiles.WriteText(TemporaryPath, payload);
        WriteWal(new V25WalRecord(V25SaveFormat.FormatId, V25SaveFormat.SchemaVersion, normalized.TransactionId, "staged", normalized.CommitSequence, checksum, normalized.SaveId));
        var staged = V25SaveCodec.Deserialize(File.ReadAllText(TemporaryPath, Encoding.UTF8), Canonical);
        if (!string.Equals(staged.Checksum, checksum, StringComparison.OrdinalIgnoreCase) || !string.Equals(staged.TransactionId, normalized.TransactionId, StringComparison.Ordinal))
            throw new InvalidDataException("Staged V2.5 save changed before swap.");
        PromoteTemporary();
        DurableSaveFiles.CopyDurable(CurrentPath, BackupPath);
        WriteWal(new V25WalRecord(V25SaveFormat.FormatId, V25SaveFormat.SchemaVersion, normalized.TransactionId, "committed", normalized.CommitSequence, checksum, normalized.SaveId));
        AppendHistory(new V25CommitHistoryEntry(normalized.SaveId, normalized.TransactionId, normalized.CommitSequence, checksum));
        return new(true, false, normalized.CommitSequence, normalized.TransactionId);
    }

    public V25SaveEnvelope? ReadCurrent() => TryReadCurrent(out var save, out var error) ? save : throw new InvalidDataException(error ?? "Current V2.5 save is invalid.");

    public bool TryReadCurrent(out V25SaveEnvelope? save, out string? error)
    {
        save = null; error = null;
        if (!File.Exists(CurrentPath)) return true;
        try { save = V25SaveCodec.Deserialize(File.ReadAllText(CurrentPath, Encoding.UTF8), Canonical); return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        { error = exception.Message; return false; }
    }

    public V25RecoveryResult Recover()
    {
        // Unsupported versions are not corruption. Never downgrade them to an older backup.
        foreach (var path in new[] { CurrentPath, WalPath, TemporaryPath })
        {
            var incompatibility = ReadVersionIncompatibility(path);
            if (incompatibility is not null)
                return new(V25RecoveryStatus.RejectedPreserved, null, incompatibility);
        }
        V25WalRecord? wal;
        try { wal = ReadWal(); }
        catch (InvalidDataException exception)
        {
            PreserveFile(WalPath, WalPath + ".rejected");
            return RecoverFromCandidates(exception.Message);
        }
        if (wal is null)
        {
            if (!File.Exists(CurrentPath))
                return File.Exists(PreviousPath) || File.Exists(BackupPath)
                    ? RecoverFromCandidates("Current snapshot is missing; checking preserved backups.")
                    : new(V25RecoveryStatus.NoSave, null, null);
            return TryReadCurrent(out var current, out var error) ? new(V25RecoveryStatus.CurrentValid, current, null) : RecoverFromCandidates(error ?? "Current V2.5 save is invalid; no WAL was present.");
        }
        if (!string.Equals(wal.Format, V25SaveFormat.FormatId, StringComparison.Ordinal) || wal.SchemaVersion != V25SaveFormat.SchemaVersion || wal.State is not ("staged" or "committed"))
        {
            PreserveFile(WalPath, WalPath + ".rejected");
            return RecoverFromCandidates("Unknown or malformed V2.5 WAL record; the WAL was preserved and valid save candidates were considered.");
        }
        if (wal.State == "committed")
        {
            if (TryReadCurrent(out var current, out var error) && current is not null && string.Equals(current.SaveId, wal.SaveId, StringComparison.Ordinal) && string.Equals(current.TransactionId, wal.TransactionId, StringComparison.Ordinal) && current.CommitSequence == wal.CommitSequence && string.Equals(current.Checksum, wal.PayloadChecksum, StringComparison.OrdinalIgnoreCase))
            {
                AppendHistory(new V25CommitHistoryEntry(current.SaveId, current.TransactionId, current.CommitSequence, current.Checksum));
                return new(V25RecoveryStatus.RecoveredAlreadySwapped, current, null);
            }
            return RecoverFromCandidates(error ?? "Committed WAL does not match current save; files were preserved.");
        }
        if (TryReadCurrent(out var stagedCurrent, out _) && stagedCurrent is not null && string.Equals(stagedCurrent.SaveId, wal.SaveId, StringComparison.Ordinal) && string.Equals(stagedCurrent.TransactionId, wal.TransactionId, StringComparison.Ordinal) && stagedCurrent.CommitSequence == wal.CommitSequence && string.Equals(stagedCurrent.Checksum, wal.PayloadChecksum, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(TemporaryPath)) File.Delete(TemporaryPath);
            DurableSaveFiles.CopyDurable(CurrentPath, BackupPath);
            WriteWal(wal with { State = "committed" });
            AppendHistory(new V25CommitHistoryEntry(stagedCurrent.SaveId, stagedCurrent.TransactionId, stagedCurrent.CommitSequence, stagedCurrent.Checksum));
            return new(V25RecoveryStatus.RecoveredAlreadySwapped, stagedCurrent, null);
        }
        if (!File.Exists(TemporaryPath)) return RecoverFromCandidates("Staged WAL has no temporary save; files were preserved.");
        try
        {
            var staged = V25SaveCodec.Deserialize(File.ReadAllText(TemporaryPath, Encoding.UTF8), Canonical);
            if (!string.Equals(staged.SaveId, wal.SaveId, StringComparison.Ordinal) || !string.Equals(staged.TransactionId, wal.TransactionId, StringComparison.Ordinal) || staged.CommitSequence != wal.CommitSequence || !string.Equals(staged.Checksum, wal.PayloadChecksum, StringComparison.OrdinalIgnoreCase))
                return RecoverFromCandidates("Staged save does not match WAL; files were preserved.");
            if (TryReadCurrent(out var current, out _) && current is not null && string.Equals(current.SaveId, staged.SaveId, StringComparison.Ordinal) && string.Equals(current.TransactionId, staged.TransactionId, StringComparison.Ordinal) && current.CommitSequence == staged.CommitSequence && string.Equals(current.Checksum, staged.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(TemporaryPath);
                WriteWal(wal with { State = "committed" });
                AppendHistory(new V25CommitHistoryEntry(current.SaveId, current.TransactionId, current.CommitSequence, current.Checksum));
                return new(V25RecoveryStatus.RecoveredAlreadySwapped, current, null);
            }
            PromoteTemporary();
            DurableSaveFiles.CopyDurable(CurrentPath, BackupPath);
            WriteWal(wal with { State = "committed" });
            AppendHistory(new V25CommitHistoryEntry(staged.SaveId, staged.TransactionId, staged.CommitSequence, staged.Checksum));
            return new(V25RecoveryStatus.RecoveredStagedCommit, staged, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            return RecoverFromCandidates($"Staged V2.5 recovery failed; files were preserved: {exception.Message}");
        }
    }

    private static string? ReadVersionIncompatibility(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            foreach (var (key, expected) in new[] {
                ("format", V25SaveFormat.FormatId), ("specRevision", V25SaveFormat.SpecRevision),
                ("contentVersion", V25SaveFormat.ContentVersion), ("balanceVersion", V25SaveFormat.BalanceVersion) })
                if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() != expected)
                    return $"Unsupported {key} in {Path.GetFileName(path)}; all slot files were left unchanged.";
            if (root.TryGetProperty("schemaVersion", out var schema) && schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out var version) && version != V25SaveFormat.SchemaVersion)
                return $"Unsupported schema in {Path.GetFileName(path)}; all slot files were left unchanged.";
            return null;
        }
        catch (JsonException) { return null; } // Corruption follows the validated backup recovery path.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { return $"Cannot inspect {Path.GetFileName(path)}; recovery stopped without replacing files: {exception.Message}"; }
    }

    private V25RecoveryResult RecoverFromCandidates(string cause)
    {
        V25SaveEnvelope? best = null;
        var bestPath = "";
        foreach (var candidate in new[] { CurrentPath, PreviousPath, BackupPath })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var parsed = V25SaveCodec.Deserialize(File.ReadAllText(candidate, Encoding.UTF8), Canonical);
                if (best is not null && !string.Equals(best.SaveId, parsed.SaveId, StringComparison.Ordinal))
                    return new(V25RecoveryStatus.RejectedPreserved, null, "Recovery candidates belong to different save identities; files were left unchanged.");
                if (best is null || parsed.CommitSequence > best.CommitSequence || parsed.CommitSequence == best.CommitSequence && candidate == CurrentPath)
                {
                    best = parsed;
                    bestPath = candidate;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            { }
        }
        if (best is null) return new(V25RecoveryStatus.RejectedPreserved, null, cause);
        try
        {
            if (bestPath != CurrentPath)
            {
                if (File.Exists(CurrentPath)) PreserveFile(CurrentPath, RejectedPath);
                DurableSaveFiles.WriteText(TemporaryPath, File.ReadAllText(bestPath, Encoding.UTF8));
                if (File.Exists(CurrentPath)) File.Replace(TemporaryPath, CurrentPath, PreviousPath, true); else File.Move(TemporaryPath, CurrentPath);
                DurableSaveFiles.CopyDurable(CurrentPath, BackupPath);
                best = V25SaveCodec.Deserialize(File.ReadAllText(CurrentPath, Encoding.UTF8), Canonical);
            }
            WriteWal(new V25WalRecord(V25SaveFormat.FormatId, V25SaveFormat.SchemaVersion, best.TransactionId, "committed", best.CommitSequence, best.Checksum, best.SaveId));
            AppendHistory(new V25CommitHistoryEntry(best.SaveId, best.TransactionId, best.CommitSequence, best.Checksum));
            var status = bestPath == CurrentPath ? V25RecoveryStatus.CurrentValid : V25RecoveryStatus.RecoveredPrevious;
            return new(status, best, $"{cause}; selected valid {Path.GetFileName(bestPath)} and restored the current slot while preserving the rejected file.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        { return new(V25RecoveryStatus.RejectedPreserved, null, $"{cause}; valid recovery candidate could not be promoted: {exception.Message}"); }
    }

    /// <summary>Explicit operator recovery from .previous/.backup. It never runs as a side effect of a failed read.</summary>
    public bool TryRestorePrevious(out V25SaveEnvelope? save, out string? error)
    {
        save = null; error = null;
        foreach (var candidate in new[] { PreviousPath, BackupPath })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var parsed = V25SaveCodec.Deserialize(File.ReadAllText(candidate, Encoding.UTF8), Canonical);
                if (File.Exists(CurrentPath)) PreserveFile(CurrentPath, RejectedPath);
                DurableSaveFiles.WriteText(TemporaryPath, File.ReadAllText(candidate, Encoding.UTF8));
                if (File.Exists(CurrentPath)) File.Replace(TemporaryPath, CurrentPath, BackupPath, true); else File.Move(TemporaryPath, CurrentPath);
                DurableSaveFiles.CopyDurable(CurrentPath, BackupPath);
                WriteWal(new V25WalRecord(V25SaveFormat.FormatId, V25SaveFormat.SchemaVersion, parsed.TransactionId, "committed", parsed.CommitSequence, parsed.Checksum, parsed.SaveId));
                AppendHistory(new V25CommitHistoryEntry(parsed.SaveId, parsed.TransactionId, parsed.CommitSequence, parsed.Checksum));
                save = parsed;
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            { error = exception.Message; }
        }
        error ??= "No valid previous V2.5 save exists; current files were preserved.";
        return false;
    }

    private void PromoteTemporary()
    {
        if (!File.Exists(TemporaryPath)) throw new FileNotFoundException("V2.5 temporary save is missing.", TemporaryPath);
        if (File.Exists(CurrentPath)) File.Replace(TemporaryPath, CurrentPath, PreviousPath, true); else File.Move(TemporaryPath, CurrentPath);
    }

    private V25WalRecord? ReadWal()
    {
        if (!File.Exists(WalPath)) return null;
        try
        {
            var record = JsonSerializer.Deserialize<V25WalRecord>(File.ReadAllText(WalPath, Encoding.UTF8), WalOptions) ?? throw new InvalidDataException("V2.5 WAL is empty.");
            ValidateWal(record);
            return record;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { throw new InvalidDataException("V2.5 WAL is unreadable; files were preserved.", exception); }
    }

    private static void ValidateWal(V25WalRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Format) || string.IsNullOrWhiteSpace(record.State)) throw new InvalidDataException("V2.5 WAL has missing identity fields.");
        if (string.IsNullOrWhiteSpace(record.SaveId) || !System.Text.RegularExpressions.Regex.IsMatch(record.SaveId, "^[A-Za-z0-9][A-Za-z0-9_.:-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new InvalidDataException("V2.5 WAL save ID is invalid.");
        if (string.IsNullOrWhiteSpace(record.TransactionId) || !System.Text.RegularExpressions.Regex.IsMatch(record.TransactionId, "^[A-Za-z0-9][A-Za-z0-9_.:-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new InvalidDataException("V2.5 WAL transaction ID is invalid.");
        if (record.CommitSequence < 0) throw new InvalidDataException("V2.5 WAL commit sequence cannot be negative.");
        if (string.IsNullOrWhiteSpace(record.PayloadChecksum) || record.PayloadChecksum.Length != 64 || record.PayloadChecksum.Any(character => !Uri.IsHexDigit(character))) throw new InvalidDataException("V2.5 WAL payload checksum is invalid.");
    }

    private void WriteWal(V25WalRecord record) => DurableSaveFiles.AtomicWriteText(WalPath, JsonSerializer.Serialize(record, WalOptions));

    private IReadOnlyList<V25CommitHistoryEntry> ReadHistory()
    {
        if (!File.Exists(HistoryPath)) return Array.Empty<V25CommitHistoryEntry>();
        try
        {
            var entries = JsonSerializer.Deserialize<List<V25CommitHistoryEntry>>(File.ReadAllText(HistoryPath, Encoding.UTF8), WalOptions) ?? throw new InvalidDataException("V2.5 commit history is empty.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.SaveId) || string.IsNullOrWhiteSpace(entry.TransactionId) || entry.CommitSequence < 0 || string.IsNullOrWhiteSpace(entry.Checksum) || entry.Checksum.Length != 64 || entry.Checksum.Any(character => !Uri.IsHexDigit(character)) || !keys.Add($"{entry.SaveId}|{entry.TransactionId}"))
                    throw new InvalidDataException("V2.5 commit history is invalid.");
            }
            return entries;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { throw new InvalidDataException("V2.5 commit history is unreadable; files were preserved.", exception); }
    }

    private void AppendHistory(V25CommitHistoryEntry entry)
    {
        var entries = ReadHistory().ToList();
        var existing = entries.FirstOrDefault(item => item.SaveId == entry.SaveId && item.TransactionId == entry.TransactionId);
        if (existing is not null)
        {
            if (existing.Checksum != entry.Checksum) throw new InvalidDataException("V2.5 commit history transaction identity changed.");
            return;
        }
        entries.Add(entry);
        DurableSaveFiles.AtomicWriteText(HistoryPath, JsonSerializer.Serialize(entries, WalOptions));
    }

    private static void PreserveFile(string path, string preferredPath)
    {
        if (!File.Exists(path)) return;
        var target = preferredPath;
        var suffix = 2;
        while (File.Exists(target)) target = preferredPath + "." + suffix++;
        DurableSaveFiles.CopyDurable(path, target);
    }
}

/// <summary>Small physical-file primitive shared by the V2.5 store and the legacy Godot save adapter.</summary>
public static class DurableSaveFiles
{
    public static void CommitText(string currentPath, string text)
    {
        var current = Path.GetFullPath(currentPath);
        var temporary = current + ".tmp";
        var previous = current + ".previous";
        var backup = current + ".backup";
        WriteText(temporary, text);
        if (File.Exists(current)) File.Replace(temporary, current, previous, true); else File.Move(temporary, current);
        CopyDurable(current, backup);
    }

    public static void WriteText(string path, string text)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is not null) Directory.CreateDirectory(directory);
        var bytes = Encoding.UTF8.GetBytes(text);
        using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    public static void AtomicWriteText(string path, string text)
    {
        var fullPath = Path.GetFullPath(path);
        var temporary = fullPath + ".next";
        WriteText(temporary, text);
        if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null, true); else File.Move(temporary, fullPath);
    }

    public static void CopyDurable(string source, string destination)
    {
        var bytes = File.ReadAllBytes(source);
        var directory = Path.GetDirectoryName(Path.GetFullPath(destination));
        if (directory is not null) Directory.CreateDirectory(directory);
        using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }
}
