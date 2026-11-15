using System.Diagnostics;
using System.Text.Json;
using Upstream.Harness;

namespace Upstream.Runner;

/// <summary>比較結果の 1 行(テスト 1 件)。</summary>
public sealed class ComparisonRow
{
    public string Id { get; set; } = "";
    public string Class { get; set; } = "";
    public string Method { get; set; } = "";

    /// <summary>クラス名を除いたテスト名(Theory の引数を含む)。</summary>
    public string TestName => Id.StartsWith(Class + ".", StringComparison.Ordinal) ? Id[(Class.Length + 1)..] : Id;

    /// <summary>判定(<see cref="Verdicts"/> のいずれか)。</summary>
    public string Verdict { get; set; } = "";

    /// <summary>判定の理由(対象外・置換判定・失敗の内容)。</summary>
    public string Reason { get; set; } = "";

    public string MemoryPackStatus { get; set; } = "";
    public string ApexPackStatus { get; set; } = "";
    public string? MemoryPackError { get; set; }
    public string? ApexPackError { get; set; }

    /// <summary>計測した呼び出しの数(両側で同じときだけ速度・サイズを比べる)。</summary>
    public int Calls { get; set; }

    public double? MemoryPackSerializeNs { get; set; }
    public double? ApexPackSerializeNs { get; set; }
    public double? MemoryPackDeserializeNs { get; set; }
    public double? ApexPackDeserializeNs { get; set; }
    public long? MemoryPackBytes { get; set; }
    public long? ApexPackBytes { get; set; }

    /// <summary>MemoryPack の時間 ÷ ApexPack の時間(1 より大きければ ApexPack が速い)。</summary>
    public double? SerializeSpeedup => Ratio(MemoryPackSerializeNs, ApexPackSerializeNs);

    public double? DeserializeSpeedup => Ratio(MemoryPackDeserializeNs, ApexPackDeserializeNs);

    /// <summary>MemoryPack のバイト数 ÷ ApexPack のバイト数(1 より大きければ ApexPack が小さい)。</summary>
    public double? SizeRatio => MemoryPackBytes is > 0 && ApexPackBytes is > 0 ? (double)MemoryPackBytes / ApexPackBytes : null;

    public List<CallResult> MemoryPackCalls { get; set; } = new();
    public List<CallResult> ApexPackCalls { get; set; } = new();

    private static double? Ratio(double? a, double? b) => a is > 0 && b is > 0 ? a / b : null;
}

public static class Verdicts
{
    public const string Match = "一致";
    public const string Replaced = "置換判定で合格";
    public const string ReplacedFailed = "置換判定で不合格";
    public const string OutOfScope = "対象外";
    public const string DesignDifference = "仕様の違い";
    public const string Failed = "失敗";
    public const string BothFailed = "MemoryPack も失敗";
    public const string Skipped = "スキップ";

    public static readonly string[] All = { Match, Replaced, ReplacedFailed, OutOfScope, DesignDifference, Failed, BothFailed, Skipped };
}

/// <summary>比較全体。</summary>
public sealed class ComparisonReport
{
    public string CreatedUtc { get; set; } = "";
    public string Framework { get; set; } = "";
    public bool Timing { get; set; }
    public List<ComparisonRow> Rows { get; set; } = new();

    public int Count(string verdict) => Rows.Count(r => r.Verdict == verdict);

    /// <summary>速度・サイズを比べられた行(両側で同じ数の呼び出しを測れた)。</summary>
    public IEnumerable<ComparisonRow> Measured => Rows.Where(r => r.SerializeSpeedup is not null || r.DeserializeSpeedup is not null || r.SizeRatio is not null);

    public double? GeoMeanSerializeSpeedup => GeoMean(Measured.Select(r => r.SerializeSpeedup));
    public double? GeoMeanDeserializeSpeedup => GeoMean(Measured.Select(r => r.DeserializeSpeedup));

    /// <summary>合計バイト数の比(MemoryPack ÷ ApexPack)。</summary>
    public double? TotalSizeRatio
    {
        get
        {
            long mp = Measured.Sum(r => r.MemoryPackBytes ?? 0);
            long ap = Measured.Sum(r => r.ApexPackBytes ?? 0);
            return mp > 0 && ap > 0 ? (double)mp / ap : null;
        }
    }

    /// <summary>直列化と逆直列化の合計時間の比(MemoryPack ÷ ApexPack)。</summary>
    public double? TotalTimeRatio
    {
        get
        {
            var rows = Measured.Where(r => r.MemoryPackSerializeNs is > 0 && r.ApexPackSerializeNs is > 0 && r.MemoryPackDeserializeNs is > 0 && r.ApexPackDeserializeNs is > 0).ToList();
            double mp = rows.Sum(r => r.MemoryPackSerializeNs!.Value + r.MemoryPackDeserializeNs!.Value);
            double ap = rows.Sum(r => r.ApexPackSerializeNs!.Value + r.ApexPackDeserializeNs!.Value);
            return mp > 0 && ap > 0 ? mp / ap : null;
        }
    }

    private static double? GeoMean(IEnumerable<double?> values)
    {
        var list = values.Where(v => v is > 0).Select(v => Math.Log(v!.Value)).ToList();
        return list.Count == 0 ? null : Math.Exp(list.Average());
    }

    public static ComparisonReport Load(string path)
        => JsonSerializer.Deserialize<ComparisonReport>(File.ReadAllText(path)) ?? new ComparisonReport();

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>両側のテストを実行して比べる。</summary>
public static class UpstreamComparison
{
    /// <summary>進捗。side は "MemoryPack" / "ApexPack" / "比較"。</summary>
    public readonly record struct Progress(string Side, int Done, int Total, string Message);

    /// <summary>リポジトリのルート(ApexPack.slnx がある場所)を探す。</summary>
    public static string? FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ApexPack.slnx"))) return directory.FullName;
        }
        return null;
    }

    public static string TestAssembly(string root, string project)
        => Path.Combine(root, "tests", project, "bin", "Release", "net9.0", project + ".dll");

    /// <summary>両側をビルドする(Release)。</summary>
    public static async Task BuildAsync(string root, IProgress<Progress>? progress, CancellationToken cancellationToken)
    {
        foreach (var project in new[] { "Upstream.OnMemoryPack", "Upstream.OnApexPack" })
        {
            progress?.Report(new Progress("ビルド", 0, 0, project));
            var (code, output) = await RunProcessAsync("dotnet", $"build \"{Path.Combine(root, "tests", project)}\" -c Release -nologo -v q", root, null, cancellationToken).ConfigureAwait(false);
            if (code != 0) throw new InvalidOperationException($"{project} のビルドに失敗しました。\n{output}");
        }
    }

    /// <summary>両側のテストを実行し(MemoryPack → ApexPack の順。同時には動かさない)、比べる。</summary>
    public static async Task<ComparisonReport> RunAsync(string root, bool timing, string? filter, IProgress<Progress>? progress, CancellationToken cancellationToken)
    {
        // 中間の JSON はリポジトリ内の obj に置く(Git の対象外)
        var work = Path.Combine(root, "tests", "Upstream.Runner", "obj", "harness");
        Directory.CreateDirectory(work);
        var mp = await RunSideAsync(root, "Upstream.OnMemoryPack", "MemoryPack", Path.Combine(work, "memorypack.json"), timing, filter, progress, cancellationToken).ConfigureAwait(false);
        var apex = await RunSideAsync(root, "Upstream.OnApexPack", "ApexPack", Path.Combine(work, "apexpack.json"), timing, filter, progress, cancellationToken).ConfigureAwait(false);
        progress?.Report(new Progress("比較", 0, 0, "結果を突き合わせています"));
        return Compare(mp, apex, timing);
    }

    private static async Task<HarnessRun> RunSideAsync(string root, string project, string side, string output, bool timing, string? filter, IProgress<Progress>? progress, CancellationToken cancellationToken)
    {
        var dll = TestAssembly(root, project);
        if (!File.Exists(dll)) throw new FileNotFoundException($"{dll} がありません。先にビルドしてください。", dll);
        if (File.Exists(output)) File.Delete(output);

        var arguments = $"\"{dll}\" --harness \"{output}\"";
        if (!timing) arguments += " --no-timing";
        if (!string.IsNullOrEmpty(filter)) arguments += $" --filter \"{filter}\"";

        var (code, log) = await RunProcessAsync("dotnet", arguments, Path.GetDirectoryName(dll)!, line =>
        {
            // "progress 12/268 Class.Method"
            if (!line.StartsWith("progress ", StringComparison.Ordinal)) return;
            var parts = line.Split(' ', 3);
            var counts = parts[1].Split('/');
            if (counts.Length == 2 && int.TryParse(counts[0], out int done) && int.TryParse(counts[1], out int total))
            {
                progress?.Report(new Progress(side, done, total, parts.Length > 2 ? parts[2] : ""));
            }
        }, cancellationToken).ConfigureAwait(false);

        if (code != 0 || !File.Exists(output))
        {
            throw new InvalidOperationException($"{side} 側のテスト実行が終了コード {code} で止まりました(プロセスごと落ちた可能性があります)。\n{Tail(log, 40)}");
        }
        return JsonSerializer.Deserialize<HarnessRun>(File.ReadAllText(output)) ?? new HarnessRun();
    }

    /// <summary>両側の結果を突き合わせて分類する。</summary>
    public static ComparisonReport Compare(HarnessRun memoryPack, HarnessRun apexPack, bool timing)
    {
        var report = new ComparisonReport
        {
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            Framework = apexPack.Framework,
            Timing = timing,
        };
        var apexById = apexPack.Tests.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        var ids = memoryPack.Tests.Select(t => t.Id).Concat(apexPack.Tests.Select(t => t.Id)).Distinct().ToList();
        var mpById = memoryPack.Tests.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (var id in ids)
        {
            mpById.TryGetValue(id, out var m);
            apexById.TryGetValue(id, out var a);
            report.Rows.Add(Classify(id, m, a));
        }
        return report;
    }

    private static ComparisonRow Classify(string id, TestResult? m, TestResult? a)
    {
        var row = new ComparisonRow
        {
            Id = id,
            Class = (m ?? a)!.Class,
            Method = (m ?? a)!.Method,
            MemoryPackStatus = m?.Status ?? "なし",
            ApexPackStatus = a?.Status ?? "なし",
            MemoryPackError = m is { Status: "fail" } ? $"{ShortType(m.ErrorType)}: {m.ErrorMessage}" : null,
            ApexPackError = a is { Status: "fail" } ? $"{ShortType(a.ErrorType)}: {a.ErrorMessage}" : null,
            MemoryPackCalls = m?.Calls ?? new(),
            ApexPackCalls = a?.Calls ?? new(),
        };

        var outOfScope = Classification.Match(Classification.OutOfScope, id);
        var format = Classification.Match(Classification.FormatSpecific, id);
        var design = Classification.Match(Classification.DesignDifference, id);

        if (m is null || a is null)
        {
            row.Verdict = Verdicts.Failed;
            row.Reason = m is null ? "MemoryPack 側に同じテストがない" : "ApexPack 側に同じテストがない";
        }
        else if (m.Status == "skip" || a.Status == "skip")
        {
            row.Verdict = Verdicts.Skipped;
            row.Reason = m.SkipReason ?? a.SkipReason ?? "";
        }
        else if (m.Status == "fail")
        {
            row.Verdict = Verdicts.BothFailed;
            row.Reason = "MemoryPack 側でも失敗(参考)";
        }
        else if (a.Feature is not null)
        {
            row.Verdict = Verdicts.OutOfScope;
            row.Reason = a.Feature;
        }
        else if (outOfScope is not null)
        {
            row.Verdict = Verdicts.OutOfScope;
            row.Reason = outOfScope.Reason;
        }
        else if (design is not null && a.Status == "fail")
        {
            row.Verdict = Verdicts.DesignDifference;
            row.Reason = design.Reason;
        }
        else if (a.Status == "pass")
        {
            row.Verdict = Verdicts.Match;
        }
        else if (format is not null)
        {
            // 置換判定: テスト中の呼び出しを再生し、すべてが再現性と往復を満たすこと。呼び出しがなければ合格にしない
            var calls = a.Calls;
            bool ok = calls.Count > 0 && calls.All(c => c.ReplayError is null && c.Deterministic == true && c.RoundTrip == true);
            row.Verdict = ok ? Verdicts.Replaced : Verdicts.ReplacedFailed;
            row.Reason = ok
                ? $"{format.Reason}。代わりに {calls.Count} 件の呼び出しで「毎回同じバイト列」「往復して同じ値」を確認"
                : calls.Count == 0
                    ? $"{format.Reason}。確かめる呼び出しが記録されていない"
                    : $"{format.Reason}。再現性または往復を満たさない呼び出しがある";
        }
        else
        {
            row.Verdict = Verdicts.Failed;
            row.Reason = row.ApexPackError ?? "";
        }

        FillMeasurements(row);
        return row;
    }

    /// <summary>両側で同じ数の呼び出しを測れたテストだけ、時間とサイズを合計して比べる。</summary>
    private static void FillMeasurements(ComparisonRow row)
    {
        var m = row.MemoryPackCalls;
        var a = row.ApexPackCalls;
        row.Calls = Math.Min(m.Count, a.Count);
        if (m.Count == 0 || m.Count != a.Count) return;
        if (row.Verdict is not (Verdicts.Match or Verdicts.Replaced)) return;
        if (m.Any(c => c.ReplayError is not null) || a.Any(c => c.ReplayError is not null)) return;

        row.MemoryPackBytes = m.Sum(c => (long)c.Bytes);
        row.ApexPackBytes = a.Sum(c => (long)c.Bytes);
        if (m.All(c => c.SerializeNs > 0) && a.All(c => c.SerializeNs > 0))
        {
            row.MemoryPackSerializeNs = m.Sum(c => c.SerializeNs);
            row.ApexPackSerializeNs = a.Sum(c => c.SerializeNs);
        }
        if (m.All(c => c.DeserializeNs > 0) && a.All(c => c.DeserializeNs > 0))
        {
            row.MemoryPackDeserializeNs = m.Sum(c => c.DeserializeNs);
            row.ApexPackDeserializeNs = a.Sum(c => c.DeserializeNs);
        }
    }

    private static string ShortType(string? type) => type is null ? "" : type[(type.LastIndexOf('.') + 1)..];

    private static string Tail(string text, int lines)
    {
        var all = text.Split('\n');
        return string.Join('\n', all.Skip(Math.Max(0, all.Length - lines)));
    }

    private static async Task<(int Code, string Output)> RunProcessAsync(string file, string arguments, string workingDirectory, Action<string>? onLine, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(file, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        using var process = new Process { StartInfo = info };
        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (output) output.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (output) output.AppendLine(e.Data);
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        lock (output) return (process.ExitCode, output.ToString());
    }
}
