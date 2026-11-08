using SwitchAlbum.Models;
using SwitchAlbum.Services;
using SwitchAlbum.Services.Parsing;
using SwitchAlbum.ViewModels;

var tests = new List<(string Name, Func<Task<bool>> Run)>();

void Add(string name, Func<bool> run) => tests.Add((name, () => Task.FromResult(run())));
void AddAsync(string name, Func<Task<bool>> run) => tests.Add((name, run));

static AlbumItem Item(string game, string file, DateTime ts, bool video = false) => new()
{
    GameTitle = game,
    DevicePath = "\\Album\\" + game + "\\" + file,
    FileName = file,
    Timestamp = ts,
    IsVideo = video,
};

// ---------- TimestampParser ----------
Add("时间戳: 16位带后缀", () =>
    TimestampParser.TryParse("2025031118460900_s", out var d) && d == new DateTime(2025, 3, 11, 18, 46, 9));
Add("时间戳: 14位", () =>
    TimestampParser.TryParse("20250311184609", out var d) && d == new DateTime(2025, 3, 11, 18, 46, 9));
Add("时间戳: 非法输入", () =>
    !TimestampParser.TryParse("2025133218460900", out _)
    && !TimestampParser.TryParse("abc", out _)
    && !TimestampParser.TryParse("2025031", out _));

// ---------- FolderNameParser ----------
Add("文件夹: 游戏名直读", () =>
{
    var r = FolderNameParser.Parse("塞尔达传说 王国之泪");
    return r.Title == "塞尔达传说 王国之泪" && r.TitleId == null && r.Timestamp == null;
});
Add("文件夹: 旧式titleid", () =>
{
    var r = FolderNameParser.Parse("2024081512304500-0100f2c0115b6000");
    return r.TitleId == "0100F2C0115B6000" && r.Timestamp == new DateTime(2024, 8, 15, 12, 30, 45);
});
Add("文件夹: 空名", () => FolderNameParser.Parse("   ").Title == null);
Add("文件夹: 纯日期不误判旧式", () =>
{
    var r = FolderNameParser.Parse("20240815");
    return r.Title == "20240815" && r.TitleId == null;
});

// ---------- FileNameSanitizer ----------
Add("清理: 非法字符替换", () =>
    FileNameSanitizer.Sanitize("A:B/C\\D*E?F\"G<H>I|J") == "A B C D E F G H I J");
Add("清理: 保留全角字符", () =>
    FileNameSanitizer.Sanitize("NARUTO×BORUTO 新忍出击") == "NARUTO×BORUTO 新忍出击");
Add("清理: 保留名加前缀", () =>
    FileNameSanitizer.Sanitize("CON").StartsWith('_') && FileNameSanitizer.Sanitize("con").StartsWith('_'));
Add("清理: 去结尾点与空格", () => FileNameSanitizer.Sanitize("abc.. ") == "abc");
Add("清理: 空输入", () => FileNameSanitizer.Sanitize("   ") == "未知");
Add("清理: 超长截断", () => FileNameSanitizer.Sanitize(new string('长', 300)).Length <= 120);
Add("清理: 含点号非保留名", () =>
    FileNameSanitizer.Sanitize("Mario vs. Donkey Kong") == "Mario vs. Donkey Kong");

// ---------- RenamePlanner ----------
Add("重命名: 同组序号递增", () =>
{
    var items = new[]
    {
        Item("游戏A", "2025031118460900_s.jpg", new DateTime(2025, 3, 11, 18, 46, 9)),
        Item("游戏A", "2025031118461000_s.jpg", new DateTime(2025, 3, 11, 18, 46, 10)),
    };
    var plan = RenamePlanner.Plan(items, null, true);
    return plan.Count == 2
        && plan.Values.Contains("20250311_游戏A_001.jpg")
        && plan.Values.Contains("20250311_游戏A_002.jpg");
});
Add("重命名: 跨日期序号重置", () =>
{
    var items = new[]
    {
        Item("游戏A", "a.jpg", new DateTime(2025, 3, 11, 1, 0, 0)),
        Item("游戏A", "b.jpg", new DateTime(2025, 3, 12, 1, 0, 0)),
    };
    var plan = RenamePlanner.Plan(items, null, true);
    return plan.Values.Contains("20250311_游戏A_001.jpg") && plan.Values.Contains("20250312_游戏A_001.jpg");
});
Add("重命名: 与已有文件冲突避让", () =>
{
    var plan = RenamePlanner.Plan(
        new[] { Item("游戏A", "x.jpg", new DateTime(2025, 3, 11, 1, 0, 0)) },
        new[] { "20250311_游戏A_001.jpg" }, true);
    return plan.Values.Single() == "20250311_游戏A_002.jpg";
});
Add("重命名: 保留原名", () =>
{
    var plan = RenamePlanner.Plan(
        new[] { Item("游戏A", "2025031118460900_s.jpg", new DateTime(2025, 3, 11, 18, 46, 9)) },
        null, false);
    return plan.Values.Single() == "2025031118460900_s.jpg";
});
Add("重命名: 原名冲突追加序号", () =>
{
    var plan = RenamePlanner.Plan(
        new[] { Item("游戏A", "a.jpg", default), Item("游戏B", "a.jpg", default) }, null, false);
    return plan.Count == 2 && plan.Values.Contains("a.jpg") && plan.Values.Contains("a_1.jpg");
});
Add("重命名: 无时间戳用未知日期", () =>
{
    var plan = RenamePlanner.Plan(new[] { Item("游戏A", "x.jpg", default) }, null, true);
    return plan.Values.Single() == "未知日期_游戏A_001.jpg";
});
Add("重命名: 游戏名含非法字符被清理", () =>
{
    var plan = RenamePlanner.Plan(
        new[] { Item("A:B", "x.jpg", new DateTime(2025, 3, 11, 1, 0, 0)) }, null, true);
    return plan.Values.Single() == "20250311_A B_001.jpg";
});
Add("重命名: 视频扩展名保留", () =>
{
    var plan = RenamePlanner.Plan(
        new[] { Item("游戏A", "2025031118460900_s.mp4", new DateTime(2025, 3, 11, 18, 46, 9), video: true) },
        null, true);
    return plan.Values.Single() == "20250311_游戏A_001.mp4";
});

// ---------- DuplicateTracker ----------
Add("去重: 标记持久化", () =>
{
    var file = TempFile();
    try
    {
        var t1 = new DuplicateTracker(file);
        t1.MarkSaved("游戏A", "a.jpg", "20250311_游戏A_001.jpg", toPc: true, toPhone: false);
        t1.Flush();
        var t2 = new DuplicateTracker(file);
        return t2.GetState("游戏A", "a.jpg") == SavedState.SavedToPc
            && t2.GetState("游戏A", "b.jpg") == SavedState.NotSaved;
    }
    finally { File.Delete(file); }
});
Add("去重: 电脑与手机状态独立", () =>
{
    var file = TempFile();
    try
    {
        var t1 = new DuplicateTracker(file);
        t1.MarkSaved("游戏A", "a.jpg", null, toPc: false, toPhone: true);
        t1.Flush();
        var t2 = new DuplicateTracker(file);
        return t2.GetState("游戏A", "a.jpg") == SavedState.SavedToPhone;
    }
    finally { File.Delete(file); }
});

// ---------- TitleDb ----------
Add("标题库: 资源名诊断", () =>
{
    foreach (var name in typeof(TitleDbService).Assembly.GetManifestResourceNames())
    {
        Console.WriteLine("  RES: " + name);
    }

    return true;
});
Add("标题库: 规范化", () =>
{
    return TitleDbService.Normalize("集合啦！动物森友会") == "集合啦动物森友会"
        && TitleDbService.Normalize("Mario vs. Donkey Kong") == "mariovsdonkeykong"
        && TitleDbService.Normalize("スプラトゥーン３") == "スプラトゥーン3";
});
Add("标题库: 嵌入库加载与查名", () =>
{
    var db = TitleDbService.LoadEmbedded();
    if (db == null)
    {
        return false;
    }

    var totk = db.GetNameByTitleId("0100F2C0115B6000");
    var mk8 = db.GetTitleIdByName("马力欧卡丁车8 豪华版");
    return totk is { Length: > 0 }
        && db.GetIconUrl("0100F2C0115B6000")?.Contains("img-eshop.cdn.nintendo.net") == true
        && mk8 is { Length: 16 } && mk8.All(Uri.IsHexDigit);
});
Add("标题库: 繁体名注册简体变体（含品牌别名）", () =>
{
    var db = TitleDbService.LoadEmbedded();
    // HK.zh 只有繁体「薩爾達傳說」，注册后应能按官方简体反查（别名 萨尔达→塞尔达）
    return db?.GetTitleIdByName("塞尔达传说 王国之泪") == "0100F2C0115B6000"
        && db.GetTitleIdByName("薩爾達傳說 王國之淚") == "0100F2C0115B6000"
        && db.GetTitleIdByName("塞尔达传说 旷野之息") == "01007EF00011E000";
});

// ---------- 标题库: 文件夹名模糊反查（合成库，确定性） ----------
static TitleDbService SyntheticDb()
{
    var json = """
    {
      "0100AAAA00001000": { "zh": "塞尔达传说 王国之泪", "en": "The Legend of Zelda: Tears of the Kingdom" },
      "0100AAAA00002000": { "zh": "超级马力欧兄弟 惊奇" },
      "0100AAAA00003000": { "en": "Splatoon 3" },
      "0100AAAA00004000": { "zht": "霍格華茲的傳承" },
      "0100AAAA00005000": { "ja": "ゼノブレイド２" },
      "0100AAAA00006000": { "zh": "神之天平 Revision" }
    }
    """;
    var t2s = "華\t华\n茲\t兹\n傳\t传\n承\t承\n";
    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    using var t2sStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(t2s));
    return new TitleDbService(stream, t2sStream);
}

Add("模糊反查: Switch 2 后缀剥离", () =>
{
    var db = SyntheticDb();
    return db.GetTitleIdByFolderName("塞尔达传说 王国之泪 Nintendo Switch 2 Edition") == "0100AAAA00001000"
        && db.GetTitleIdByFolderName("塞尔达传说 王国之泪 體驗版") == "0100AAAA00001000"
        && db.GetTitleIdByFolderName("塞尔达传说 王国之泪 DEMO") == "0100AAAA00001000";
});
Add("模糊反查: 括号注释剥离", () =>
{
    var db = SyntheticDb();
    return db.GetTitleIdByFolderName("Splatoon 3（斯普拉遁 3）") == "0100AAAA00003000"
        && db.GetTitleIdByFolderName("Splatoon 3 (スプラトゥーン3)") == "0100AAAA00003000";
});
Add("模糊反查: 包含匹配（后缀+捆绑）", () =>
{
    var db = SyntheticDb();
    return db.GetTitleIdByFolderName("超级马力欧兄弟 惊奇 Nintendo Switch 2 Edition + 同游铃铃公园") == "0100AAAA00002000"
        && db.GetTitleIdByFolderName("神之天平") == "0100AAAA00006000";
});
Add("模糊反查: 品牌别名（繁体霍格華茲→霍格沃茨之遗）", () =>
{
    var db = SyntheticDb();
    return db.GetTitleIdByFolderName("霍格沃茨之遗") == "0100AAAA00004000";
});
Add("模糊反查: 日文名注册与全角数字", () =>
{
    var db = SyntheticDb();
    return db.GetTitleIdByFolderName("ゼノブレイド2") == "0100AAAA00005000";
});
Add("模糊反查: 真实库 Switch 2 风格文件夹名", () =>
{
    var db = TitleDbService.LoadEmbedded();
    return db?.GetTitleIdByFolderName("塞尔达传说 王国之泪 Nintendo Switch 2 Edition") == "0100F2C0115B6000"
        && db.GetTitleIdByFolderName("塞尔达传说 旷野之息 Nintendo Switch 2 Edition") == "01007EF00011E000";
});
Add("模糊反查: 真实库日文文件夹名", () =>
{
    var db = TitleDbService.LoadEmbedded();
    // titles.json 已合并 titledb JP.ja 日文名
    var xb2 = db?.GetTitleIdByFolderName("ゼノブレイド２");
    return xb2 is { Length: 16 } && xb2.All(Uri.IsHexDigit);
});

// ---------- 集成: 模拟设备扫描 ----------
AddAsync("模拟: 扫描相册结构", async () =>
{
    var root = TempDir();
    try
    {
        DemoAlbumGenerator.Generate(root);
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var devices = await provider.GetDevicesAsync(default);
        var switchDevice = devices.Single(d => d.IsSwitch);
        await using var session = await provider.ConnectAsync(switchDevice, default);
        var scan = await AlbumScanner.ScanAsync(session, null, default);
        if (scan == null)
        {
            return false;
        }

        // 6 个中文游戏 + 旧式文件夹 + 全角符号游戏 = 8 个游戏
        return scan.Games.Count == 8
            && scan.Games.Any(g => g.Title == "NARUTO×BORUTO 新忍出击")
            && scan.Games.Any(g => g.Title == "未知游戏 (0100F2C0…)")
            && scan.AllItems.Count > 20
            && scan.AllItems.Any(i => i.IsVideo)
            && scan.AllItems.All(i => i.Timestamp != default);
    }
    finally { Directory.Delete(root, recursive: true); }
});

// ---------- 集成: 保存到电脑 + 重复跳过 ----------
AddAsync("模拟: 保存到电脑按游戏分文件夹并跳过重复", async () =>
{
    var root = TempDir();
    try
    {
        DemoAlbumGenerator.Generate(root);
        var outDir = Path.Combine(root, "out");
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var tracker = new DuplicateTracker(Path.Combine(root, "saved.json"));
        var save = new SaveService(tracker);
        var devices = await provider.GetDevicesAsync(default);
        await using var session = await provider.ConnectAsync(devices.Single(d => d.IsSwitch), default);
        var scan = await AlbumScanner.ScanAsync(session, null, default);
        if (scan == null)
        {
            return false;
        }

        var r1 = await save.SaveToPcAsync(scan.AllItems, session, outDir, autoRename: true, null, default);
        var files = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories);
        var namesOk = files.All(f =>
        {
            var n = Path.GetFileName(f);
            return n.StartsWith("20") && n.Contains('_') && !n.EndsWith(".part");
        });
        var firstOk = r1.SavedCount == scan.AllItems.Count && r1.SkippedCount == 0 && r1.Failed.Count == 0;
        var videoOk = files.Any(f => f.EndsWith(".mp4"));

        // 每个文件都位于以其游戏名命名的子文件夹内
        var subdirOk = files.All(f =>
        {
            var dirName = Path.GetFileName(Path.GetDirectoryName(f)!)!;
            return dirName != "out" && Directory.Exists(Path.Combine(outDir, dirName));
        });
        var gameDirs = Directory.GetDirectories(outDir).Select(Path.GetFileName).ToArray();
        var totkOk = gameDirs.Contains("塞尔达传说 王国之泪");

        var r2 = await save.SaveToPcAsync(scan.AllItems, session, outDir, autoRename: true, null, default);
        var skipOk = r2.SkippedCount == scan.AllItems.Count && r2.SavedCount == 0;

        return namesOk && firstOk && videoOk && subdirOk && totkOk && skipOk;
    }
    finally { Directory.Delete(root, recursive: true); }
});

// ---------- 集成: 保存到手机按游戏分文件夹 ----------
AddAsync("模拟: 保存到手机按游戏分文件夹", async () =>
{
    var root = TempDir();
    try
    {
        DemoAlbumGenerator.Generate(root);
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var tracker = new DuplicateTracker(Path.Combine(root, "saved.json"));
        var settings = new SettingsService(Path.Combine(root, "settings.json"));
        var phoneSave = new PhoneSaveService(tracker, settings);
        var devices = await provider.GetDevicesAsync(default);
        await using var switchSession = await provider.ConnectAsync(devices.Single(d => d.IsSwitch), default);
        var phoneDevice = (await provider.GetPhoneCandidateDevicesAsync(default)).Single();
        await using var phoneSession = await provider.ConnectAsync(phoneDevice, default);
        var scan = await AlbumScanner.ScanAsync(switchSession, null, default);
        if (scan == null)
        {
            return false;
        }

        var r = await phoneSave.SaveToPhoneAsync(
            scan.AllItems, switchSession, phoneSession, autoRename: true, null, default);
        var dcim = Path.Combine(root, "phone", "Internal shared storage", "DCIM", "SwitchAlbum");
        var dirs = Directory.Exists(dcim) ? Directory.GetDirectories(dcim).Select(Path.GetFileName).ToArray() : Array.Empty<string?>();
        var files = Directory.Exists(dcim) ? Directory.GetFiles(dcim, "*", SearchOption.AllDirectories) : Array.Empty<string>();

        return r.SavedCount == scan.AllItems.Count
            && dirs.Contains("塞尔达传说 王国之泪")
            && !dirs.Any(d => d is { Length: 10 } && d.Contains('-'))   // 不再按日期建目录
            && files.Length == scan.AllItems.Count;
    }
    finally { Directory.Delete(root, recursive: true); }
});

// ---------- 集成: 本地相册页扫描 → 保存到手机 ----------
AddAsync("模拟: 本地目录扫描并保存到手机（本地相册页流程）", async () =>
{
    var root = TempDir();
    try
    {
        DemoAlbumGenerator.Generate(root);
        var outDir = Path.Combine(root, "out");
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var tracker = new DuplicateTracker(Path.Combine(root, "saved.json"));
        var settings = new SettingsService(Path.Combine(root, "settings.json"));
        var save = new SaveService(tracker);
        var phoneSave = new PhoneSaveService(tracker, settings);
        var devices = await provider.GetDevicesAsync(default);

        // 第一步：Switch → 电脑（分游戏文件夹）
        await using (var switchSession = await provider.ConnectAsync(devices.Single(d => d.IsSwitch), default))
        {
            var scan = await AlbumScanner.ScanAsync(switchSession, null, default);
            if (scan == null)
            {
                return false;
            }

            var r = await save.SaveToPcAsync(scan.AllItems, switchSession, outDir, autoRename: true, null, default);
            if (r.SavedCount != scan.AllItems.Count)
            {
                return false;
            }
        }

        // 第二步：本地目录会话扫描电脑保存目录（与本地相册页同一路径）
        await using var localSession = new LocalFolderSession(outDir);
        var localScan = await AlbumScanner.ScanAsync(localSession, null, default);
        var scanOk = localScan != null
            && localScan.AllItems.Count == Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Length
            && localScan.Games.Any(g => g.Title == "塞尔达传说 王国之泪");

        // 第三步：本地 → 手机
        var phoneDevice = (await provider.GetPhoneCandidateDevicesAsync(default)).Single();
        await using var phoneSession = await provider.ConnectAsync(phoneDevice, default);
        var r2 = await phoneSave.SaveToPhoneAsync(
            localScan!.AllItems, localSession, phoneSession, autoRename: true, null, default);
        var dcim = Path.Combine(root, "phone", "Internal shared storage", "DCIM", "SwitchAlbum");
        var gameDirOk = Directory.Exists(Path.Combine(dcim, "塞尔达传说 王国之泪"));

        return scanOk && r2.SavedCount == localScan.AllItems.Count && gameDirOk;
    }
    finally { Directory.Delete(root, recursive: true); }
});

// ---------- WindowBounds: 窗口位置自愈 ----------
Add("窗口边界: 屏幕内有效", () =>
    WindowBounds.IsVisibleOnScreen(100, 100, 800, 600, 0, 0, 1920, 1080));
Add("窗口边界: 完全在屏幕外(右侧)无效", () =>
    !WindowBounds.IsVisibleOnScreen(2000, 100, 800, 600, 0, 0, 1920, 1080));
Add("窗口边界: 完全在屏幕外(左侧/负坐标)无效", () =>
    !WindowBounds.IsVisibleOnScreen(-900, 100, 800, 600, 0, 0, 1920, 1080));
Add("窗口边界: 上方越界(标题栏不可及)无效", () =>
    !WindowBounds.IsVisibleOnScreen(100, -600, 800, 600, 0, 0, 1920, 1080));
Add("窗口边界: 只剩小条可见(<100px)无效", () =>
    !WindowBounds.IsVisibleOnScreen(1900, 100, 800, 600, 0, 0, 1920, 1080));
Add("窗口边界: 多屏布局(副屏在右)有效", () =>
    WindowBounds.IsVisibleOnScreen(2000, 100, 800, 600, 0, 0, 3840, 1080));
Add("窗口边界: 显示器拔掉后副屏位置无效", () =>
    !WindowBounds.IsVisibleOnScreen(2000, 100, 800, 600, 0, 0, 1920, 1080));

// ---------- 设备分类: 外置硬盘不进手机候选 ----------
AddAsync("设备分类: 手机候选不含模拟 MSC 硬盘", async () =>
{
    var root = TempDir();
    try
    {
        Directory.CreateDirectory(root);
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var all = await provider.GetDevicesAsync(default);
        var phones = await provider.GetPhoneCandidateDevicesAsync(default);

        // 全量枚举里有硬盘与手机；候选列表只应有手机
        return all.Count == 3
            && all.Any(d => d.FriendlyName.Contains("硬盘"))
            && phones.Count == 1
            && phones[0].DeviceId == MockMediaProvider.PhoneDeviceId
            && phones.All(p => p.IsPhoneCandidate);
    }
    finally { Directory.Delete(root, recursive: true); }
});

// ---------- iPhone 网页服务器 ----------
AddAsync("iPhone 网页: 首页与 ZIP 批量下载", async () =>
{
    var root = TempDir();
    try
    {
        DemoAlbumGenerator.Generate(root);
        var outDir = Path.Combine(root, "out");
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var tracker = new DuplicateTracker(Path.Combine(root, "saved.json"));
        var save = new SaveService(tracker);
        var devices = await provider.GetDevicesAsync(default);
        await using var switchSession = await provider.ConnectAsync(devices.Single(d => d.IsSwitch), default);
        var scan = await AlbumScanner.ScanAsync(switchSession, null, default);
        if (scan == null)
        {
            return false;
        }

        var r = await save.SaveToPcAsync(scan.AllItems, switchSession, outDir, autoRename: true, null, default);
        if (r.SavedCount != scan.AllItems.Count)
        {
            return false;
        }

        using var server = new IphoneWebServer(outDir, ip: "127.0.0.1", startPort: 53791);
        server.Start();

        using var http = new HttpClient();
        var home = await http.GetAsync(server.Url);
        var homeText = await home.Content.ReadAsStringAsync();
        var homeOk = home.StatusCode == System.Net.HttpStatusCode.OK
            && homeText.Contains("下载全部 ZIP")
            && homeText.Contains("塞尔达传说 王国之泪");

        // 无 token 应拒绝
        var noToken = await http.GetAsync($"http://127.0.0.1:{server.Port}/");
        var noTokenOk = noToken.StatusCode == System.Net.HttpStatusCode.Forbidden;

        var zipUrl = server.Url.Replace("/?t=", "/zip?game=__all__&t=");
        var zipResponse = await http.GetAsync(zipUrl);
        var zipOk = zipResponse.StatusCode == System.Net.HttpStatusCode.OK;
        var entryCount = 0;
        if (zipOk)
        {
            await using var zipStream = await zipResponse.Content.ReadAsStreamAsync();
            using var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read);
            entryCount = archive.Entries.Count;
        }

        // 越界路径应 404
        var badImg = await http.GetAsync($"http://127.0.0.1:{server.Port}/img?path=..%2F..%2FWindows%2Fwin.ini&t={new Uri(server.Url).Query[3..]}");
        var badOk = badImg.StatusCode == System.Net.HttpStatusCode.NotFound;

        return homeOk && noTokenOk && zipOk && entryCount == scan.AllItems.Count && badOk;
    }
    finally { Directory.Delete(root, recursive: true); }
});

// ---------- v1.1: 去重记录与实际文件一致 ----------
const string SwitchName = "2025031118460900-02CB906EA538A35643C1E1484C4B947D.jpg";

Add("去重: Switch 原始文件名与游戏名无关（换语言/改名不重复保存）", () =>
{
    var file = TempFile();
    try
    {
        var t1 = new DuplicateTracker(file);
        t1.MarkSaved("塞尔达传说 王国之泪", SwitchName, "x.jpg", toPc: true, toPhone: false);
        t1.Flush();
        var t2 = new DuplicateTracker(file);
        return t2.GetState("ゼルダの伝説 ティアーズ オブ ザ キングダム", SwitchName) == SavedState.SavedToPc;
    }
    finally { File.Delete(file); }
});

Add("去重: 旧版键（游戏名|文件名）自动迁移", () =>
{
    var file = TempFile();
    try
    {
        File.WriteAllText(file, "{\"游戏A|" + SwitchName + "\":{\"Id\":\"游戏A|" + SwitchName + "\",\"GameTitle\":\"游戏A\",\"FileName\":\"" + SwitchName + "\",\"SavedToPc\":true}}");
        var t = new DuplicateTracker(file);
        return t.GetState("游戏B", SwitchName) == SavedState.SavedToPc && t.Count == 1;
    }
    finally { File.Delete(file); }
});

Add("去重: 电脑上的文件被删除后不再算「已保存」", () =>
{
    var file = TempFile();
    var saved = Path.Combine(Path.GetTempPath(), "sab-test-" + Guid.NewGuid().ToString("N") + ".jpg");
    try
    {
        File.WriteAllBytes(saved, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
        var t = new DuplicateTracker(file);
        var item = Item("游戏A", SwitchName, new DateTime(2025, 3, 11));
        t.MarkSavedToPc(item, saved);
        var before = t.IsOnPc(item) && t.GetState(item) == SavedState.SavedToPc;
        File.Delete(saved);
        var after = !t.IsOnPc(item) && t.GetState(item) == SavedState.NotSaved;
        return before && after;
    }
    finally { File.Delete(file); if (File.Exists(saved)) File.Delete(saved); }
});

Add("去重: 按手机区分，换手机不会被误跳过", () =>
{
    var file = TempFile();
    try
    {
        var t = new DuplicateTracker(file);
        var item = Item("游戏A", SwitchName, new DateTime(2025, 3, 11));
        t.MarkSavedToPhone(item, "phone-1", "20250311_游戏A_001.jpg");
        return t.GetPhoneFileName(item, "phone-1") == "20250311_游戏A_001.jpg"
            && t.GetPhoneFileName(item, "phone-2") == null
            && t.GetState(item) == SavedState.SavedToPhone;
    }
    finally { File.Delete(file); }
});

Add("去重: 清除记录", () =>
{
    var file = TempFile();
    try
    {
        var t = new DuplicateTracker(file);
        t.MarkSaved("游戏A", "a.jpg", null, toPc: true, toPhone: false);
        t.ClearAll();
        return t.Count == 0 && new DuplicateTracker(file).Count == 0;
    }
    finally { File.Delete(file); }
});

// ---------- v1.1: 手机存储卷选择 ----------
Add("手机存储: 有 SD 卡时选内部存储", () =>
    PhoneSaveService.PickStorageRoot(new[] { "SD 卡", "内部共享存储空间" }) == "内部共享存储空间"
    && PhoneSaveService.PickStorageRoot(new[] { "SD card", "Internal shared storage" }) == "Internal shared storage"
    && PhoneSaveService.PickStorageRoot(new[] { "Phone", "Card" }) == "Phone");
Add("手机存储: 只有一个卷时直接用", () =>
    PhoneSaveService.PickStorageRoot(new[] { "内部存储" }) == "内部存储"
    && PhoneSaveService.PickStorageRoot(new[] { "SD 卡" }) == "SD 卡"
    && PhoneSaveService.PickStorageRoot(Array.Empty<string>()) == null);

// ---------- v1.1: 完整性校验 ----------
Add("校验: JPG/PNG/MP4 文件头", () =>
    MediaVerifier.HasValidHeader(".jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE1 })
    && !MediaVerifier.HasValidHeader(".jpg", new byte[] { 0x00, 0x00, 0x00, 0x00 })
    && MediaVerifier.HasValidHeader(".png", new byte[] { 0x89, 0x50, 0x4E, 0x47 })
    && MediaVerifier.HasValidHeader(".mp4", new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p' })
    && !MediaVerifier.HasValidHeader(".mp4", new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }));
Add("校验: 空文件与大小不一致都判失败", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "sab-test-" + Guid.NewGuid().ToString("N") + ".jpg");
    try
    {
        File.WriteAllBytes(path, Array.Empty<byte>());
        var empty = MediaVerifier.Verify(path, 0) != null;
        File.WriteAllBytes(path, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2 });
        var ok = MediaVerifier.Verify(path, 0) == null && MediaVerifier.Verify(path + ".part", 0) != null;
        var sizeMismatch = MediaVerifier.Verify(path, 999) != null;
        return empty && ok && sizeMismatch;
    }
    finally { File.Delete(path); }
});

// ---------- v1.1: HTTP Range ----------
Add("Range: 常见格式", () =>
    IphoneWebServer.TryParseRange("bytes=0-1", 100, out var s1, out var e1) && s1 == 0 && e1 == 1
    && IphoneWebServer.TryParseRange("bytes=10-", 100, out var s2, out var e2) && s2 == 10 && e2 == 99
    && IphoneWebServer.TryParseRange("bytes=-20", 100, out var s3, out var e3) && s3 == 80 && e3 == 99
    && IphoneWebServer.TryParseRange("bytes=50-500", 100, out var s4, out var e4) && s4 == 50 && e4 == 99
    && !IphoneWebServer.TryParseRange("bytes=200-", 100, out _, out _)
    && !IphoneWebServer.TryParseRange("bytes=0-1,5-6", 100, out _, out _));

// ---------- v1.1: 集成 ----------
AddAsync("模拟: 删除电脑上的文件后可以重新保存", async () =>
{
    var root = TempDir();
    try
    {
        DemoAlbumGenerator.Generate(root);
        var outDir = Path.Combine(root, "out");
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var tracker = new DuplicateTracker(Path.Combine(root, "saved.json"));
        var save = new SaveService(tracker);
        var devices = await provider.GetDevicesAsync(default);
        await using var session = await provider.ConnectAsync(devices.Single(d => d.IsSwitch), default);
        var scan = await AlbumScanner.ScanAsync(session, null, default);
        if (scan == null)
        {
            return false;
        }

        var r1 = await save.SaveToPcAsync(scan.AllItems, session, outDir, autoRename: true, null, default);
        Directory.Delete(outDir, recursive: true);   // 用户手动删掉了
        var r2 = await save.SaveToPcAsync(scan.AllItems, session, outDir, autoRename: true, null, default);
        var files = Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Length;
        return r1.SavedCount == scan.AllItems.Count
            && r2.SavedCount == scan.AllItems.Count && r2.SkippedCount == 0
            && files == scan.AllItems.Count;
    }
    finally { Directory.Delete(root, recursive: true); }
});

AddAsync("模拟: Switch 直传手机，第二次跳过，换手机重新传", async () =>
{
    var root = TempDir();
    try
    {
        DemoAlbumGenerator.Generate(root);
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var tracker = new DuplicateTracker(Path.Combine(root, "saved.json"));
        var phoneSave = new PhoneSaveService(tracker, new SettingsService(Path.Combine(root, "settings.json")));
        var devices = await provider.GetDevicesAsync(default);
        await using var switchSession = await provider.ConnectAsync(devices.Single(d => d.IsSwitch), default);
        var phoneDevice = (await provider.GetPhoneCandidateDevicesAsync(default)).Single();
        await using var phoneSession = await provider.ConnectAsync(phoneDevice, default);
        var scan = await AlbumScanner.ScanAsync(switchSession, null, default);
        if (scan == null)
        {
            return false;
        }

        var r1 = await phoneSave.SaveToPhoneAsync(scan.AllItems, switchSession, phoneSession, true, null, default, "phone-A");
        var r2 = await phoneSave.SaveToPhoneAsync(scan.AllItems, switchSession, phoneSession, true, null, default, "phone-A");
        var r3 = await phoneSave.SaveToPhoneAsync(scan.AllItems, switchSession, phoneSession, true, null, default, "phone-B");

        // 电脑上没有留任何副本（直传）
        var pcCopies = Directory.Exists(Path.Combine(root, "out"));
        return r1.SavedCount == scan.AllItems.Count
            && r2.SkippedCount == scan.AllItems.Count && r2.SavedCount == 0
            && r3.SavedCount == scan.AllItems.Count
            && !pcCopies;
    }
    finally { Directory.Delete(root, recursive: true); }
});

AddAsync("iPhone 网页: 分享指定文件 + 缩略图 + Range 206", async () =>
{
    var root = TempDir();
    try
    {
        DemoAlbumGenerator.Generate(root);
        var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
        var devices = await provider.GetDevicesAsync(default);
        await using var switchSession = await provider.ConnectAsync(devices.Single(d => d.IsSwitch), default);
        var scan = await AlbumScanner.ScanAsync(switchSession, null, default);
        if (scan == null)
        {
            return false;
        }

        // Switch 直传：下载到临时目录再分享
        var share = new IphoneShareService(Path.Combine(root, "share"));
        var picked = scan.AllItems.Take(5).ToList();
        var (files, result) = await share.PrepareFromDeviceAsync(picked, switchSession, true, null, default);
        if (files.Count != picked.Count || result.Failed.Count != 0)
        {
            return false;
        }

        using var server = new IphoneWebServer(ip: "127.0.0.1", startPort: 53811);
        server.ShareFiles(files);
        server.Start();
        using var http = new HttpClient();
        var homeText = await http.GetStringAsync(server.Url);
        var token = new Uri(server.Url).Query[3..];

        var ids = System.Text.RegularExpressions.Regex.Matches(homeText, "/f\\?id=([0-9a-f]+)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        if (ids.Count != picked.Count)
        {
            return false;
        }

        var baseUrl = $"http://127.0.0.1:{server.Port}";
        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/f?id={ids[0]}&t={token}");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1);
        var ranged = await http.SendAsync(request);
        var rangedBytes = await ranged.Content.ReadAsByteArrayAsync();
        var rangeOk = ranged.StatusCode == System.Net.HttpStatusCode.PartialContent && rangedBytes.Length == 2;

        var thumbIds = System.Text.RegularExpressions.Regex.Matches(homeText, "/thumb\\?id=([0-9a-f]+)")
            .Select(m => m.Groups[1].Value).ToList();
        var thumbOk = true;
        if (thumbIds.Count > 0)
        {
            var thumb = await http.GetAsync($"{baseUrl}/thumb?id={thumbIds[0]}&t={token}");
            thumbOk = thumb.StatusCode == System.Net.HttpStatusCode.OK
                && thumb.Content.Headers.ContentType?.MediaType == "image/jpeg";
        }

        var unknown = await http.GetAsync($"{baseUrl}/f?id=deadbeefdeadbeef&t={token}");
        share.Cleanup();
        return rangeOk && thumbOk && unknown.StatusCode == System.Net.HttpStatusCode.NotFound
            && !Directory.Exists(Path.Combine(root, "share"));
    }
    finally { Directory.Delete(root, recursive: true); }
});

// ---------- Switch 2 游戏（在线更新 / 内置快照） ----------
Add("NS2: 内置快照可读取且含马力欧卡丁车 世界", () =>
{
    var updater = new OnlineTitleUpdater(Path.Combine(Path.GetTempPath(), "sab-test-" + Guid.NewGuid().ToString("N") + ".json"));
    var local = updater.LoadLocal();
    return local.Count >= 600 && local.Any(e => e.Ja == "マリオカート ワールド" && e.Zht == "瑪利歐賽車 世界" && e.Icon != null);
});

Add("NS2: 合并后中文/日文/英文文件夹名都能找到封面", () =>
{
    var db = TitleDbService.LoadEmbedded()!;
    var before = db.GetTitleIdByFolderName("马力欧卡丁车 世界");
    var updater = new OnlineTitleUpdater(Path.Combine(Path.GetTempPath(), "sab-test-" + Guid.NewGuid().ToString("N") + ".json"));
    var added = db.Merge(OnlineTitleUpdater.ToTitles(updater.LoadLocal()));
    var zhs = db.GetTitleIdByFolderName("马力欧卡丁车 世界");
    var zht = db.GetTitleIdByFolderName("瑪利歐賽車 世界");
    var ja = db.GetTitleIdByFolderName("マリオカート ワールド");
    var en = db.GetTitleIdByFolderName("Mario Kart World");
    var dk = db.GetTitleIdByFolderName("咚奇刚 蕉力全开");
    var splat = db.GetTitleIdByFolderName("斯普拉遁 塗擊隊");
    // Switch 1 游戏不受影响
    var totk = db.GetTitleIdByFolderName("塞尔达传说 王国之泪");
    Console.WriteLine($"  NS2 新增 {added} 款; 卡丁车={zhs}");
    Console.WriteLine($"  合并前 卡丁车 世界 → {before ?? "无"}");
    return added >= 600
        // 内置库里已有的 NS2 游戏（如马力欧卡丁车 世界）优先用内置 TitleId，其余用在线数据
        && zhs != null && zhs == zht && zhs == ja && zhs == en
        && dk != null && db.GetCoverUrls(dk).Count >= 1 && splat != null && splat.StartsWith("ns2-")
        && db.GetCoverUrls(zhs).Count >= 1 && db.GetCoverUrls(splat)[0].StartsWith("https://img-eshop.cdn.nintendo.net/i/")
        && totk == "0100F2C0115B6000";
});

// ---------- 预览大小 ----------
Add("预览大小: 默认窗口下每一档的列数都不同", () =>
{
    var columns = Enumerable.Range(0, PhotoGridLayout.MaxZoomLevel + 1)
        .Select(level => PhotoGridLayout.Compute(1207, level).Columns)
        .ToArray();
    return columns.SequenceEqual(new[] { 6, 5, 4, 3, 2 });
});
Add("预览大小: 卡片铺满一行且不超出", () =>
{
    foreach (var width in new[] { 947.0, 1207, 1847, 2487 })
    {
        for (var level = 0; level <= PhotoGridLayout.MaxZoomLevel; level++)
        {
            var (columns, card) = PhotoGridLayout.Compute(width, level);
            var used = columns * (card + PhotoGridLayout.CardGap);
            if (used > width || used < width - columns)
            {
                return false;
            }
        }
    }

    return true;
});
Add("预览大小: 最大档卡片足够宽（文字冒险的对话看得清）", () =>
    PhotoGridLayout.Compute(1207, PhotoGridLayout.MaxZoomLevel).CardWidth >= 590
    && PhotoGridLayout.Compute(1847, PhotoGridLayout.MaxZoomLevel).CardWidth >= 590);
Add("预览大小: 窗口很窄时至少 2 列，档位越界被夹住", () =>
    PhotoGridLayout.Compute(500, PhotoGridLayout.MaxZoomLevel).Columns == 2
    && PhotoGridLayout.Compute(1207, 99).Columns == 2
    && PhotoGridLayout.Compute(1207, -5).Columns == 6);
Add("预览大小: 图片区是 16:9（截图不裁边）", () =>
    Math.Abs(PhotoGridLayout.ImageHeight(600) / 600 - 9.0 / 16) < 0.002
    && PhotoGridLayout.CardHeight(600) == PhotoGridLayout.ImageHeight(600) + PhotoGridLayout.InfoHeight);
AddAsync("预览大小: 调整后记住档位，重排时告知视野位置", async () =>
{
    var dir = TempDir();
    Directory.CreateDirectory(dir);
    try
    {
        var settings = new SettingsService(Path.Combine(dir, "settings.json"));
        var tracker = new DuplicateTracker(Path.Combine(dir, "saved.json"));
        var main = new SwitchAlbum.ViewModels.MainViewModel(
            settings, new ThemeService(settings), tracker, new SaveService(tracker), new PhoneSaveService(tracker, settings),
            new ThumbnailService(Path.Combine(dir, "thumbs")), new CoverService(Path.Combine(dir, "covers")),
            new ScanCacheService(Path.Combine(dir, "scan")), Task.FromResult<ITitleDb?>(null),
            new MockMediaProvider(Path.Combine(dir, "album"), Path.Combine(dir, "phone")));

        var items = Enumerable.Range(0, 100).Select(i => Item("游戏A", $"{i}.jpg", new DateTime(2025, 1, 1).AddMinutes(i))).ToList();
        var grid = new SwitchAlbum.ViewModels.GameGridViewModel("游戏A", items, main);
        grid.SetViewportWidth(1207);
        var startColumns = grid.Columns;

        int? anchor = null;
        grid.RowsReflowed += index => anchor = index;
        grid.FirstVisibleIndex = 42;
        grid.ChangeZoom(1);
        var ok = grid.ZoomLevel == 2 && grid.Columns == startColumns - 1 && anchor == 42
                 && grid.Rows.Sum(r => r.Items.Count) == 100
                 && grid.Rows[0].Items.Count == grid.Columns;

        grid.ChangeZoom(99);
        ok &= grid.ZoomLevel == PhotoGridLayout.MaxZoomLevel;

        var reopened = new SwitchAlbum.ViewModels.GameGridViewModel("游戏A", items, main);
        var fromDisk = new SettingsService(Path.Combine(dir, "settings.json")).Current.PhotoZoomLevel;
        await Task.CompletedTask;
        return ok && reopened.ZoomLevel == PhotoGridLayout.MaxZoomLevel && fromDisk == PhotoGridLayout.MaxZoomLevel;
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
});

Add("设置: 第一次运行（窗口位置还是 NaN）也能保存", () =>
{
    var file = TempFile();
    try
    {
        var settings = new SettingsService(file);
        settings.Current.PhotoZoomLevel = 3;
        settings.Current.SavePath = "D:\\照片";
        settings.Save();
        var loaded = new SettingsService(file).Current;
        return loaded.PhotoZoomLevel == 3 && loaded.SavePath == "D:\\照片" && double.IsNaN(loaded.WindowLeft);
    }
    finally { File.Delete(file); }
});

// ---------- 封面匹配：宁可无封面，也不要别的游戏的封面 ----------
static TitleDbService MatchingDb()
{
    var json = """
    {
      "0100CCCC00001000": { "en": "RITE" },
      "0100CCCC00002000": { "en": "POOL" },
      "0100CCCC00003000": { "en": "FINAL FANTASY VII" },
      "0100CCCC00004000": { "en": "Wizard of Legend" },
      "0100CCCC00005000": { "en": "Arcade Archives Scramble" },
      "0100CCCC00006001": { "zht": "研究與毀滅之命運石之門角色扮演服裝" },
      "0100CCCC00007000": { "en": "Hollow Knight" },
      "0100CCCC00008000": { "en": "Hollow Knight Silksong" },
      "0100CCCC00009000": { "en": "Dragon Quest Builders" },
      "0100CCCC0000A000": { "en": "Dragon Quest Treasures" },
      "0100CCCC0000B000": { "en": "Xenoblade2 (ゼノブレイド2) Nintendo Switch 2 Edition" },
      "0100CCCC0000C000": { "en": "Dungeon Antiqua 2" }
    }
    """;
    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
    return new TitleDbService(stream);
}

Add("封面匹配: 名称中间碰巧相同不算（Rewrite≠RITE）", () =>
{
    var db = MatchingDb();
    return db.GetTitleIdByFolderName("Rewrite") == null
        && db.GetTitleIdByFolderName("SWEET POOL") == null;
});
Add("封面匹配: DLC 不参与匹配", () => MatchingDb().GetTitleIdByFolderName("命运石之门") == null);
Add("封面匹配: 不断在单词中间（VIII≠VII，SCRAMBLED≠Scramble）", () =>
{
    var db = MatchingDb();
    return db.GetTitleIdByFolderName("FINAL FANTASY VIII Remastered") == null
        && db.GetTitleIdByFolderName("Arcade Archives SCRAMBLED EGG") == null;
});
Add("封面匹配: 续作不算同一款（II≠本体，本体≠2）", () =>
{
    var db = MatchingDb();
    return db.GetTitleIdByFolderName("Wizard of Legend II") == null
        && db.GetTitleIdByFolderName("Wizard of Legend 2") == null
        && db.GetTitleIdByFolderName("Dungeon Antiqua") == null;
});
Add("封面匹配: 文件夹是库名的开头：同一款取本体，不同游戏不猜", () =>
{
    var db = MatchingDb();
    return db.GetTitleIdByFolderName("Hollow Knigh") == "0100CCCC00007000"
        && db.GetTitleIdByFolderName("Dragon Quest") == null;
});
Add("封面匹配: 复合名称括号内外都能命中", () =>
{
    var db = MatchingDb();
    return db.GetTitleIdByFolderName("ゼノブレイド２") == "0100CCCC0000B000"
        && db.GetTitleIdByFolderName("Xenoblade2") == "0100CCCC0000B000";
});
Add("封面匹配: 真实库里文字冒险的典型误判已消失", () =>
{
    var db = TitleDbService.LoadEmbedded()!;
    return new[] { "Rewrite", "SWEET POOL", "ATRI", "Summer Pockets", "命运石之门" }
        .All(name => db.GetTitleIdByFolderName(name) == null)
        && db.GetTitleIdByFolderName("CLANNAD") == "0100A3A00CC7E000";
});

// ---------- 自定义封面 ----------
static string WriteImage(string dir, string name, int width = 64, int height = 64)
{
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, name);
    using var bitmap = new System.Drawing.Bitmap(width, height);
    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    return path;
}

AddAsync("自定义封面: 优先于默认封面，恢复后回到默认", async () =>
{
    var dir = TempDir();
    try
    {
        var covers = new CoverService(Path.Combine(dir, "covers"), Path.Combine(dir, "overrides"));
        const string tid = "0100F2C0115B6000";
        var builtIn = await covers.ResolveAsync(tid, "塞尔达传说 王国之泪", null, default);
        var custom = await covers.SetOverrideAsync("塞尔达传说 王国之泪", WriteImage(dir, "mine.png"));
        var resolved = await covers.ResolveAsync(tid, "塞尔达传说 王国之泪", null, default);
        var cleared = covers.ClearOverride("塞尔达传说 王国之泪");
        var after = await covers.ResolveAsync(tid, "塞尔达传说 王国之泪", null, default);
        return builtIn != null && resolved == custom && cleared && after == builtIn
            && covers.GetOverride("塞尔达传说 王国之泪") == null;
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
});
AddAsync("自定义封面: Switch 相册和电脑文件夹名共用一张", async () =>
{
    var dir = TempDir();
    try
    {
        var covers = new CoverService(Path.Combine(dir, "covers"), Path.Combine(dir, "overrides"));
        var path = await covers.SetOverrideAsync("Mario: The Game?", WriteImage(dir, "mine.png"));
        return covers.GetOverride(FileNameSanitizer.Sanitize("Mario: The Game?")) == path;
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
});
AddAsync("自定义封面: 清除缓存不会删掉", async () =>
{
    var dir = TempDir();
    try
    {
        var cache = Path.Combine(dir, "covers");
        var covers = new CoverService(cache);
        var path = await covers.SetOverrideAsync("游戏A", WriteImage(dir, "mine.png"));
        Directory.Delete(cache, recursive: true);
        return File.Exists(path) && await covers.ResolveAsync(null, "游戏A", null, default) == path;
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
});
AddAsync("自定义封面: 再换一张只留最新的，文件名变化", async () =>
{
    var dir = TempDir();
    try
    {
        var overrides = Path.Combine(dir, "overrides");
        var covers = new CoverService(Path.Combine(dir, "covers"), overrides);
        var first = await covers.SetOverrideAsync("游戏A", WriteImage(dir, "a.png"));
        await Task.Delay(5);
        var second = await covers.SetOverrideAsync("游戏A", WriteImage(dir, "b.png"));
        return first != second && covers.GetOverride("游戏A") == second
            && Directory.GetFiles(overrides).Length == 1;
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
});
AddAsync("自定义封面: 大图缩到 512 以内", async () =>
{
    var dir = TempDir();
    try
    {
        var covers = new CoverService(Path.Combine(dir, "covers"), Path.Combine(dir, "overrides"));
        var path = await covers.SetOverrideAsync("游戏A", WriteImage(dir, "big.png", 1600, 900));
        using var image = System.Drawing.Image.FromFile(path);
        return image.Width == 512 && image.Height == 288;
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
});
AddAsync("自定义封面: 打不开的文件报错，不留垃圾，不影响旧封面", async () =>
{
    var dir = TempDir();
    try
    {
        var overrides = Path.Combine(dir, "overrides");
        var covers = new CoverService(Path.Combine(dir, "covers"), overrides);
        var good = await covers.SetOverrideAsync("游戏A", WriteImage(dir, "good.png"));
        var bad = Path.Combine(dir, "bad.png");
        File.WriteAllText(bad, "这不是图片");
        try
        {
            await covers.SetOverrideAsync("游戏A", bad);
            return false;
        }
        catch (Exception)
        {
            return covers.GetOverride("游戏A") == good && Directory.GetFiles(overrides).Length == 1;
        }
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
});

// ---------- 原图缓存 ----------
static async Task<(string Root, MockMediaProvider Provider, string Path)> PhotoOnMockSwitch()
{
    var root = TempDir();
    var game = Path.Combine(root, "album", "Album", "游戏A");
    WriteImage(game, "2025031118460900-ABC.jpg", 320, 180);
    var provider = new MockMediaProvider(Path.Combine(root, "album"), Path.Combine(root, "phone"));
    await Task.CompletedTask;
    return (root, provider, string.Join('\\', "", "Album", "游戏A", "2025031118460900-ABC.jpg"));
}

AddAsync("原图缓存: 清除缓存后不用重启也能继续加载", async () =>
{
    var (root, provider, path) = await PhotoOnMockSwitch();
    try
    {
        var device = (await provider.GetDevicesAsync(default)).First(d => d.IsSwitch);
        await using var session = await provider.ConnectAsync(device, default);
        var cache = Path.Combine(root, "thumbs");
        var thumbs = new ThumbnailService(cache);
        Directory.Delete(cache, recursive: true);
        var thumb = await thumbs.GetThumbnailAsync("d", session, path, "a.jpg", null, 0, default);
        var full = await thumbs.GetFullImageAsync("d", session, path, "a.jpg", null, 0, default);

        var covers = Path.Combine(root, "covers");
        var coverService = new CoverService(covers);
        Directory.Delete(covers, recursive: true);
        var cover = await coverService.ResolveAsync("0100F2C0115B6000", "塞尔达", null, default);
        return thumb != null && full != null && cover != null;
    }
    finally { try { Directory.Delete(root, recursive: true); } catch { } }
});
AddAsync("原图缓存: 灯箱和网格同时要同一张图不会互相打架", async () =>
{
    var (root, provider, path) = await PhotoOnMockSwitch();
    try
    {
        var device = (await provider.GetDevicesAsync(default)).First(d => d.IsSwitch);
        await using var session = await provider.ConnectAsync(device, default);
        var cache = Path.Combine(root, "thumbs");
        var thumbs = new ThumbnailService(cache);
        var results = await Task.WhenAll(
            thumbs.GetFullImageAsync("d", session, path, "a.jpg", null, 0, default),
            thumbs.GetFullImageAsync("d", session, path, "a.jpg", null, 0, default, background: true),
            thumbs.GetFullImageAsync("d", session, path, "a.jpg", null, 0, default),
            thumbs.GetFullImageAsync("d", session, path, "a.jpg", null, 0, default, background: true));
        return results.All(r => r != null) && results.Distinct().Count() == 1
            && Directory.GetFiles(cache).Length == 1;
    }
    finally { try { Directory.Delete(root, recursive: true); } catch { } }
});
AddAsync("原图缓存: 取消时抛出取消异常，不留临时文件", async () =>
{
    var (root, provider, path) = await PhotoOnMockSwitch();
    try
    {
        var device = (await provider.GetDevicesAsync(default)).First(d => d.IsSwitch);
        await using var session = await provider.ConnectAsync(device, default);
        var cache = Path.Combine(root, "thumbs");
        var thumbs = new ThumbnailService(cache);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try
        {
            await thumbs.GetFullImageAsync("d", session, path, "a.jpg", null, 0, cts.Token, background: true);
            return false;
        }
        catch (OperationCanceledException)
        {
            return Directory.GetFiles(cache).Length == 0;
        }
    }
    finally { try { Directory.Delete(root, recursive: true); } catch { } }
});

// ---------- 联网诊断（只打印结果，不影响通过与否） ----------
AddAsync("诊断: 任天堂封面 CDN 与 Switch 2 在线列表是否可访问", async () =>
{
    var dir = TempDir();
    try
    {
        var covers = new CoverService(Path.Combine(dir, "covers"));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cover = await covers.ResolveAsync("0400C3F00006E000", "马力欧卡丁车 世界",
            new[] { "https://img-eshop.cdn.nintendo.net/i/f881a2ef02b5e87cd52bdbc9713ac2b72c78dba9a6a050ba78389b846993ab2a.jpg" }, default);
        Console.WriteLine($"  封面 CDN: {(cover != null ? "可访问" : "失败")}（{sw.ElapsedMilliseconds} ms）");

        sw.Restart();
        var updater = new OnlineTitleUpdater(Path.Combine(dir, "ns2.json"));
        var list = await updater.RefreshAsync(Array.Empty<Ns2Entry>(), default);
        Console.WriteLine($"  在线列表: {(list != null ? list.Count + " 款" : "失败")}（{sw.ElapsedMilliseconds} ms）");
        var withZh = list?.Count(e => e.Zht != null) ?? 0;
        Console.WriteLine($"  其中取到繁中名: {withZh} 款");
        return true;
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
});

// ---------- 运行 ----------
var passed = 0;
foreach (var (name, run) in tests)
{
    try
    {
        var ok = await run();
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
        if (ok)
        {
            passed++;
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"FAIL  {name}  （异常: {e.Message}）");
    }
}

Console.WriteLine();
Console.WriteLine($"{passed}/{tests.Count} 通过");
Environment.Exit(passed == tests.Count ? 0 : 1);

static string TempDir() => Path.Combine(Path.GetTempPath(), "sab-test-" + Guid.NewGuid().ToString("N"));
static string TempFile() => Path.Combine(Path.GetTempPath(), "sab-test-" + Guid.NewGuid().ToString("N") + ".json");
