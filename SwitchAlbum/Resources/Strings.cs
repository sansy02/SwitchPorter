namespace SwitchAlbum.Resources;

/// <summary>全应用中文文案，XAML 中通过 {x:Static} 引用。</summary>
public static class Strings
{
    public const string App_Title = "Switch 相册管家";

    // ---------- 设备状态 ----------
    public const string Status_Scanning = "正在扫描相册…";
    public const string Status_Connected = "已连接";
    public const string Status_NotConnected = "未连接 Switch";
    public const string Status_Connecting = "正在连接…";
    public const string Status_LoadingTitleDb = "正在加载游戏库…";

    // ---------- 空状态 ----------
    public const string Hint_NotConnectedTitle = "未连接 Switch";
    public const string Hint_ConnectSwitch = "请用 USB 数据线连接 Switch 与电脑，并在主机上进入\n系统设置 → 数据管理 → 管理截图与视频 → 通过 USB 连接复制到电脑\n连接后会自动识别（也可以点右上角「刷新」）";
    public const string Hint_EmptyAlbumTitle = "相册是空的";
    public const string Hint_EmptyAlbum = "Switch 已连接，但没有找到截图或视频（Album 目录为空）";
    public const string Hint_LibraryEmptyTitle = "电脑里还没有保存的照片";
    public const string Local_EmptyHint = "在「Switch 相册」里勾选照片，点「存到电脑」后会出现在这里。\n想直接传到手机，不需要先存电脑：在「Switch 相册」里选好照片，点「存到安卓手机」或「发送到 iPhone」。";
    public const string Hint_LoadingTitle = "正在准备…";

    // ---------- 顶栏 ----------
    public const string Tab_SwitchAlbum = "Switch 相册";
    public const string Tab_Library = "电脑里的照片";
    public const string Btn_Refresh = "刷新";
    public const string Btn_Settings = "设置";

    // ---------- 操作栏（选好照片后选择去处） ----------
    public const string Action_PickHint = "点开一个游戏（或「全部」），勾选照片后在这里选择去处";
    public const string Action_Selected = "已选 {0} 项";
    public const string Action_SaveToPc = "存到电脑";
    public const string Action_SaveToAndroid = "存到安卓手机";
    public const string Action_SendToIphone = "发送到 iPhone";
    public const string Action_SaveToPcTip = "按游戏分文件夹保存到：{0}";
    public const string Action_AndroidTip = "用 USB 连接安卓手机（选择「传输文件」），直接传到手机相册 DCIM\\SwitchAlbum，不在电脑留副本";
    public const string Action_IphoneTip = "iPhone 与电脑连同一个 WiFi，扫码在浏览器里保存（无需安装 App）";
    public const string Lbl_SaveTo = "保存位置";
    public const string Lbl_SavePath = "保存位置（存到电脑时按游戏分文件夹）";
    public const string Btn_ChangePath = "更改";
    public const string Btn_OpenSaveFolder = "打开保存文件夹";
    public const string Btn_OpenFolder = "打开文件夹";

    // ---------- 网格 ----------
    public const string Btn_Back = "返回全部游戏";
    public const string Btn_SelectAll = "全选";
    public const string Btn_DeselectAll = "取消全选";
    public const string Btn_SelectUnsaved = "选未保存的";
    public const string Btn_Cancel = "取消";
    public const string Btn_OpenLogs = "打开日志文件夹";
    public const string Btn_OpenVideo = "打开播放";
    public const string Wall_SearchHint = "搜索游戏";
    public const string Wall_NoResults = "没有找到匹配的游戏";
    public const string Sort_NewestFirst = "从新到旧";
    public const string Sort_OldestFirst = "从旧到新";
    public const string Sort_Tooltip = "按拍摄时间排序";
    public const string Lbl_Selected = "已选 {0} 项";
    public const string Card_AllGames = "全部";
    public const string Card_PhotoCount = "{0} 张照片";
    public const string Card_VideoCount = "{0} 个视频";
    public const string Badge_Video = "视频";
    public const string Badge_Saved = "已保存";
    public const string Badge_OnPc = "已存电脑";
    public const string Badge_OnPhone = "已存手机";
    public const string Badge_OnBoth = "电脑+手机";
    public const string Cover_Unknown = "无封面";

    // ---------- 传输进度 / 结果 ----------
    public const string Status_Progress = "正在保存 {0}/{1}：{2}";
    public const string Status_PhoneProgress = "正在传到手机 {0}/{1}：{2}";
    public const string Status_IphoneProgress = "正在准备 iPhone 分享 {0}/{1}：{2}";
    public const string Status_SpeedSuffix = " · {0} MB/s";
    public const string Status_EtaSuffix = " · 剩余约 {0}";
    public const string Status_SavedDone = "已保存 {0} 个，跳过 {1} 个（之前已保存），失败 {2} 个";
    public const string Status_SavedPartial = "已取消，完成 {0}/{1} 个";
    public const string Status_NoSelection = "请先勾选照片";
    public const string Status_NoPhone = "未检测到安卓手机：请用 USB 连接、解锁手机，并在通知栏选择「传输文件」";
    public const string Status_PhoneDone = "已传到手机 {0} 个，跳过 {1} 个（这部手机上已有）";
    public const string Status_SaveSuccessCount = "保存成功 · {0} 个";
    public const string Status_PhoneSuccessCount = "已传到手机 · {0} 个";
    public const string Status_AllSkipped = "所选照片之前都已保存过，已跳过";
    public const string Status_SaveFailedShort = "保存失败";
    public const string Status_SomeFailed = "{0} 个失败：{1}";
    public const string Status_Busy = "正在传输，请等待完成或先取消";
    public const string Status_NeedSwitch = "Switch 未连接，无法从 Switch 读取照片";
    public const string Msg_PathInvalid = "保存位置不可用，请重新选择";
    public const string Msg_SaveFailed = "保存失败：{0}";

    // ---------- 设置 ----------
    public const string Dlg_Settings_Title = "设置";
    public const string Settings_Theme = "主题";
    public const string Settings_Theme_System = "跟随系统";
    public const string Settings_Theme_Light = "浅色";
    public const string Settings_Theme_Dark = "深色";
    public const string Settings_AutoRename = "保存时自动重命名（日期_游戏名_序号）";
    public const string Settings_AutoRenameExample = "示例：20250912_塞尔达传说 王国之泪_001.jpg";
    public const string Settings_PhoneDevice = "安卓手机";
    public const string Settings_PhoneDevice_Auto = "自动检测";
    public const string Settings_PickFolder = "选择保存文件夹";
    public const string Settings_Close = "关闭";
    public const string Btn_ClearCache = "清除封面与缩略图缓存";
    public const string Btn_ClearSaved = "清除「已保存」记录";
    public const string Settings_ClearSavedConfirm = "清除后，所有照片都会显示为「未保存」，再次保存时不会被跳过（不会删除任何已保存的文件）。\n\n确定清除吗？";
    public const string Settings_ClearSavedDone = "已清除 {0} 条记录";
    public const string Settings_SavedCount = "共 {0} 条「已保存」记录";
    public const string Status_CacheCleared = "缓存已清除";
    public const string Status_CacheClearedWithSize = "缓存已清除（释放 {0}）";
    public const string Settings_CacheSize = "当前缓存占用 {0}";
    public const string Settings_CacheSizeNone = "当前无缓存";

    // ---------- 手机设备选择 ----------
    public const string Phone_Picker_Title = "选择手机设备";
    public const string Phone_Refresh = "刷新";
    public const string Phone_None = "未检测到手机设备";
    public const string Phone_AppleUnsupported = "检测到 iPhone：iOS 不允许电脑通过 USB 写入相册。请改用「发送到 iPhone」（局域网扫码）";

    // ---------- iPhone 局域网方案 ----------
    public const string Iphone_Dialog_Title = "发送到 iPhone";
    public const string Iphone_Summary = "已准备好 {0} 个文件，用 iPhone 扫码打开";
    public const string Iphone_ServerFailed = "局域网服务启动失败：{0}";
    public const string Iphone_NothingShared = "没有可发送的文件";
    public const string Iphone_AddressLabel = "地址";
    public const string Iphone_AddressTip = "电脑有多个网络时，如果扫码打不开，换一个地址再试";
    public const string Iphone_CopyUrl = "复制链接";
    public const string Iphone_Copied = "已复制";
    public const string Iphone_Step1 = "iPhone 和电脑需要连接同一个 WiFi（同一个路由器）";
    public const string Iphone_Step2 = "① 用 iPhone 相机扫上面的二维码，在 Safari 打开";
    public const string Iphone_Step3 = "② 单张：点开照片后长按 →「存储到照片」；视频点开后用分享按钮存储";
    public const string Iphone_Step4 = "③ 批量：点「下载 ZIP」→「文件」App 里点开 ZIP 解压 → 全选 → 分享 →「存储图像」";
    public const string Iphone_TroubleTitle = "扫码后一直打不开？";
    public const string Iphone_Trouble1 = "• 确认手机连的是 WiFi（不是蜂窝数据），且和电脑是同一个网络；校园网 / 酒店 WiFi 常会隔离设备，可改用手机热点给电脑上网";
    public const string Iphone_Trouble2 = "• 电脑开着 VPN / 加速器 / Clash 的 TUN 模式时，二维码可能用了错误的地址：先在上面换一个地址，不行再暂时关闭它们";
    public const string Iphone_Trouble3 = "• Windows 防火墙可能拦截了连接（首次运行时的弹窗被取消、或网络被设为「公用网络」）：点下面按钮一键放行";
    public const string Iphone_AllowFirewall = "允许通过防火墙（需要管理员）";
    public const string Iphone_FirewallDone = "已放行，请让手机刷新页面";
    public const string Iphone_FirewallFailed = "未完成（取消了授权？）";
    public const string Iphone_Note = "说明：iOS 不允许任何软件通过 USB 或网页直接写入相册，以上是苹果允许的方式。关闭软件后链接失效。";

    // ---------- 通用 ----------
    public const string Common_Ok = "确定";
    public const string Common_Cancel = "取消";
    public const string Lightbox_NoPreview = "视频无法在这里预览";
}
