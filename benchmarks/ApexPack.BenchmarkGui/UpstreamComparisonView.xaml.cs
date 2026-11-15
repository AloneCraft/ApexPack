using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using Upstream.Runner;

namespace ApexPack.BenchmarkGui;

/// <summary>
/// MemoryPack 公式テストの比較画面。実行は tests/Upstream.Runner(両側のテストを別プロセスで動かす)に任せる。
/// </summary>
public partial class UpstreamComparisonView : UserControl
{
    private const string AllVerdicts = "すべて";

    private ComparisonReport? _report;
    private CancellationTokenSource? _cancellation;
    private readonly string? _root;

    public UpstreamComparisonView()
    {
        InitializeComponent();
        CmbVerdict.ItemsSource = new[] { AllVerdicts }.Concat(Verdicts.All).ToList();
        CmbVerdict.SelectedIndex = 0;

        _root = UpstreamComparison.FindRepositoryRoot(AppContext.BaseDirectory) ?? UpstreamComparison.FindRepositoryRoot(Environment.CurrentDirectory);
        if (_root is null)
        {
            TxtStatus.Text = "リポジトリ(ApexPack.slnx)が見つかりません。実行はできません。保存済みの結果は開けます。";
            BtnRun.IsEnabled = BtnBuildRun.IsEnabled = false;
            return;
        }

        var saved = ReportPath(_root);
        if (File.Exists(saved))
        {
            try
            {
                Show(ComparisonReport.Load(saved));
                TxtStatus.Text = $"前回の結果を表示しています: {saved}";
            }
            catch (Exception e)
            {
                TxtStatus.Text = $"前回の結果を読めませんでした: {e.Message}";
            }
        }
        else
        {
            TxtStatus.Text = "「ビルドして実行」で両側のテストを実行します(数分かかります)。";
        }
    }

    private static string ReportPath(string root) => Path.Combine(root, "upstream_report.json");

    private async void BtnBuildRun_Click(object sender, RoutedEventArgs e) => await RunAsync(build: true);

    private async void BtnRun_Click(object sender, RoutedEventArgs e) => await RunAsync(build: false);

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private async Task RunAsync(bool build)
    {
        if (_root is null) return;
        SetRunning(true);
        _cancellation = new CancellationTokenSource();
        bool timing = ChkTiming.IsChecked == true;
        var progress = new Progress<UpstreamComparison.Progress>(p =>
        {
            if (p.Total > 0)
            {
                BarProgress.IsIndeterminate = false;
                BarProgress.Maximum = p.Total;
                BarProgress.Value = p.Done;
                TxtProgress.Text = $"{p.Side} {p.Done}/{p.Total}";
                TxtStatus.Text = $"{p.Side} 側を実行中: {p.Message}";
            }
            else
            {
                BarProgress.IsIndeterminate = true;
                TxtProgress.Text = p.Side;
                TxtStatus.Text = $"{p.Side}: {p.Message}";
            }
        });

        try
        {
            var token = _cancellation.Token;
            var report = await Task.Run(async () =>
            {
                if (build) await UpstreamComparison.BuildAsync(_root, progress, token);
                return await UpstreamComparison.RunAsync(_root, timing, null, progress, token);
            }, token);
            report.Save(ReportPath(_root));
            Show(report);
            TxtStatus.Text = $"完了: {report.Rows.Count} 件。結果を {ReportPath(_root)} に保存しました。";
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = "中止しました。";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "エラーで止まりました。";
            MessageBox.Show(ex.Message, "比較の実行エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetRunning(false);
        }
    }

    private void SetRunning(bool running)
    {
        BtnRun.IsEnabled = BtnBuildRun.IsEnabled = BtnOpen.IsEnabled = !running && _root is not null;
        BtnCancel.IsEnabled = running;
        PanelProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        BarProgress.IsIndeterminate = true;
        TxtProgress.Text = "";
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "比較結果 (*.json)|*.json", InitialDirectory = _root ?? "" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            Show(ComparisonReport.Load(dialog.FileName));
            TxtStatus.Text = $"開きました: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "読み込みエラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null)
        {
            MessageBox.Show("保存する結果がありません。先に実行してください。", "通知", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dialog = new SaveFileDialog { Filter = "比較結果 (*.json)|*.json", FileName = "upstream_report.json", InitialDirectory = _root ?? "" };
        if (dialog.ShowDialog() != true) return;
        _report.Save(dialog.FileName);
        TxtStatus.Text = $"保存しました: {dialog.FileName}";
    }

    private void Show(ComparisonReport report)
    {
        _report = report;

        PanelCounts.Children.Clear();
        foreach (var verdict in Verdicts.All)
        {
            int count = report.Count(verdict);
            if (count == 0 && verdict is not (Verdicts.Failed or Verdicts.Match)) continue;
            var text = new TextBlock
            {
                Text = $"{verdict} {count}",
                Margin = new Thickness(0, 0, 14, 4),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = VerdictBrushConverter.BrushFor(verdict),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "クリックでこの判定に絞り込む",
            };
            text.MouseLeftButtonUp += (_, _) => CmbVerdict.SelectedItem = verdict;
            PanelCounts.Children.Add(text);
        }
        PanelCounts.Children.Add(new TextBlock
        {
            Text = $"取り込み対象外 {Classification.ExcludedGeneratorTestMethods}",
            Margin = new Thickness(0, 0, 14, 4),
            FontSize = 14,
            Foreground = VerdictBrushConverter.BrushFor(Verdicts.Skipped),
            ToolTip = "MemoryPack のジェネレーター本体と診断に依存するテストメソッド(SourceGeneratorTests)。ソースを取り込んでいない",
        });

        TxtSer.Text = Ratio(report.GeoMeanSerializeSpeedup);
        TxtDes.Text = Ratio(report.GeoMeanDeserializeSpeedup);
        TxtTime.Text = Ratio(report.TotalTimeRatio);
        TxtSize.Text = Ratio(report.TotalSizeRatio);
        TxtMeasured.Text = $"MemoryPack ÷ ApexPack(1 より大きいと ApexPack が小さい)。比べたテスト {report.Measured.Count()} 件"
                           + (report.Timing ? "" : "。時間は測っていない");
        ApplyFilter();
    }

    private static string Ratio(double? value) => value is null ? "---" : value.Value.ToString("0.00", CultureInfo.InvariantCulture) + "x";

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (_report is null || TxtRowCount is null) return;
        string verdict = CmbVerdict.SelectedItem as string ?? AllVerdicts;
        string search = TxtSearch.Text.Trim();
        bool measuredOnly = ChkMeasuredOnly.IsChecked == true;
        var rows = _report.Rows
            .Where(r => verdict == AllVerdicts || r.Verdict == verdict)
            .Where(r => search.Length == 0 || r.Id.Contains(search, StringComparison.OrdinalIgnoreCase) || r.Reason.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(r => !measuredOnly || r.SerializeSpeedup is not null || r.DeserializeSpeedup is not null || r.SizeRatio is not null)
            .ToList();
        GridRows.ItemsSource = rows;
        TxtRowCount.Text = $"{rows.Count} / {_report.Rows.Count} 件";
    }

    private void GridRows_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GridRows.SelectedItem is not ComparisonRow row) return;
        var sb = new StringBuilder();
        sb.AppendLine($"{row.Id}");
        sb.AppendLine($"判定: {row.Verdict}{(row.Reason.Length > 0 ? " — " + row.Reason : "")}");
        sb.AppendLine($"MemoryPack: {StatusTextConverter.Text(row.MemoryPackStatus)}{(row.MemoryPackError is null ? "" : " — " + row.MemoryPackError)}");
        sb.AppendLine($"ApexPack:   {StatusTextConverter.Text(row.ApexPackStatus)}{(row.ApexPackError is null ? "" : " — " + row.ApexPackError)}");
        sb.AppendLine();
        int count = Math.Max(row.MemoryPackCalls.Count, row.ApexPackCalls.Count);
        if (count == 0)
        {
            sb.AppendLine("記録した呼び出しはありません。");
        }
        else
        {
            sb.AppendLine("記録した呼び出し(テスト後に再生して測定): 種類 / 型 / バイト数 MP→Apex / 直列化 ns MP→Apex / 逆直列化 ns MP→Apex / 再現性・往復(Apex)");
            for (int i = 0; i < count; i++)
            {
                var m = i < row.MemoryPackCalls.Count ? row.MemoryPackCalls[i] : null;
                var a = i < row.ApexPackCalls.Count ? row.ApexPackCalls[i] : null;
                var c = m ?? a!;
                sb.Append($"  {i + 1,2}. {c.Op,-11} {c.Type,-40} ");
                sb.Append($"{m?.Bytes.ToString("N0") ?? "-",8} → {a?.Bytes.ToString("N0") ?? "-",-8} ");
                sb.Append($"{Ns(m?.SerializeNs),10} → {Ns(a?.SerializeNs),-10} ");
                sb.Append($"{Ns(m?.DeserializeNs),10} → {Ns(a?.DeserializeNs),-10} ");
                sb.Append(a is null ? "" : $"{Mark(a.Deterministic)}{Mark(a.RoundTrip)}");
                var error = a?.ReplayError ?? m?.ReplayError;
                if (error is not null) sb.Append($"  再生エラー: {error}");
                sb.AppendLine();
            }
        }
        TxtDetail.Text = sb.ToString();
    }

    /// <summary>確認用(画面の画像を保存するとき): テスト名で絞り込み、最初の行を選ぶ。</summary>
    public void SelectForScreenshot(string search)
    {
        TxtSearch.Text = search;
        ApplyFilter();
        if (GridRows.Items.Count > 0) GridRows.SelectedIndex = 0;
    }

    private static string Ns(double? value) => value is > 0 ? value.Value.ToString("N1", CultureInfo.InvariantCulture) : "-";

    private static string Mark(bool? value) => value switch { true => "○", false => "×", null => "-" };
}

/// <summary>テストの状態(pass / fail / skip)を日本語にする。</summary>
public sealed class StatusTextConverter : IValueConverter
{
    public static string Text(string? status) => status switch
    {
        "pass" => "合格",
        "fail" => "失敗",
        "skip" => "スキップ",
        _ => status ?? "",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Text(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>判定ごとの色。</summary>
public sealed class VerdictBrushConverter : IValueConverter
{
    public static Brush BrushFor(string? verdict) => verdict switch
    {
        Verdicts.Match => Freeze(166, 227, 161),
        Verdicts.Replaced => Freeze(148, 226, 213),
        Verdicts.OutOfScope => Freeze(147, 153, 178),
        Verdicts.DesignDifference => Freeze(249, 226, 175),
        Verdicts.Skipped => Freeze(108, 112, 134),
        Verdicts.BothFailed => Freeze(250, 179, 135),
        _ => Freeze(243, 139, 168),
    };

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => BrushFor(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>倍率の色(1 より大きい = ApexPack が有利で緑、小さい = 赤)。</summary>
public sealed class RatioBrushConverter : IValueConverter
{
    private static readonly Brush Better = Make(166, 227, 161);
    private static readonly Brush Worse = Make(243, 139, 168);
    private static readonly Brush Even = Make(205, 214, 244);

    private static SolidColorBrush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        double v when v >= 1.05 => Better,
        double v when v <= 0.95 => Worse,
        _ => Even,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
