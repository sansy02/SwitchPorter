using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwitchAlbum.Resources;
using SwitchAlbum.Services;

namespace SwitchAlbum.ViewModels;

public sealed class PhoneOption
{
    public PhoneOption(string? id, string name)
    {
        Id = id;
        Name = name;
    }

    public string? Id { get; }
    public string Name { get; }
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly IMediaProvider _provider;
    private readonly DuplicateTracker? _tracker;

    public SettingsViewModel(SettingsService settings, ThemeService theme, IMediaProvider provider, DuplicateTracker? tracker = null)
    {
        _settings = settings;
        _theme = theme;
        _provider = provider;
        _tracker = tracker;
        _savedCountText = tracker != null ? string.Format(Strings.Settings_SavedCount, tracker.Count) : "";
        _savePath = settings.Current.SavePath;
        _autoRename = settings.Current.AutoRename;
        _themeOption = MapToOption(theme.Theme);
        _ = LoadPhonesAsync();
        _ = LoadCacheSizeAsync();
    }

    [ObservableProperty] private string _savePath;
    [ObservableProperty] private bool _autoRename;
    [ObservableProperty] private string _themeOption;
    [ObservableProperty] private PhoneOption? _selectedPhone;
    [ObservableProperty] private string _cacheStatus = "";
    [ObservableProperty] private string _cacheSizeText = "";
    [ObservableProperty] private string _savedCountText;

    public ObservableCollection<PhoneOption> Phones { get; } = new();

    public string[] ThemeOptions { get; } =
    {
        Strings.Settings_Theme_System,
        Strings.Settings_Theme_Light,
        Strings.Settings_Theme_Dark,
    };

    private static string MapToOption(AppTheme theme) => theme switch
    {
        AppTheme.Light => Strings.Settings_Theme_Light,
        AppTheme.Dark => Strings.Settings_Theme_Dark,
        _ => Strings.Settings_Theme_System,
    };

    private static AppTheme MapToTheme(string option) => option switch
    {
        Strings.Settings_Theme_Light => AppTheme.Light,
        Strings.Settings_Theme_Dark => AppTheme.Dark,
        _ => AppTheme.System,
    };

    /// <summary>主题选择即时生效：不必等关闭对话框才变色。</summary>
    partial void OnThemeOptionChanged(string value) => _theme.SetTheme(MapToTheme(value));

    public void SetSavePath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            SavePath = path;
        }
    }

    public void Save()
    {
        _settings.Current.SavePath = SavePath;
        _settings.Current.AutoRename = AutoRename;

        _settings.Current.PhoneDeviceId = SelectedPhone?.Id;
        _settings.Current.PhoneFriendlyName = SelectedPhone?.Name;
        _settings.Save();

        _theme.SetTheme(MapToTheme(ThemeOption));
    }

    [RelayCommand]
    private async Task RefreshPhonesAsync() => await LoadPhonesAsync();

    [RelayCommand]
    private void OpenLogs()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Log.DirectoryPath)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // 打开失败忽略
        }
    }

    [RelayCommand]
    private void OpenSaveFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_settings.Current.SavePath)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // 打开失败忽略
        }
    }

    /// <summary>
    /// 清除「已保存」记录：所有照片恢复为未保存，下次保存不会被跳过。
    /// 不删除任何照片文件。用于记录与实际不符时的补救（例如换了电脑、手机恢复出厂）。
    /// </summary>
    [RelayCommand]
    private void ClearSavedRecords()
    {
        if (_tracker == null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            Strings.Settings_ClearSavedConfirm, Strings.Btn_ClearSaved,
            System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        var count = _tracker.Count;
        _tracker.ClearAll();
        Log.Info($"用户清除了 {count} 条已保存记录");
        SavedCountText = string.Format(Strings.Settings_ClearSavedDone, count);
    }

    [RelayCommand]
    private void ClearCache()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwitchAlbum");
        long freed = 0;
        foreach (var sub in new[] { "covers", "thumbs" })
        {
            try
            {
                var dir = Path.Combine(baseDir, sub);
                if (Directory.Exists(dir))
                {
                    freed += DirSize(dir);
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // 文件被占用时忽略
            }
        }

        CacheStatus = string.Format(Strings.Status_CacheClearedWithSize, FormatSize(freed));
        _ = LoadCacheSizeAsync();
    }

    private async Task LoadCacheSizeAsync()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwitchAlbum");
        var total = await Task.Run(() =>
        {
            long sum = 0;
            foreach (var sub in new[] { "covers", "thumbs" })
            {
                try
                {
                    var dir = Path.Combine(baseDir, sub);
                    if (Directory.Exists(dir))
                    {
                        sum += DirSize(dir);
                    }
                }
                catch
                {
                    // 忽略统计失败
                }
            }

            return sum;
        });

        CacheSizeText = total > 0
            ? string.Format(Strings.Settings_CacheSize, FormatSize(total))
            : Strings.Settings_CacheSizeNone;
    }

    private static long DirSize(string dir)
    {
        long sum = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try
            {
                sum += new FileInfo(file).Length;
            }
            catch
            {
                // 忽略单文件失败
            }
        }

        return sum;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB",
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} B",
    };

    private async Task LoadPhonesAsync()
    {
        Phones.Clear();
        Phones.Add(new PhoneOption(null, Strings.Settings_PhoneDevice_Auto));

        // 仅 MTP 协议设备（安卓手机）；硬盘（MSC）等不会出现在列表里
        var devices = await _provider.GetPhoneCandidateDevicesAsync(CancellationToken.None);
        foreach (var device in devices)
        {
            Phones.Add(new PhoneOption(device.DeviceId, device.FriendlyName));
        }

        SelectedPhone = Phones.FirstOrDefault(p => p.Id == _settings.Current.PhoneDeviceId) ?? Phones[0];
    }
}
