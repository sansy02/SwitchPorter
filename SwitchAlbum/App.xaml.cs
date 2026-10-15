using System.Windows;
using SwitchAlbum.Services;
using SwitchAlbum.ViewModels;
using SwitchAlbum.Views;

namespace SwitchAlbum;

public partial class App : Application
{
    private SettingsService? _settings;
    private ThemeService? _theme;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            // 未处理异常不崩溃：记录日志并给出可见提示（之前只写日志，用户不知道发生了什么）
            Log.Error("未处理异常: " + args.Exception.Message, args.Exception);
            args.Handled = true;
            (MainWindow?.DataContext as MainViewModel)?.ShowToast("出错了：" + args.Exception.Message + "（详情见设置 → 打开日志文件夹）");
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("后台任务异常", args.Exception);
            args.SetObserved();
        };

        try
        {
            if (e.Args.Contains("--smoke-test"))
            {
                SmokeTest.RunAsync();
                return;
            }

            _settings = new SettingsService();
            _theme = new ThemeService(_settings);
            ApplyTheme();
            _theme.ThemeChanged += ApplyTheme;

            var tracker = new DuplicateTracker();
            var saveService = new SaveService(tracker);
            var phoneSaveService = new PhoneSaveService(tracker, _settings);
            var thumbnailService = new ThumbnailService();
            var coverService = new CoverService();

            // 游戏库（约 10MB JSON）放到后台解析：先把窗口显示出来，不再启动白屏
            var titleDbTask = Task.Run<ITitleDb?>(() => TitleDbService.LoadEmbedded());

            var mainViewModel = new MainViewModel(
                _settings, _theme, tracker, saveService, phoneSaveService,
                thumbnailService, coverService, new ScanCacheService(), titleDbTask);

            var window = new MainWindow { DataContext = mainViewModel };
            mainViewModel.AttachWindow(window);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Error("启动失败", ex);
            try
            {
                MessageBox.Show("启动失败：" + ex.Message, "SwitchAlbum", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // 弹窗失败忽略
            }

            Shutdown(-1);
        }
    }

    private void ApplyTheme()
    {
        var merged = Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(new ResourceDictionary
        {
            Source = new Uri(
                _theme!.IsDark ? "Resources/Themes/Colors.Dark.xaml" : "Resources/Themes/Colors.Light.xaml",
                UriKind.Relative),
        });
        merged.Add(new ResourceDictionary
        {
            Source = new Uri("Resources/Themes/Styles.xaml", UriKind.Relative),
        });
    }
}
