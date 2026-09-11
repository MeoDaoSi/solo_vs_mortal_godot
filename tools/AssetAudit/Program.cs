using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// ──────────────────────────────────────────────────────────────────────────────
// Solo vs Mortal — Phase 0 Asset Audit Tool
// Reads asset-catalog.v2.5.json + presentation-visual-metrics.v2.5.json + PNGs,
// computes per-frame hashes, opaque bounds, drift, and emits reports.
// ──────────────────────────────────────────────────────────────────────────────

var projectRoot = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var catalogPath = Path.Combine(projectRoot, "data", "v2.5", "asset-catalog.v2.5.json");
var metricsPath = Path.Combine(projectRoot, "data", "v2.5", "presentation-visual-metrics.v2.5.json");
var outputDir = Path.Combine(projectRoot, "docs", "V2.5");

if (!File.Exists(catalogPath)) { Console.Error.WriteLine($"Catalog not found: {catalogPath}"); return 1; }
if (!File.Exists(metricsPath)) { Console.Error.WriteLine($"Metrics not found: {metricsPath}"); return 1; }

Console.WriteLine($"Project root: {projectRoot}");

// ── Parse catalog ────────────────────────────────────────────────────────────
var catalogJson = JsonDocument.Parse(File.ReadAllText(catalogPath));
var catalogRoot = catalogJson.RootElement;
var catalogVersion = catalogRoot.GetProperty("catalogVersion").GetString()!;
var catalogComplete = catalogRoot.GetProperty("complete").GetBoolean();

var assets = new List<AssetEntry>();
foreach (var item in catalogRoot.GetProperty("assets").EnumerateArray())
{
    var assetId = item.GetProperty("assetId").GetString()!;
    var relativeFile = item.GetProperty("relativeFile").GetString()!;
    var absoluteFile = Path.GetFullPath(Path.Combine(projectRoot, relativeFile.Replace('/', Path.DirectorySeparatorChar)));
    var frameSizeArr = item.GetProperty("frameSize");
    var pivotArr = item.GetProperty("pivot");
    var frames = new List<FrameEntry>();
    foreach (var f in item.GetProperty("frames").EnumerateArray())
    {
        var rect = f.GetProperty("rect");
        frames.Add(new FrameEntry(rect[0].GetInt32(), rect[1].GetInt32(), rect[2].GetInt32(), rect[3].GetInt32(), f.GetProperty("durationMs").GetInt32()));
    }
    var qa = item.GetProperty("qa");
    var layering = item.GetProperty("layering");
    assets.Add(new AssetEntry(assetId, absoluteFile, item.GetProperty("sha256").GetString()!,
        item.GetProperty("role").GetString()!, item.GetProperty("representation").GetString()!,
        item.GetProperty("rank").GetInt32(), item.GetProperty("direction").GetString()!,
        item.GetProperty("clip").GetString()!, frameSizeArr[0].GetInt32(), frameSizeArr[1].GetInt32(),
        pivotArr[0].GetDouble(), pivotArr[1].GetInt32(), frames,
        qa.GetProperty("technical").GetBoolean(), qa.GetProperty("visual").GetBoolean(),
        qa.GetProperty("inEngine").GetBoolean(), item.GetProperty("approvalStatus").GetString()!,
        layering.GetProperty("zIndex").GetInt32(), layering.GetProperty("ySortEnabled").GetBoolean()));
}
Console.WriteLine($"Loaded {assets.Count} catalog entries.");

// ── Parse visual metrics ─────────────────────────────────────────────────────
var metricsJson = JsonDocument.Parse(File.ReadAllText(metricsPath));
var metricsDict = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
foreach (var m in metricsJson.RootElement.GetProperty("assets").EnumerateArray())
{
    var aid = m.GetProperty("assetId").GetString()!;
    metricsDict[aid] = new MetricEntry(aid,
        m.GetProperty("canvasSize")[0].GetInt32(), m.GetProperty("canvasSize")[1].GetInt32(),
        m.GetProperty("opaqueBounds")[0].GetInt32(), m.GetProperty("opaqueBounds")[1].GetInt32(),
        m.GetProperty("opaqueBounds")[2].GetInt32(), m.GetProperty("opaqueBounds")[3].GetInt32(),
        m.GetProperty("groundPivot")[0].GetInt32(), m.GetProperty("groundPivot")[1].GetInt32(),
        m.GetProperty("intendedFootprint")[0].GetInt32(), m.GetProperty("intendedFootprint")[1].GetInt32());
}
Console.WriteLine($"Loaded {metricsDict.Count} visual metrics.");

// ── Load PNGs ────────────────────────────────────────────────────────────────
Console.WriteLine("Loading PNG files...");
var imageCache = new Dictionary<string, int[]>(StringComparer.Ordinal);
foreach (var asset in assets)
{
    if (imageCache.ContainsKey(asset.AbsoluteFile) || !File.Exists(asset.AbsoluteFile)) continue;
    try { imageCache[asset.AbsoluteFile] = LoadPngRgba(asset.AbsoluteFile).Rgba; }
    catch (Exception ex) { Console.WriteLine($"  WARN: Cannot parse '{asset.AbsoluteFile}': {ex.Message}"); }
}
Console.WriteLine($"Loaded {imageCache.Count} unique PNG images.");

// ── Analyze each asset ───────────────────────────────────────────────────────
var results = new List<AuditAssetResult>();
var issues = new List<AuditIssue>();

foreach (var asset in assets)
{
    var result = new AuditAssetResult
    {
        AssetId = asset.AssetId, Role = asset.Role, Representation = asset.Representation,
        Rank = asset.Rank, Direction = asset.Direction, Clip = asset.Clip,
        FrameSizeW = asset.FrameW, FrameSizeH = asset.FrameH, FrameCount = asset.Frames.Count,
        PivotX = asset.PivotX, PivotY = asset.PivotY,
        TechnicalQa = asset.TechnicalQa, VisualQa = asset.VisualQa, InEngineQa = asset.InEngineQa,
        ApprovalStatus = asset.ApprovalStatus, ZIndex = asset.ZIndex, YSortEnabled = asset.YSortEnabled,
        FileExists = File.Exists(asset.AbsoluteFile), Frames = new List<FrameAuditResult>()
    };

    if (imageCache.TryGetValue(asset.AbsoluteFile, out var rgba))
    {
        var (pngW, pngH) = GetPngDimensions(asset.AbsoluteFile);
        result.SourcePngWidth = pngW; result.SourcePngHeight = pngH;
        var frameHashes = new List<string>();
        foreach (var frame in asset.Frames)
        {
            var hash = ComputeRegionHash(rgba, pngW, frame.X, frame.Y, frame.W, frame.H);
            frameHashes.Add(hash);
            var ob = ComputeOpaqueBounds(rgba, pngW, frame.X, frame.Y, frame.W, frame.H);
            result.Frames.Add(new FrameAuditResult { RectX = frame.X, RectY = frame.Y, RectW = frame.W, RectH = frame.H, DurationMs = frame.DurationMs, Sha256 = hash, OpaqueX = ob.X, OpaqueY = ob.Y, OpaqueW = ob.W, OpaqueH = ob.H });
        }
        result.UniqueFrameCount = frameHashes.Distinct().Count();
        result.AllFramesIdentical = result.UniqueFrameCount == 1 && asset.Frames.Count > 1;
        result.FrameHashes = frameHashes;
        if (result.AllFramesIdentical)
            issues.Add(new AuditIssue { Severity = "HIGH", Category = "IDENTICAL_FRAMES", AssetId = asset.AssetId, Message = $"All {asset.Frames.Count} frames are identical." });
        // Opaque bounds drift
        if (result.Frames.Count > 1)
        {
            var first = result.Frames[0];
            for (int i = 1; i < result.Frames.Count; i++)
            {
                var f = result.Frames[i];
                if (Math.Abs(f.OpaqueX - first.OpaqueX) > 2 || Math.Abs(f.OpaqueY - first.OpaqueY) > 2 || Math.Abs(f.OpaqueW - first.OpaqueW) > 2 || Math.Abs(f.OpaqueH - first.OpaqueH) > 2)
                { issues.Add(new AuditIssue { Severity = "MEDIUM", Category = "OPAQUE_BOUNDS_DRIFT", AssetId = asset.AssetId, Message = $"Frame 0=({first.OpaqueX},{first.OpaqueY},{first.OpaqueW},{first.OpaqueH}) vs frame {i}=({f.OpaqueX},{f.OpaqueY},{f.OpaqueW},{f.OpaqueH})." }); break; }
            }
        }
    }

    result.HasVisualMetrics = metricsDict.ContainsKey(asset.AssetId);
    if (result.HasVisualMetrics && metricsDict.TryGetValue(asset.AssetId, out var metric))
    {
        result.MetricsCanvasW = metric.CanvasW; result.MetricsCanvasH = metric.CanvasH;
        result.MetricsOpaqueX = metric.OpaqueX; result.MetricsOpaqueY = metric.OpaqueY;
        result.MetricsOpaqueW = metric.OpaqueW; result.MetricsOpaqueH = metric.OpaqueH;
        result.MetricsPivotX = metric.PivotX; result.MetricsPivotY = metric.PivotY;
        result.MetricsFootprintW = metric.FootprintW; result.MetricsFootprintH = metric.FootprintH;
        if (metric.OpaqueW > 0 && metric.OpaqueH > 0)
            result.UniformScale = Math.Min((double)metric.FootprintW / metric.OpaqueW, (double)metric.FootprintH / metric.OpaqueH);
        result.FinalVisibleHeight = metric.OpaqueH * result.UniformScale;
        result.FinalVisibleWidth = metric.OpaqueW * result.UniformScale;
    }
    if (!result.HasVisualMetrics)
        issues.Add(new AuditIssue { Severity = "MEDIUM", Category = "MISSING_METRICS", AssetId = asset.AssetId, Message = "No entry in presentation-visual-metrics.v2.5.json." });
    if (result.HasVisualMetrics && (result.MetricsFootprintW != result.FrameSizeW || result.MetricsFootprintH != result.FrameSizeH))
        issues.Add(new AuditIssue { Severity = "INFO", Category = "SCALE_OWNERSHIP", AssetId = asset.AssetId, Message = $"Footprint ({result.MetricsFootprintW}x{result.MetricsFootprintH}) != canvas ({result.FrameSizeW}x{result.FrameSizeH})." });

    results.Add(result);
}

// ── Cross-asset analysis ─────────────────────────────────────────────────────
var actorGroups = assets.Where(a => a.Role == "actor" && a.Clip != "static").GroupBy(a => PrefixBeforeClip(a.AssetId));
foreach (var g in actorGroups)
{
    var dirs = g.Select(a => a.Direction).Distinct().ToHashSet();
    var clips = g.Select(a => a.Clip).Distinct().ToHashSet();
    foreach (var dir in new[] { "s", "n", "e", "w" }) if (!dirs.Contains(dir)) issues.Add(new AuditIssue { Severity = "HIGH", Category = "MISSING_DIRECTION", AssetId = g.Key, Message = $"Missing direction '{dir}'." });
    // Weapon poses are attack-only overlays by design — skip MISSING_CLIP for them
var requiredClips = new[] { "idle", "move", "attack" };
foreach (var clip in requiredClips)
{
    if (clips.Contains(clip)) continue;
    if (g.Key.Contains("weaponpose")) continue;
    issues.Add(new AuditIssue { Severity = "HIGH", Category = "MISSING_CLIP", AssetId = g.Key, Message = $"Missing clip '{clip}'." });
}
}

// Pivot drift
var pivotGroups = results.Where(r => r.HasVisualMetrics).GroupBy(r => PrefixBeforeClip(r.AssetId));
foreach (var g in pivotGroups)
{
    var pivots = g.Select(r => (r.MetricsPivotX, r.MetricsPivotY)).Distinct().ToList();
    if (pivots.Count > 1) issues.Add(new AuditIssue { Severity = "MEDIUM", Category = "PIVOT_DRIFT", AssetId = g.Key, Message = $"Multiple pivots: {string.Join(", ", pivots.Select(p => $"({p.Item1},{p.Item2})"))}" });
}

Console.WriteLine($"Analysis: {results.Count} assets, {issues.Count} issues.");

// ── JSON report ──────────────────────────────────────────────────────────────
var jsonReport = new JsonReport { GeneratedAt = DateTime.UtcNow.ToString("o"), CatalogVersion = catalogVersion, CatalogComplete = catalogComplete, TotalAssets = results.Count, AssetsWithMetrics = results.Count(r => r.HasVisualMetrics), AssetsMissingMetrics = results.Count(r => !r.HasVisualMetrics), IdenticalFrameAssets = results.Count(r => r.AllFramesIdentical), Issues = issues, Assets = results };
Directory.CreateDirectory(outputDir);
var jsonPath = Path.Combine(outputDir, "VISUAL_ASSET_AUDIT_2026-09-11.json");
File.WriteAllText(jsonPath, JsonSerializer.Serialize(jsonReport, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }));
Console.WriteLine($"JSON: {jsonPath}");

// ── Markdown report ──────────────────────────────────────────────────────────
var md = new StringBuilder();
md.AppendLine("# Solo vs Mortal — Visual Asset Audit Report");
md.AppendLine($"\n**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
md.AppendLine($"**Catalog:** {catalogVersion} (complete={catalogComplete})");
md.AppendLine($"**Total assets:** {results.Count}");
md.AppendLine($"**With visual metrics:** {results.Count(r => r.HasVisualMetrics)}");
md.AppendLine($"**Missing visual metrics:** {results.Count(r => !r.HasVisualMetrics)}");
md.AppendLine($"**Identical frames:** {results.Count(r => r.AllFramesIdentical)}");
md.AppendLine($"**Issues:** {issues.Count}\n");

md.AppendLine("## Issues\n");
if (issues.Count == 0) md.AppendLine("None.\n");
else
{
    md.AppendLine("| Severity | Category | AssetId | Message |");
    md.AppendLine("|----------|----------|---------|---------|");
    foreach (var i in issues.OrderByDescending(i => i.Severity)) md.AppendLine($"| {i.Severity} | {i.Category} | `{i.AssetId}` | {i.Message} |");
    md.AppendLine();
}

md.AppendLine("## Full Asset Table\n");
md.AppendLine("| AssetId | Role | Rep | Rank | Dir | Clip | PNG | FrameSize | Frames | Unique | OpaqueBounds | Pivot | Footprint | Scale | VisH | Metrics | Approval |");
md.AppendLine("|---------|------|-----|------|-----|------|-----|-----------|--------|--------|-------------|-------|-----------|-------|------|---------|----------|");
foreach (var r in results.OrderBy(r => r.AssetId))
{
    var ob = r.HasVisualMetrics ? $"({r.MetricsOpaqueX},{r.MetricsOpaqueY},{r.MetricsOpaqueW},{r.MetricsOpaqueH})" : "—";
    var pv = r.HasVisualMetrics ? $"({r.MetricsPivotX},{r.MetricsPivotY})" : $"({r.PivotX},{r.PivotY})";
    var fp = r.HasVisualMetrics ? $"{r.MetricsFootprintW}x{r.MetricsFootprintH}" : "—";
    var sc = r.HasVisualMetrics ? $"{r.UniformScale:0.###}" : "—";
    var vh = r.HasVisualMetrics ? $"{r.FinalVisibleHeight:0.#}" : "—";
    var id = r.AllFramesIdentical ? " **ID**" : "";
    md.AppendLine($"| `{r.AssetId}` | {r.Role} | {r.Representation} | {r.Rank} | {r.Direction} | {r.Clip} | {r.SourcePngWidth}x{r.SourcePngHeight} | {r.FrameSizeW}x{r.FrameSizeH} | {r.FrameCount} | {r.UniqueFrameCount}/{r.FrameCount}{id} | {ob} | {pv} | {fp} | {sc} | {vh} | {(r.HasVisualMetrics ? "Y" : "N")} | {r.ApprovalStatus} |");
}
md.AppendLine();

md.AppendLine("## Direction Coverage (Animated Actors)\n");
md.AppendLine("| Prefix | s | n | e | w | Clips |");
md.AppendLine("|--------|---|---|---|---|-------|");
foreach (var g in actorGroups.OrderBy(g => g.Key))
{
    var d = g.Select(a => a.Direction).Distinct().ToHashSet();
    var c = g.Select(a => a.Clip).Distinct().OrderBy(c => c);
    md.AppendLine($"| `{g.Key}` | {(d.Contains("s") ? "Y" : "N")} | {(d.Contains("n") ? "Y" : "N")} | {(d.Contains("e") ? "Y" : "N")} | {(d.Contains("w") ? "Y" : "N")} | {string.Join(", ", c)} |");
}
md.AppendLine();

var staticActors = assets.Where(a => a.Role == "actor" && a.Clip == "static").ToList();
if (staticActors.Count > 0)
{
    md.AppendLine("## Static Reuse Actors (excluded from direction/clip coverage)\n");
    md.AppendLine("| AssetId | Direction | Approval |");
    md.AppendLine("|---------|-----------|----------|");
    foreach (var a in staticActors.OrderBy(a => a.AssetId))
        md.AppendLine($"| `{a.AssetId}` | {a.Direction} | {a.ApprovalStatus} |");
    md.AppendLine();
}

var identical = results.Where(r => r.AllFramesIdentical).ToList();
if (identical.Count > 0)
{
    md.AppendLine("## Identical Frame Assets\n");
    md.AppendLine("| AssetId | Frames | Hash |");
    md.AppendLine("|---------|--------|------|");
    foreach (var r in identical) md.AppendLine($"| `{r.AssetId}` | {r.FrameCount} | {r.FrameHashes.FirstOrDefault()?[..16]}... |");
    md.AppendLine();
}

md.AppendLine("## Scale Hierarchy\n");
var playerH = results.FirstOrDefault(r => r.AssetId == "player.base.idle.s")?.FinalVisibleHeight ?? 50;
md.AppendLine($"Player baseline: {playerH:0.#} px\n");
md.AppendLine("| AssetId | VisH | Ratio | Category |");
md.AppendLine("|---------|------|-------|----------|");
foreach (var r in results.Where(r => r.HasVisualMetrics).OrderByDescending(r => r.FinalVisibleHeight))
    md.AppendLine($"| `{r.AssetId}` | {r.FinalVisibleHeight:0.#} | {(playerH > 0 ? r.FinalVisibleHeight / playerH : 0):0.00} | {r.Role}:{r.Representation} |");

var mdPath = Path.Combine(outputDir, "VISUAL_ASSET_AUDIT_2026-09-11.md");
File.WriteAllText(mdPath, md.ToString());
Console.WriteLine($"Markdown: {mdPath}");
Console.WriteLine("\nDone.");
return 0;

// ──────────────────────────────────────────────────────────────────────────────
// Static helpers (must precede type declarations in top-level statements)
// ──────────────────────────────────────────────────────────────────────────────

static string PrefixBeforeClip(string assetId)
{
    var parts = assetId.Split('.');
    for (int i = parts.Length - 1; i >= 1; i--)
        if (parts[i] is "idle" or "move" or "attack" or "hit" or "death" or "disperse" or "summon" or "recall" or "pickup" or "banner")
            return string.Join('.', parts[..i]);
    return assetId;
}

static (int Width, int Height, int[] Rgba) LoadPngRgba(string path)
{
    var bytes = File.ReadAllBytes(path);
    int pos = 8, width = 0, height = 0, bitDepth = 0, colorType = 0;
    var idatChunks = new List<byte[]>();
    while (pos < bytes.Length)
    {
        if (pos + 8 > bytes.Length) break;
        var chunkLen = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos, 4));
        var chunkType = Encoding.ASCII.GetString(bytes, pos + 4, 4);
        pos += 8;
        if (chunkType == "IHDR") { width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos)); height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos + 4)); bitDepth = bytes[pos + 8]; colorType = bytes[pos + 9]; }
        else if (chunkType == "IDAT") { var d = new byte[chunkLen]; Array.Copy(bytes, pos, d, 0, chunkLen); idatChunks.Add(d); }
        else if (chunkType == "IEND") break;
        pos += chunkLen + 4;
    }
    var compressed = new MemoryStream(); foreach (var c in idatChunks) compressed.Write(c); compressed.Position = 0;
    using var deflate = new ZLibStream(compressed, CompressionMode.Decompress);
    using var raw = new MemoryStream(); deflate.CopyTo(raw); var scanlines = raw.ToArray();
    int bpp = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 4 };
    int stride = width * bpp; var rgba = new int[width * height]; int si = 0;
    for (int y = 0; y < height; y++)
    {
        if (si >= scanlines.Length) break;
        var filter = (PNGFilter)scanlines[si++];
        var rawLine = new byte[stride]; Array.Copy(scanlines, si, rawLine, 0, Math.Min(stride, scanlines.Length - si)); si += stride;
        var recon = new byte[stride];
        for (int x = 0; x < stride; x++)
        {
            int left = x >= bpp ? recon[x - bpp] : 0;
            int above = y > 0 ? GetByteFromPacked(rgba[(y - 1) * width + (x / bpp)], x % bpp) : 0;
            int aboveLeft = y > 0 && x >= bpp ? GetByteFromPacked(rgba[(y - 1) * width + ((x - bpp) / bpp)], (x - bpp) % bpp) : 0;
            recon[x] = filter switch
            {
                PNGFilter.None => rawLine[x],
                PNGFilter.Sub => (byte)((rawLine[x] + left) & 0xFF),
                PNGFilter.Up => (byte)((rawLine[x] + above) & 0xFF),
                PNGFilter.Average => (byte)((rawLine[x] + (left + above) / 2) & 0xFF),
                PNGFilter.Paeth => (byte)((rawLine[x] + PaethPredictor(left, above, aboveLeft)) & 0xFF),
                _ => rawLine[x]
            };
        }
        for (int x = 0; x < width; x++)
        {
            int r, g, b, a;
            switch (colorType)
            {
                case 0: r = g = b = recon[x]; a = 255; break;
                case 2: r = recon[x * 3]; g = recon[x * 3 + 1]; b = recon[x * 3 + 2]; a = 255; break;
                case 4: r = g = b = recon[x * 2]; a = recon[x * 2 + 1]; break;
                case 6: r = recon[x * 4]; g = recon[x * 4 + 1]; b = recon[x * 4 + 2]; a = recon[x * 4 + 3]; break;
                default: r = g = b = a = 0; break;
            }
            rgba[y * width + x] = (a << 24) | (b << 16) | (g << 8) | r;
        }
    }
    return (width, height, rgba);
}

static int GetByteFromPacked(int pixel, int channel) => channel switch { 0 => pixel & 0xFF, 1 => (pixel >> 8) & 0xFF, 2 => (pixel >> 16) & 0xFF, 3 => (pixel >> 24) & 0xFF, _ => 0 };
static int PaethPredictor(int left, int above, int aboveLeft) { int p = left + above - aboveLeft; int pa = Math.Abs(p - left); int pb = Math.Abs(p - above); int pc = Math.Abs(p - aboveLeft); return pa <= pb && pa <= pc ? left : pb <= pc ? above : aboveLeft; }
static (int Width, int Height) GetPngDimensions(string path)
{
    using var fs = File.OpenRead(path); Span<byte> sig = stackalloc byte[8]; fs.ReadExactly(sig);
    Span<byte> ihdr = stackalloc byte[21]; fs.ReadExactly(ihdr);
    return (BinaryPrimitives.ReadInt32BigEndian(ihdr[8..12]), BinaryPrimitives.ReadInt32BigEndian(ihdr[12..16]));
}

static string ComputeRegionHash(int[] rgba, int pngW, int rx, int ry, int rw, int rh)
{
    using var sha = SHA256.Create(); var buf = new byte[rw * rh * 4];
    for (int y = 0; y < rh; y++) { int sy = ry + y; if (sy < 0 || sy >= rgba.Length / pngW) continue;
        for (int x = 0; x < rw; x++) { int sx = rx + x; if (sx < 0 || sx >= pngW) continue;
            var p = rgba[sy * pngW + sx]; var o = (y * rw + x) * 4; buf[o] = (byte)(p & 0xFF); buf[o + 1] = (byte)((p >> 8) & 0xFF); buf[o + 2] = (byte)((p >> 16) & 0xFF); buf[o + 3] = (byte)((p >> 24) & 0xFF); } }
    return Convert.ToHexString(sha.ComputeHash(buf)).ToLowerInvariant();
}

static FrameOpaqueBounds ComputeOpaqueBounds(int[] rgba, int pngW, int rx, int ry, int rw, int rh)
{
    int minX = rw, minY = rh, maxX = 0, maxY = 0; bool found = false;
    for (int y = 0; y < rh; y++) { int sy = ry + y; if (sy < 0 || sy >= rgba.Length / pngW) continue;
        for (int x = 0; x < rw; x++) { int sx = rx + x; if (sx < 0 || sx >= pngW) continue;
            if (((rgba[sy * pngW + sx] >> 24) & 0xFF) > 0) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; found = true; } } }
    return found ? new FrameOpaqueBounds(minX, minY, maxX - minX + 1, maxY - minY + 1) : default;
}

enum PNGFilter : byte { None = 0, Sub = 1, Up = 2, Average = 3, Paeth = 4 }

// ──────────────────────────────────────────────────────────────────────────────
// Type declarations
// ──────────────────────────────────────────────────────────────────────────────

record FrameEntry(int X, int Y, int W, int H, int DurationMs);
record AssetEntry(string AssetId, string AbsoluteFile, string Sha256, string Role, string Representation, int Rank, string Direction, string Clip, int FrameW, int FrameH, double PivotX, int PivotY, List<FrameEntry> Frames, bool TechnicalQa, bool VisualQa, bool InEngineQa, string ApprovalStatus, int ZIndex, bool YSortEnabled);
record MetricEntry(string AssetId, int CanvasW, int CanvasH, int OpaqueX, int OpaqueY, int OpaqueW, int OpaqueH, int PivotX, int PivotY, int FootprintW, int FootprintH);
record struct FrameOpaqueBounds(int X, int Y, int W, int H);

class AuditAssetResult
{
    public string AssetId { get; set; } = ""; public string Role { get; set; } = ""; public string Representation { get; set; } = "";
    public int Rank { get; set; } public string Direction { get; set; } = ""; public string Clip { get; set; } = "";
    public int SourcePngWidth { get; set; } public int SourcePngHeight { get; set; }
    public int FrameSizeW { get; set; } public int FrameSizeH { get; set; } public int FrameCount { get; set; }
    public int UniqueFrameCount { get; set; } public bool AllFramesIdentical { get; set; }
    public double PivotX { get; set; } public int PivotY { get; set; }
    public bool TechnicalQa { get; set; } public bool VisualQa { get; set; } public bool InEngineQa { get; set; }
    public string ApprovalStatus { get; set; } = ""; public int ZIndex { get; set; } public bool YSortEnabled { get; set; }
    public bool FileExists { get; set; } public bool HasVisualMetrics { get; set; }
    public int MetricsCanvasW { get; set; } public int MetricsCanvasH { get; set; }
    public int MetricsOpaqueX { get; set; } public int MetricsOpaqueY { get; set; }
    public int MetricsOpaqueW { get; set; } public int MetricsOpaqueH { get; set; }
    public int MetricsPivotX { get; set; } public int MetricsPivotY { get; set; }
    public int MetricsFootprintW { get; set; } public int MetricsFootprintH { get; set; }
    public double UniformScale { get; set; } public double FinalVisibleHeight { get; set; } public double FinalVisibleWidth { get; set; }
    public List<FrameAuditResult> Frames { get; set; } = new();
    [JsonIgnore] public List<string> FrameHashes { get; set; } = new();
}

class FrameAuditResult
{
    public int RectX { get; set; } public int RectY { get; set; } public int RectW { get; set; } public int RectH { get; set; }
    public int DurationMs { get; set; } public string Sha256 { get; set; } = "";
    public int OpaqueX { get; set; } public int OpaqueY { get; set; } public int OpaqueW { get; set; } public int OpaqueH { get; set; }
}

class AuditIssue { public string Severity { get; set; } = ""; public string Category { get; set; } = ""; public string AssetId { get; set; } = ""; public string Message { get; set; } = ""; }

class JsonReport
{
    public string GeneratedAt { get; set; } = ""; public string CatalogVersion { get; set; } = ""; public bool CatalogComplete { get; set; }
    public int TotalAssets { get; set; } public int AssetsWithMetrics { get; set; } public int AssetsMissingMetrics { get; set; } public int IdenticalFrameAssets { get; set; }
    public List<AuditIssue> Issues { get; set; } = new(); public List<AuditAssetResult> Assets { get; set; } = new();
}
