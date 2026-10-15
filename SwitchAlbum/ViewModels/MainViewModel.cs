using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SwitchAlbum.Models;
using SwitchAlbum.Resources;
using SwitchAlbum.Services;

namespace SwitchAlbum.ViewModels;

/// <summary>
/// 照片来源（顶栏两个标签）。「去哪里」不再是页面，而是底部操作栏的按钮：
/// 存到电脑 / 存到安卓手机 / 发送到 iPhone。
/// </summary>
public enum SourceMode
{
    /// <summary>USB 连接的 Switch 相册。</summary>
    Switch,

    /// <summary>电脑保存位置里已保存的照片。</summary>
    Library,
}

/// <summary>照片要去的地方。</summary>
public enum Destination
{
    Pc,
    Android,
    Iphone,
}

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly ThemeService _theme;
    private readonly DuplicateTracker _tracker;
    private readonly SaveService _saveService;
    private readonly PhoneSaveService _phoneSaveService;
    private readonly ThumbnailService _thumbnailService;
    private readonly CoverService _coverService;
    private readonly ScanCacheService _scanCache;
    private readonly IphoneShareService _iphoneShare = new();
    private readonly OnlineTitleUpdater _onlineTitles = new();
    private readonly Task<ITitleDb?> _titleDbTask;
    private readonly SynchronizationContext? _ui;
    private readonly IMediaProvider _provider;

    private ITitleDb? _titleDb;
    private IMediaDeviceSession? _session;
    private LocalFolderSession? _localSession;
    private ScanResult? _scan;
    private ScanResult? _localScan;
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _transferCts;
    private IphoneWebServer? _iphoneServer;
    private Window? _owner;
    private string? _deviceKey;
    private int _toastSequence;
    private int _transferGeneration;

    public MainViewModel(
        SettingsService settings,
        ThemeService theme,
        DuplicateTracker tracker,
        SaveService saveService,
        PhoneSaveService phoneSaveService,
        ThumbnailService thumbnailService,
        CoverService coverService,
        ScanCacheService scanCache,
        Task<ITitleDb?> titleDbTask,
        IMediaProvider? provider = null)
    {
        _settings = settings;
        _theme = theme;
        _tracker = tracker;
        _saveService = saveService;
        _phoneSaveService = phoneSaveService;
        _thumbnailService = thumbnailService;
        _coverService = coverService;
        _scanCache = scanCache;
        _titleDbTask = titleDbTask;
        _ui = SynchronizationContext.Current;

        _tracker.LegacySaveRoot = settings.Current.SavePath;
        SavePathText = settings.Current.SavePath;
        _provider = provider ?? new MtpMediaProvider();
        _provider.DeviceChanged += OnDeviceChanged;
        SetHint(Strings.Hint_LoadingTitle, Strings.Status_LoadingTitleDb);
    }

    // =====================================================================
    // 可绑定状态
    // =====================================================================
    public AppSettings Settings => _settings.Current;

    [ObservableProperty] private string _deviceStatusText = Strings.Status_NotConnected;
    [ObservableProperty] private bool _isDeviceConnected;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isTransferring;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private SourceMode _source = SourceMode.Switch;

    [ObservableProperty] private GameWallViewModel? _wall;
    [ObservableProperty] private GameGridViewModel? _grid;
    [ObservableProperty] private GameWallViewModel? _localWall;
    [ObservableProperty] private GameGridViewModel? _localGrid;

    [ObservableProperty] private LightboxViewModel? _lightbox;
    [ObservableProperty] private string _hintTitle = "";
    [ObservableProperty] private string _hintText = "";
    [ObservableProperty] private bool _hintVisible;
    [ObservableProperty] private string _toastText = "";
    [ObservableProperty] private bool _toastVisible;
    [ObservableProperty] private string _savePathText;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string? _lastOutputFolder;

    public bool IsSwitchSource => Source == SourceMode.Switch;
    public bool IsLibrarySource => Source == SourceMode.Library;

    /// <summary>内容区当前视图：当前来源的 游戏墙 或 照片网格。</summary>
    public object? CurrentContent => IsSwitchSource ? (object?)Grid ?? Wall : (object?)LocalGrid ?? LocalWall;

    public bool IsInGrid => ActiveGrid != null;

    public bool CanAct => SelectedCount > 0 && !IsTransferring;

    /// <summary>「存到电脑」只对 Switch 来源有意义（电脑里的照片已经在电脑上了）。</summary>
    public bool CanSaveToPc => CanAct && IsSwitchSource && IsDeviceConnected;

    public bool CanSendToPhone => CanAct && (IsLibrarySource || IsDeviceConnected);

    public string ActionSummary => SelectedCount > 0
        ? string.Format(Strings.Action_Selected, SelectedCount)
        : Strings.Action_PickHint;

    public string SaveToPcTooltip => string.Format(Strings.Action_SaveToPcTip, SavePathText);

    public bool HasLastOutput => !string.IsNullOrEmpty(LastOutputFolder) && !IsTransferring;

    private ScanResult? ActiveScan => IsLibrarySource ? _localScan : _scan;
    private IMediaDeviceSession? ActiveSession => IsLibrarySource ? _localSession : _session;
    private string? ActiveDeviceKey => IsLibrarySource ? "local" : _deviceKey;
    private GameGridViewModel? ActiveGrid => IsLibrarySource ? LocalGrid : Grid;

    partial void OnSourceChanged(SourceMode value)
    {
        OnPropertyChanged(nameof(IsSwitchSource));
        OnPropertyChanged(nameof(IsLibrarySource));
        NotifyViewChanged();
    }

    partial void OnWallChanged(GameWallViewModel? value) => NotifyViewChanged();
    partial void OnGridChanged(GameGridViewModel? value) => NotifyViewChanged();
    partial void OnLocalWallChanged(GameWallViewModel? value) => NotifyViewChanged();
    partial void OnLocalGridChanged(GameGridViewModel? value) => NotifyViewChanged();
    partial void OnSelectedCountChanged(int value) => NotifyActionsChanged();
    partial void OnIsDeviceConnectedChanged(bool value) => NotifyActionsChanged();

    partial void OnIsTransferringChanged(bool value)
    {
        NotifyActionsChanged();
        OnPropertyChanged(nameof(HasLastOutput));
    }

    partial void OnLastOutputFolderChanged(string? value) => OnPropertyChanged(nameof(HasLastOutput));
    partial void OnSavePathTextChanged(string value) => OnPropertyChanged(nameof(SaveToPcTooltip));

    private void NotifyViewChanged()
    {
        OnPropertyChanged(nameof(CurrentContent));
        OnPropertyChanged(nameof(IsInGrid));
        SelectedCount = ActiveGrid?.SelectedCount ?? 0;
        NotifyActionsChanged();
    }

    private void NotifyActionsChanged()
    {
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(CanSaveToPc));
        OnPropertyChanged(nameof(CanSendToPhone));
        OnPropertyChanged(nameof(ActionSummary));
    }

    /// <summary>网格里勾选变化时由 GameGridViewModel 回调。</summary>
    public void OnGridSelectionChanged(GameGridViewModel grid)
    {
        if (ReferenceEquals(grid, ActiveGrid))
        {
            SelectedCount = grid.SelectedCount;
        }
    }

    private void SetHint(string title, string text)
    {
        HintTitle = title;
        HintText = text;
        HintVisible = true;
    }

    /// <summary>根据当前来源与数据刷新空状态提示。</summary>
    private void UpdateHint()
    {
        if (IsLibrarySource)
        {
            if (_localScan == null || _localScan.AllItems.Count == 0)
            {
                SetHint(Strings.Hint_LibraryEmptyTitle, Strings.Local_EmptyHint);
            }
            else
            {
                HintVisible = false;
            }

            return;
        }

        if (!IsDeviceConnected)
        {
            SetHint(Strings.Hint_NotConnectedTitle, Strings.Hint_ConnectSwitch);
        }
        else if (IsScanning && Wall == null)
        {
            SetHint(Strings.Status_Scanning, "");
        }
        else if (Wall == null || (_scan != null && _scan.AllItems.Count == 0))
        {
            SetHint(Strings.Hint_EmptyAlbumTitle, Strings.Hint_EmptyAlbum);
        }
        else
        {
            HintVisible = false;
        }
    }

    // =====================================================================
    // 生命周期
    // =====================================================================
    public void AttachWindow(Window window) => _owner = window;

    /// <summary>窗口显示后调用：先等游戏库在后台加载完（窗口已可见，不再白屏），再扫描设备。</summary>
    public async Task StartAsync()
    {
        DeviceStatusText = Strings.Status_LoadingTitleDb;
        try
        {
            _titleDb = await _titleDbTask;
        }
        catch (Exception ex)
        {
            Log.Error("游戏库加载失败（将显示文件夹原名）", ex);
        }

        _iphoneShare.Cleanup(); // 清理上次异常退出遗留的临时分享

        // Switch 2 游戏：先用本地数据（内置快照 + 上次联网缓存，离线可用），扫描后再在后台联网更新
        var ns2Local = await Task.Run(() =>
        {
            var local = _onlineTitles.LoadLocal();
            (_titleDb as TitleDbService)?.Merge(OnlineTitleUpdater.ToTitles(local));
            return local;
        });

        await RescanAsync();
        _ = RefreshOnlineTitlesAsync(ns2Local);
    }

    /// <summary>后台联网更新 Switch 2 游戏列表；有新数据时给还没封面的游戏重新取封面。</summary>
    private async Task RefreshOnlineTitlesAsync(IReadOnlyList<Ns2Entry> known)
    {
        if (_titleDb is not TitleDbService db || !_onlineTitles.NeedsRefresh())
        {
            return;
        }

        try
        {
            var updated = await Task.Run(() => _onlineTitles.RefreshAsync(known, CancellationToken.None));
            if (updated == null)
            {
                return;
            }

            await Task.Run(() => db.Merge(OnlineTitleUpdater.ToTitles(updated)));
            RetryMissingCovers();
        }
        catch (Exception ex)
        {
            Log.Error("Switch 2 游戏列表更新失败", ex);
        }
    }

    private void RetryMissingCovers()
    {
        if (_titleDb == null)
        {
            return;
        }

        foreach (var wall in new[] { Wall, LocalWall })
        {
            if (wall == null)
            {
                continue;
            }

            foreach (var card in wall.Cards.Where(c => !c.IsAllCard && c.CoverPath == null))
            {
                var tid = _titleDb.GetTitleIdByFolderName(card.Title);
                if (tid != null)
                {
                    card.RetryCover(tid);
                }
            }
        }
    }

    public void SaveWindowBounds(double left, double top, double width, double height, bool maximized)
    {
        _settings.Current.WindowLeft = left;
        _settings.Current.WindowTop = top;
        _settings.Current.WindowWidth = width;
        _settings.Current.WindowHeight = height;
        _settings.Current.WindowMaximized = maximized;
        _settings.Save();
    }

    public async Task ShutdownAsync()
    {
        _transferCts?.Cancel();
        _scanCts?.Cancel();
        _tracker.Flush();
        _iphoneServer?.Dispose();
        _iphoneShare.Cleanup();
        await DisposeSessionAsync();
        await DisposeLocalSessionAsync();
        (_provider as IDisposable)?.Dispose();
    }

    // =====================================================================
    // 设备
    // =====================================================================
    private async Task DisposeSessionAsync()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session != null)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch (Exception ex)
            {
                Log.Info("关闭 Switch 会话失败（设备可能已拔出）: " + ex.Message);
            }
        }
    }

    private async Task DisposeLocalSessionAsync()
    {
        var session = Interlocked.Exchange(ref _localSession, null);
        if (session != null)
        {
            await session.DisposeAsync();
        }
    }

    private void OnDeviceChanged(object? sender, DeviceChangedEventArgs e)
    {
        _ui?.Post(_ => _ = OnDeviceChangedUiAsync(e), null);
    }

    private async Task OnDeviceChangedUiAsync(DeviceChangedEventArgs e)
    {
        if (e.Connected)
        {
            if (IsScanning || IsDeviceConnected || IsTransferring || !_titleDbTask.IsCompleted)
            {
                return; // 新插入的可能是手机，已连接 Switch 时不打断
            }

            await Task.Delay(600); // 防抖：MTP 常连续上报多个事件
            if (!IsDeviceConnected)
            {
                await RescanAsync();
            }
        }
        else if (IsDeviceConnected && string.Equals(e.DeviceId, _deviceKey, StringComparison.OrdinalIgnoreCase))
        {
            // 只有拔掉的是 Switch 本身才算断开（拔手机不影响）
            await ShowDisconnectedAsync();
        }
    }

    private async Task ShowDisconnectedAsync()
    {
        IsDeviceConnected = false;
        DeviceStatusText = Strings.Status_NotConnected;
        await DisposeSessionAsync();
        _scan = null;
        _deviceKey = null;
        Wall = null;
        Grid = null;
        if (IsSwitchSource)
        {
            CloseLightbox();
        }

        UpdateHint();
    }

    // =====================================================================
    // 扫描 / 刷新
    // =====================================================================
    /// <summary>顶栏「刷新」：Switch 来源重新扫描设备；电脑来源重新读取保存位置。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsTransferring)
        {
            ShowToast(Strings.Status_Busy);
            return;
        }

        if (IsLibrarySource)
        {
            await LoadLibraryAsync();
        }
        else
        {
            await RescanAsync();
        }
    }

    private async Task RescanAsync()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        IsScanning = true;
        DeviceStatusText = Strings.Status_Connecting;
        if (Wall == null && IsSwitchSource)
        {
            SetHint(Strings.Status_Connecting, "");
        }

        try
        {
            var switchDevice = await FindSwitchAsync(ct);
            if (switchDevice == null)
            {
                Log.Info("未找到 Switch 设备（含兜底探测），进入未连接状态");
                await ShowDisconnectedAsync();
                return;
            }

            await DisposeSessionAsync();
            _session = await _provider.ConnectAsync(switchDevice, ct);
            _deviceKey = switchDevice.DeviceId;
            IsDeviceConnected = true;
            UpdateHint();

            // 先显示本地扫描缓存（秒开），后台真实扫描刷新
            var cached = _scanCache.Load(switchDevice.DeviceId);
            if (cached != null)
            {
                ApplyScanResult(cached, keepGrid: false);
                Log.Info("已加载扫描缓存（" + cached.Games.Count + " 个游戏），后台刷新中");
            }

            DeviceStatusText = Strings.Status_Scanning;
            var scan = await AlbumScanner.ScanAsync(_session, _titleDb, ct);
            DeviceStatusText = switchDevice.FriendlyName;

            if (scan != null)
            {
                // 缓存已先显示时，用户可能已经点进了某个游戏：真实扫描结果不要把他踢回游戏墙
                ApplyScanResult(scan, keepGrid: cached != null);
                _scanCache.Save(switchDevice.DeviceId, scan);
            }
            else
            {
                Log.Info("相册扫描返回空（根目录无法枚举）");
            }
        }
        catch (OperationCanceledException)
        {
            // 重新扫描打断，静默
        }
        catch (Exception ex)
        {
            Log.Error("扫描失败", ex);
            await ShowDisconnectedAsync();
            ShowToast("连接 Switch 失败：" + ex.Message);
        }
        finally
        {
            IsScanning = false;
            UpdateHint();
        }
    }

    private async Task<MediaDeviceInfo?> FindSwitchAsync(CancellationToken ct)
    {
        var devices = await _provider.GetDevicesAsync(ct);
        var switchDevice = devices.FirstOrDefault(d => d.IsSwitch);
        if (switchDevice != null)
        {
            return switchDevice;
        }

        // Switch 2 等非标准名称设备兜底：根目录含 Album 文件夹即视为 Switch
        foreach (var device in devices)
        {
            try
            {
                var probe = await _provider.ConnectAsync(device, ct);
                await using (probe)
                {
                    var root = await probe.EnumerateAsync("\\", ct);
                    if (root.Any(e => e.IsDirectory && e.Name.Equals("Album", StringComparison.OrdinalIgnoreCase)))
                    {
                        Log.Info($"按 Album 目录兜底识别 Switch: {device.FriendlyName} ({device.DeviceId})");
                        return device;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Info($"兜底探测 {device.FriendlyName} 失败: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>把扫描结果应用到界面（缓存与真实扫描共用）。</summary>
    private void ApplyScanResult(ScanResult scan, bool keepGrid)
    {
        var previousGridTitle = keepGrid ? Grid?.Title : null;
        _scan = scan;
        foreach (var item in scan.AllItems)
        {
            item.Saved = _tracker.GetState(item);
        }

        Wall = new GameWallViewModel(scan, this);
        Grid = null;
        if (IsSwitchSource)
        {
            CloseLightbox();
        }

        // 尽量回到用户刚才打开的游戏
        if (previousGridTitle != null)
        {
            var card = Wall.Cards.FirstOrDefault(c => c.Title == previousGridTitle);
            if (card != null)
            {
                OpenGame(card);
            }
        }

        UpdateHint();
    }

    // =====================================================================
    // 来源切换（顶栏标签）
    // =====================================================================
    [RelayCommand]
    private void ShowSwitchSource()
    {
        if (IsSwitchSource)
        {
            return;
        }

        CloseLightbox();
        Source = SourceMode.Switch;
        UpdateHint();
    }

    [RelayCommand]
    private async Task ShowLibrarySourceAsync()
    {
        if (IsLibrarySource)
        {
            return;
        }

        CloseLightbox();
        Source = SourceMode.Library;
        await LoadLibraryAsync();
    }

    /// <summary>扫描电脑保存位置（「电脑里的照片」）。</summary>
    private async Task LoadLibraryAsync()
    {
        var savePath = _settings.Current.SavePath;
        if (string.IsNullOrWhiteSpace(savePath))
        {
            ShowToast(Strings.Msg_PathInvalid);
            return;
        }

        var previousGridTitle = LocalGrid?.Title;
        await DisposeLocalSessionAsync();
        try
        {
            _localSession = new LocalFolderSession(savePath);
        }
        catch (Exception ex)
        {
            Log.Error("保存位置不可用: " + savePath, ex);
            ShowToast(Strings.Msg_PathInvalid);
            return;
        }

        IsScanning = true;
        HintVisible = false;
        try
        {
            var session = _localSession;
            _localScan = await Task.Run(() => AlbumScanner.ScanAsync(session, _titleDb, CancellationToken.None));
        }
        finally
        {
            IsScanning = false;
        }

        LocalGrid = null;
        if (_localScan == null || _localScan.AllItems.Count == 0)
        {
            LocalWall = null;
            UpdateHint();
            return;
        }

        // 把电脑里已重命名的文件指回它对应的 Switch 原图记录：两边的「已存手机」状态互通
        var index = _tracker.SnapshotPcPathIndex();
        foreach (var item in _localScan.AllItems)
        {
            var full = Path.GetFullPath(_localSession.ToLocalPath(item.DevicePath));
            if (index.TryGetValue(full, out var key))
            {
                item.TrackingKey = key;
            }

            item.Saved = _tracker.GetState(item);
        }

        LocalWall = new GameWallViewModel(_localScan, this);
        if (previousGridTitle != null)
        {
            var card = LocalWall.Cards.FirstOrDefault(c => c.Title == previousGridTitle);
            if (card != null)
            {
                OpenGame(card);
            }
        }

        UpdateHint();
    }

    // =====================================================================
    // 游戏墙 / 网格 / 灯箱
    // =====================================================================
    public void OpenGame(GameCardViewModel card)
    {
        var scan = ActiveScan;
        if (scan == null)
        {
            return;
        }

        IReadOnlyList<AlbumItem> items = card.IsAllCard || card.DevicePath == null
            ? scan.AllItems
            : scan.ItemsByGame.TryGetValue(card.DevicePath, out var list) ? list : Array.Empty<AlbumItem>();

        var grid = new GameGridViewModel(card.Title, items, this);
        if (IsLibrarySource)
        {
            LocalGrid = grid;
        }
        else
        {
            Grid = grid;
        }
    }

    [RelayCommand]
    private void BackToWall()
    {
        CloseLightbox();
        if (IsLibrarySource)
        {
            LocalGrid = null;
        }
        else
        {
            Grid = null;
        }
    }

    /// <summary>双击照片：在当前来源的网格里打开大图（之前「电脑里的照片」双击没反应）。</summary>
    public void OpenPhoto(PhotoItemViewModel item) => ActiveGrid?.OpenLightbox(item);

    public void OpenLightbox(IReadOnlyList<PhotoItemViewModel> items, PhotoItemViewModel current)
    {
        var lightbox = new LightboxViewModel(items, current, LoadFullImageAsync, item => _ = OpenVideoAsync(item));
        lightbox.Closed += CloseLightbox;
        Lightbox = lightbox;
    }

    private void CloseLightbox()
    {
        Lightbox?.Cancel();
        Lightbox = null;
    }

    [RelayCommand]
    private void SelectAll() => ActiveGrid?.SetAllSelected(true);

    [RelayCommand]
    private void DeselectAll() => ActiveGrid?.SetAllSelected(false);

    /// <summary>只勾选还没存到电脑（Switch 来源）/ 还没存到手机（电脑来源）的照片。</summary>
    [RelayCommand]
    private void SelectUnsaved()
    {
        var grid = ActiveGrid;
        if (grid == null)
        {
            return;
        }

        grid.SelectWhere(item => IsSwitchSource
            ? item.Saved is SavedState.NotSaved or SavedState.SavedToPhone
            : item.Saved is SavedState.NotSaved or SavedState.SavedToPc);
    }

    // =====================================================================
    // 图片 / 视频 / 封面
    // =====================================================================
    public Task<string?> LoadThumbnailAsync(PhotoItemViewModel item)
    {
        var session = ActiveSession;
        var deviceKey = ActiveDeviceKey;
        if (session == null || deviceKey == null)
        {
            return Task.FromResult<string?>(null);
        }

        return _thumbnailService.GetThumbnailAsync(
            deviceKey, session, item.Item.DevicePath, item.Item.FileName,
            item.Item.LastModified, item.Item.Size, CancellationToken.None);
    }

    private Task<string?> LoadFullImageAsync(PhotoItemViewModel item)
    {
        var session = ActiveSession;
        var deviceKey = ActiveDeviceKey;
        if (session == null || deviceKey == null)
        {
            return Task.FromResult<string?>(null);
        }

        // 电脑里的照片直接用原文件，不必复制到缓存
        if (session is LocalFolderSession local)
        {
            var path = local.ToLocalPath(item.Item.DevicePath);
            return Task.FromResult<string?>(File.Exists(path) ? path : null);
        }

        return _thumbnailService.GetFullImageAsync(
            deviceKey, session, item.Item.DevicePath, item.Item.FileName,
            item.Item.LastModified, item.Item.Size, CancellationToken.None);
    }

    public async Task OpenVideoAsync(PhotoItemViewModel item)
    {
        if (ActiveSession == null)
        {
            return;
        }

        var path = await LoadFullImageAsync(item);
        if (path == null)
        {
            ShowToast(string.Format(Strings.Msg_SaveFailed, item.FileName));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Info("打开视频失败: " + ex.Message);
            ShowToast(Strings.Lightbox_NoPreview);
        }
    }

    public Task<string?> ResolveCoverAsync(string? titleId, string title)
    {
        var urls = titleId != null ? _titleDb?.GetCoverUrls(titleId) : null;
        return _coverService.ResolveAsync(titleId, title, urls, CancellationToken.None);
    }

    // =====================================================================
    // 去处：存到电脑 / 存到安卓手机 / 发送到 iPhone（底部操作栏）
    // =====================================================================
    private IReadOnlyList<AlbumItem>? TakeSelection()
    {
        if (IsTransferring)
        {
            ShowToast(Strings.Status_Busy);
            return null;
        }

        var selected = ActiveGrid?.SelectedItems();
        if (selected == null || selected.Count == 0)
        {
            ShowToast(Strings.Status_NoSelection);
            return null;
        }

        return selected;
    }

    [RelayCommand]
    private async Task SaveToPcAsync()
    {
        var items = TakeSelection();
        if (items == null)
        {
            return;
        }

        var session = _session;
        if (!IsSwitchSource || session == null)
        {
            ShowToast(Strings.Status_NeedSwitch);
            return;
        }

        var targetDir = _settings.Current.SavePath;
        try
        {
            Directory.CreateDirectory(targetDir);
        }
        catch
        {
            ShowToast(Strings.Msg_PathInvalid);
            return;
        }

        var result = await RunTransferAsync(Destination.Pc, items.Count,
            (progress, ct) => _saveService.SaveToPcAsync(items, session, targetDir, _settings.Current.AutoRename, progress, ct));

        if (result.SavedCount > 0)
        {
            LastOutputFolder = result.OutputFolder ?? targetDir;
        }

        ReportOutcome(result, Destination.Pc, items.Count);
    }

    [RelayCommand]
    private async Task SaveToAndroidAsync()
    {
        var items = TakeSelection();
        if (items == null)
        {
            return;
        }

        // 来源：Switch（直传，不在电脑留副本）或 电脑里的照片
        var source = ActiveSession;
        if (source == null)
        {
            ShowToast(Strings.Status_NeedSwitch);
            return;
        }

        // 先解析手机设备再进入传输状态：未连手机时只有提示，不闪进度条
        var phone = await ResolvePhoneAsync(CancellationToken.None);
        if (phone == null)
        {
            return;
        }

        await using var phoneSession = phone.Value.Session;
        var phoneKey = phone.Value.DeviceId;
        var result = await RunTransferAsync(Destination.Android, items.Count,
            (progress, ct) => _phoneSaveService.SaveToPhoneAsync(
                items, source, phoneSession, _settings.Current.AutoRename, progress, ct, phoneKey));

        ReportOutcome(result, Destination.Android, items.Count);
    }

    [RelayCommand]
    private async Task SendToIphoneAsync()
    {
        var items = TakeSelection();
        if (items == null)
        {
            return;
        }

        IReadOnlyList<SharedFile> files;
        if (IsLibrarySource && _localSession is { } local)
        {
            // 已在电脑上：直接分享原文件
            files = IphoneShareService.FromLocalFiles(items.Select(i => (i.GameTitle, local.ToLocalPath(i.DevicePath))));
        }
        else if (_session is { } session)
        {
            // Switch 直传：下载到临时文件夹（不进保存位置），退出时自动删除
            IReadOnlyList<SharedFile> prepared = Array.Empty<SharedFile>();
            var result = await RunTransferAsync(Destination.Iphone, items.Count, async (progress, ct) =>
            {
                var (shared, r) = await _iphoneShare.PrepareFromDeviceAsync(
                    items, session, _settings.Current.AutoRename, progress, ct);
                prepared = shared;
                return r;
            });

            if (result.Cancelled)
            {
                ProgressText = string.Format(Strings.Status_SavedPartial, result.SavedCount, items.Count);
                return;
            }

            if (result.Failed.Count > 0)
            {
                ShowToast(string.Format(Strings.Status_SomeFailed, result.Failed.Count, result.Failed[0].Error));
            }

            files = prepared;
        }
        else
        {
            ShowToast(Strings.Status_NeedSwitch);
            return;
        }

        if (files.Count == 0)
        {
            ShowToast(Strings.Iphone_NothingShared);
            return;
        }

        if (!EnsureIphoneServer())
        {
            return;
        }

        _iphoneServer!.ShareFiles(files);
        ProgressText = string.Format(Strings.Iphone_Summary, files.Count);
        ProgressValue = 100;
        var dialog = new Views.IphoneDialog(_iphoneServer, string.Format(Strings.Iphone_Summary, files.Count)) { Owner = _owner };
        dialog.ShowDialog();
    }

    private bool EnsureIphoneServer()
    {
        if (_iphoneServer is { IsRunning: true })
        {
            return true;
        }

        try
        {
            _iphoneServer?.Dispose();
            _iphoneServer = new IphoneWebServer();
            _iphoneServer.Start();
            Log.Info($"iPhone 网页服务已启动: {_iphoneServer.Url}（候选地址: {string.Join(", ", _iphoneServer.CandidateAddresses)}）");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("iPhone 网页服务启动失败", ex);
            _iphoneServer = null;
            ShowToast(string.Format(Strings.Iphone_ServerFailed, ex.Message));
            return false;
        }
    }

    private static bool IsAppleDeviceName(string name)
        => name.Contains("iphone", StringComparison.OrdinalIgnoreCase)
           || name.Contains("ipad", StringComparison.OrdinalIgnoreCase)
           || name.Contains("apple", StringComparison.OrdinalIgnoreCase);

    private async Task<(IMediaDeviceSession Session, string DeviceId)?> ResolvePhoneAsync(CancellationToken ct)
    {
        using var _ = _provider.SuspendPolling();

        // 仅 MTP 协议设备（安卓手机）为候选；MSC 硬盘/U 盘被过滤；正在使用的 Switch 也排除（Switch 2 名称可能不含 Switch）
        var phones = (await _provider.GetPhoneCandidateDevicesAsync(ct))
            .Where(p => !string.Equals(p.DeviceId, _deviceKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (phones.Count == 0)
        {
            // iPhone 走 PTP、不在 MTP 候选里：给出明确的替代方案
            var all = await _provider.GetDevicesAsync(ct);
            ShowToast(all.Any(d => !d.IsSwitch && IsAppleDeviceName(d.FriendlyName))
                ? Strings.Phone_AppleUnsupported
                : Strings.Status_NoPhone);
            return null;
        }

        MediaDeviceInfo? phone = null;
        var storedId = _settings.Current.PhoneDeviceId;
        if (!string.IsNullOrEmpty(storedId))
        {
            phone = phones.FirstOrDefault(p => p.DeviceId == storedId);
        }

        if (phone == null && phones.Count == 1)
        {
            phone = phones[0];
        }

        if (phone == null)
        {
            var dialog = new Views.DevicePickerDialog(phones) { Owner = _owner };
            if (dialog.ShowDialog() != true || dialog.SelectedDevice == null)
            {
                return null;
            }

            phone = dialog.SelectedDevice;
            _settings.Current.PhoneDeviceId = phone.DeviceId;
            _settings.Current.PhoneFriendlyName = phone.FriendlyName;
            _settings.Save();
        }

        try
        {
            var session = await _provider.ConnectAsync(phone, ct);
            return (session, phone.DeviceId);
        }
        catch (Exception ex)
        {
            Log.Error("连接手机失败: " + phone.FriendlyName, ex);
            ShowToast(Strings.Status_NoPhone);
            return null;
        }
    }

    // =====================================================================
    // 传输公共流程：进度 / 速度 / 剩余时间 / 取消 / 暂停设备轮询
    // =====================================================================
    private async Task<SaveResult> RunTransferAsync(
        Destination destination, int total,
        Func<IProgress<SaveProgress>, CancellationToken, Task<SaveResult>> work)
    {
        _transferCts = new CancellationTokenSource();
        var ct = _transferCts.Token;
        var generation = ++_transferGeneration;
        _speed.Reset();
        LastOutputFolder = null;
        IsTransferring = true;
        ProgressValue = 0;
        ProgressText = "";

        using var pause = _provider.SuspendPolling();
        SaveResult result;
        try
        {
            result = await work(new Progress<SaveProgress>(p => ReportProgress(p, destination, generation)), ct);
        }
        catch (OperationCanceledException)
        {
            result = new SaveResult { Cancelled = true };
        }
        catch (Exception ex)
        {
            Log.Error("传输失败", ex);
            result = new SaveResult { Failed = new[] { (new AlbumItem { GameTitle = "", DevicePath = "", FileName = "" }, ex.Message) } };
        }
        finally
        {
            IsTransferring = false;
            _transferCts.Dispose();
            _transferCts = null;
        }

        RefreshSavedStates();
        if (result.Failed.Count > 0)
        {
            Log.Info($"本次 {result.Failed.Count}/{total} 个失败，首个原因: {result.Failed[0].Error}");
        }

        return result;
    }

    private readonly SpeedMeter _speed = new();

    private void ReportProgress(SaveProgress p, Destination destination, int generation)
    {
        // 进度回调经 dispatcher 异步投递，可能在传输已结束后才到达：按代次丢弃滞后回调
        if (!IsTransferring || generation != _transferGeneration)
        {
            return;
        }

        var format = destination switch
        {
            Destination.Android => Strings.Status_PhoneProgress,
            Destination.Iphone => Strings.Status_IphoneProgress,
            _ => Strings.Status_Progress,
        };

        var text = string.Format(format, p.Done, p.Total, Path.GetFileName(p.CurrentFileName ?? ""));
        var (mbs, eta) = _speed.Update(p.BytesDone, p.Done - p.Skipped, p.Total - p.Skipped);
        if (mbs > 0 && p.Done < p.Total)
        {
            text += string.Format(Strings.Status_SpeedSuffix, mbs.ToString("F1"));
        }

        if (eta != null && p.Done < p.Total)
        {
            text += string.Format(Strings.Status_EtaSuffix, eta);
        }

        ProgressText = text;
        ProgressValue = p.Total == 0 ? 0 : p.Done * 100.0 / p.Total;
    }

    /// <summary>传输结束：状态栏留下持久文字 + 轻提示。</summary>
    private void ReportOutcome(SaveResult result, Destination destination, int total)
    {
        ProgressValue = 100;
        string text;
        if (result.Cancelled)
        {
            text = string.Format(Strings.Status_SavedPartial, result.SavedCount, total);
        }
        else if (result.SavedCount == 0 && result.Failed.Count > 0)
        {
            text = string.Format(Strings.Msg_SaveFailed, result.Failed[0].Error);
        }
        else if (result.SavedCount == 0 && result.SkippedCount > 0)
        {
            text = Strings.Status_AllSkipped;
        }
        else if (destination == Destination.Android)
        {
            text = string.Format(Strings.Status_PhoneDone, result.SavedCount, result.SkippedCount);
        }
        else
        {
            text = string.Format(Strings.Status_SavedDone, result.SavedCount, result.SkippedCount, result.Failed.Count);
        }

        if (result.SavedCount > 0 && result.Failed.Count > 0)
        {
            text += " · " + string.Format(Strings.Status_SomeFailed, result.Failed.Count, result.Failed[0].Error);
        }

        ProgressText = text;
        ShowToast(text);
    }

    /// <summary>保存后刷新所有照片的「已保存」标记（之前只在重新扫描后才更新）。</summary>
    private void RefreshSavedStates()
    {
        foreach (var scan in new[] { _scan, _localScan })
        {
            if (scan == null)
            {
                continue;
            }

            foreach (var item in scan.AllItems)
            {
                item.Saved = _tracker.GetState(item);
            }
        }

        foreach (var wall in new[] { Wall, LocalWall })
        {
            if (wall == null)
            {
                continue;
            }

            foreach (var card in wall.Cards)
            {
                card.RefreshSaved();
            }
        }

        Grid?.RefreshSaved();
        LocalGrid?.RefreshSaved();
    }

    [RelayCommand]
    private void CancelTransfer() => _transferCts?.Cancel();

    [RelayCommand]
    private void OpenLastOutput() => OpenFolder(LastOutputFolder ?? _settings.Current.SavePath);

    [RelayCommand]
    private void OpenSaveFolder() => OpenFolder(_settings.Current.SavePath);

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Info("打开文件夹失败: " + ex.Message);
            ShowToast(Strings.Msg_PathInvalid);
        }
    }

    // =====================================================================
    // 设置
    // =====================================================================
    [RelayCommand]
    private async Task ChangeSavePathAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.Settings_PickFolder,
            InitialDirectory = Directory.Exists(_settings.Current.SavePath)
                ? _settings.Current.SavePath
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };

        if (_owner == null || dialog.ShowDialog(_owner) != true)
        {
            return;
        }

        _settings.Current.SavePath = dialog.FolderName;
        _settings.Save();
        await OnSavePathMaybeChangedAsync();
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var viewModel = new SettingsViewModel(_settings, _theme, _provider, _tracker);
        var dialog = new Views.SettingsDialog(viewModel) { Owner = _owner };
        dialog.ShowDialog();
        await OnSavePathMaybeChangedAsync();
    }

    private async Task OnSavePathMaybeChangedAsync()
    {
        var changed = !string.Equals(SavePathText, _settings.Current.SavePath, StringComparison.OrdinalIgnoreCase);
        SavePathText = _settings.Current.SavePath;
        _tracker.LegacySaveRoot = _settings.Current.SavePath;
        RefreshSavedStates();
        if (changed && IsLibrarySource)
        {
            await LoadLibraryAsync();
        }
    }

    // =====================================================================
    // 轻提示
    // =====================================================================
    public void ShowToast(string text)
    {
        ToastText = text;
        ToastVisible = true;
        var sequence = ++_toastSequence;
        _ = Task.Delay(Math.Clamp(text.Length * 120, 3500, 8000)).ContinueWith(_ =>
        {
            _ui?.Post(__ =>
            {
                if (sequence == _toastSequence)
                {
                    ToastVisible = false;
                }
            }, null);
        }, TaskScheduler.Default);
    }
}

/// <summary>传输速度（约 1 秒滑动窗口）与剩余时间（按已完成文件的平均耗时估算）。</summary>
internal sealed class SpeedMeter
{
    private long _lastBytes;
    private long _lastTicks;
    private long _startTicks;
    private double _mbs;

    public void Reset()
    {
        _lastBytes = 0;
        _lastTicks = 0;
        _startTicks = 0;
        _mbs = 0;
    }

    public (double MbPerSecond, string? Eta) Update(long bytesDone, int filesDone, int filesTotal)
    {
        var now = Stopwatch.GetTimestamp();
        if (_startTicks == 0)
        {
            _startTicks = now;
        }

        if (_lastTicks == 0)
        {
            _lastBytes = bytesDone;
            _lastTicks = now;
        }
        else
        {
            var elapsed = (double)(now - _lastTicks) / Stopwatch.Frequency;
            if (elapsed >= 1.0)
            {
                _mbs = (bytesDone - _lastBytes) / elapsed / (1024.0 * 1024.0);
                _lastBytes = bytesDone;
                _lastTicks = now;
            }
        }

        string? eta = null;
        var total = (double)(now - _startTicks) / Stopwatch.Frequency;
        if (filesDone >= 2 && filesTotal > filesDone && total >= 2)
        {
            var remaining = TimeSpan.FromSeconds(total / filesDone * (filesTotal - filesDone));
            eta = remaining.TotalMinutes >= 1
                ? $"{(int)remaining.TotalMinutes} 分 {remaining.Seconds} 秒"
                : $"{Math.Max(1, remaining.Seconds)} 秒";
        }

        return (_mbs, eta);
    }
}
