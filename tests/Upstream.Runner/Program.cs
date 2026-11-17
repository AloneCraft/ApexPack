using System.Globalization;
using Upstream.Runner;

// 使い方: dotnet run --project tests/Upstream.Runner -c Release -f net9.0 -- [--build] [--no-timing] [--filter 文字列] [--out upstream_report.json]
// 両側のテストを別プロセスで実行して比べ、結果を JSON に書いて集計を表示する。

Console.OutputEncoding = System.Text.Encoding.UTF8;
bool build = args.Contains("--build");
bool timing = !args.Contains("--no-timing");
string? filter = Value("--filter");
var root = UpstreamComparison.FindRepositoryRoot(AppContext.BaseDirectory) ?? UpstreamComparison.FindRepositoryRoot(Environment.CurrentDirectory)
    ?? throw new InvalidOperationException("ApexPack.slnx が見つかりません。リポジトリの中で実行してください。");
string output = Value("--out") ?? Path.Combine(root, "upstream_report.json");

var progress = new Progress<UpstreamComparison.Progress>(p =>
{
    if (p.Total > 0 && p.Done % 25 != 0 && p.Done != p.Total) return;
    Console.WriteLine(p.Total > 0 ? $"[{p.Side}] {p.Done}/{p.Total} {p.Message}" : $"[{p.Side}] {p.Message}");
});

if (build) await UpstreamComparison.BuildAsync(root, progress, CancellationToken.None);
var report = await UpstreamComparison.RunAsync(root, timing, filter, progress, CancellationToken.None);
report.Save(output);

Console.WriteLine();
foreach (var verdict in Verdicts.All)
{
    Console.WriteLine($"{verdict,-16} {report.Count(verdict),4}");
}
Console.WriteLine($"速度・サイズを比べたテスト {report.Measured.Count()} 件");
Console.WriteLine($"直列化の速さ(幾何平均、MemoryPack ÷ ApexPack)   {Format(report.GeoMeanSerializeSpeedup)}");
Console.WriteLine($"逆直列化の速さ(幾何平均、MemoryPack ÷ ApexPack) {Format(report.GeoMeanDeserializeSpeedup)}");
Console.WriteLine($"合計時間の比(MemoryPack ÷ ApexPack)             {Format(report.TotalTimeRatio)}");
Console.WriteLine($"合計サイズの比(MemoryPack ÷ ApexPack)           {Format(report.TotalSizeRatio)}");
foreach (var row in report.Rows.Where(r => r.Verdict is Verdicts.Failed or Verdicts.ReplacedFailed))
{
    Console.WriteLine($"  {row.Verdict}: {row.Id} — {row.Reason}");
}
Console.WriteLine($"-> {output}");
return report.Count(Verdicts.Failed) + report.Count(Verdicts.ReplacedFailed) == 0 ? 0 : 1;

string? Value(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static string Format(double? value) => value is null ? "-" : value.Value.ToString("0.00", CultureInfo.InvariantCulture) + "x";
