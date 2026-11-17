using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ApexPack.BenchmarkGui;

public partial class App : Application
{
    /// <summary>
    /// 確認用: <c>--screenshot 出力.png [--tab 番号] [--search テスト名] [--unmeasured]</c> で画面外の描画結果を保存する。
    /// (操作中の画面の前面に出さない)。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        int index = Array.IndexOf(args, "--screenshot");
        var window = new MainWindow();
        MainWindow = window;
        if (index < 0 || index + 1 >= args.Length)
        {
            window.Show();
            return;
        }

        string output = Path.GetFullPath(args[index + 1]);
        int tabIndex = Array.IndexOf(args, "--tab");
        int tab = tabIndex >= 0 && tabIndex + 1 < args.Length && int.TryParse(args[tabIndex + 1], out var t) ? t : 0;

        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.Width = 1500;
        window.Height = 900;
        window.Show();
        if (window.Content is TabControl tabs && tab < tabs.Items.Count)
        {
            tabs.SelectedIndex = tab;
            int searchIndex = Array.IndexOf(args, "--search");
            if (((TabItem)tabs.Items[tab]).Content is UpstreamComparisonView view)
            {
                view.SelectForScreenshot(searchIndex >= 0 && searchIndex + 1 < args.Length ? args[searchIndex + 1] : "", args.Contains("--unmeasured"));
            }
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var element = (FrameworkElement)window.Content;
            element.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(element);
            var bitmap = new RenderTargetBitmap(
                (int)(element.ActualWidth * dpi.DpiScaleX), (int)(element.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var context = background.RenderOpen())
            {
                context.DrawRectangle(window.Background, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            bitmap.Render(background);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using (var stream = File.Create(output)) encoder.Save(stream);
            Shutdown();
        };
        timer.Start();
    }
}
