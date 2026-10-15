using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwitchAlbum.Services;

/// <summary>
/// Switch 2 游戏名与封面的「网上更新」。
/// <para>
/// 内置游戏库来自 Switch 1 eShop 数据，没有 NS2 独占游戏，所以这些游戏一直显示「无封面」。
/// 这里从任天堂官方公开数据补齐：
/// </para>
/// <list type="number">
/// <item>任天堂日本官方软件检索（search.nintendo.jp）：全部 Switch 2 软件的日文名 + 图标（含刚发售的新游戏）；</item>
/// <item>任天堂香港官网游戏页：繁体中文名（简体名由游戏库自动繁转简生成）。</item>
/// </list>
/// <para>
/// 离线可用：程序内置一份快照（Resources/ns2-titles.json）；联网更新的结果缓存在
/// %LocalAppData%\SwitchAlbum\ns2-titles.json，每天最多联网一次；封面图片下载后由 CoverService 缓存，
/// 之后断网也能显示。
/// </para>
/// </summary>
public sealed class OnlineTitleUpdater
{
    private const string JpSearchUrl = "https://search.nintendo.jp/nintendo_soft/search.json?opt_hard=05_BEE&limit=300&page={0}";
    private const string HkGamePageUrl = "https://www.nintendo.com/hk/games/switch2/{0}/index.html";
    private const string CoverUrl = "https://img-eshop.cdn.nintendo.net/i/{0}.jpg";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);
    private static readonly Regex HtmlTitle = new(@"<title>\s*([^<|]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _cachePath;
    private readonly HttpClient _http;

    public OnlineTitleUpdater(string? cachePath = null, HttpClient? http = null)
    {
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwitchAlbum", "ns2-titles.json");
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        if (http == null)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SwitchAlbum/1.1");
        }
    }

    /// <summary>本地已有的数据（缓存优先，没有则用内置快照）。离线也能立即使用。</summary>
    public IReadOnlyList<Ns2Entry> LoadLocal()
    {
        var cached = ReadFile(_cachePath);
        var embedded = ReadEmbedded();
        return MergeEntries(embedded, cached);
    }

    public bool NeedsRefresh()
    {
        try
        {
            return !File.Exists(_cachePath) || DateTime.Now - File.GetLastWriteTime(_cachePath) > RefreshInterval;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// 联网拉取最新列表并写入缓存。失败（没网、被墙、接口变化）时返回 null，继续用本地数据。
    /// </summary>
    public async Task<IReadOnlyList<Ns2Entry>?> RefreshAsync(IReadOnlyList<Ns2Entry> known, CancellationToken ct)
    {
        List<Ns2Entry> fetched;
        try
        {
            fetched = await FetchJpListAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log.Info("Switch 2 游戏列表联网更新失败（继续使用本地数据）: " + ex.Message);
            return null;
        }

        if (fetched.Count == 0)
        {
            return null;
        }

        // 已知的中文名沿用；新游戏去香港官网补繁体中文名（第三方游戏通常没有页面，跳过即可）
        var knownByNsuid = known.ToDictionary(k => k.Nsuid, StringComparer.Ordinal);
        var lookups = 0;
        foreach (var entry in fetched)
        {
            if (knownByNsuid.TryGetValue(entry.Nsuid, out var old))
            {
                entry.Zht ??= old.Zht;
                entry.Extra = old.Extra;
            }
            else if (entry.Icode.Length > 0 && lookups < 40)
            {
                lookups++;
                entry.Zht = await TryFetchHkNameAsync(entry.Icode, ct).ConfigureAwait(false);
            }
        }

        var merged = MergeEntries(known, fetched);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            var tmp = _cachePath + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(merged, JsonOptions), ct).ConfigureAwait(false);
            File.Move(tmp, _cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Info("Switch 2 游戏列表缓存写入失败: " + ex.Message);
        }

        Log.Info($"Switch 2 游戏列表已更新：{merged.Count} 款（新增 {fetched.Count(f => !knownByNsuid.ContainsKey(f.Nsuid))} 款）");
        return merged;
    }

    /// <summary>转换为游戏库可合并的格式。</summary>
    public static IEnumerable<OnlineTitle> ToTitles(IEnumerable<Ns2Entry> entries)
        => entries.Select(e => new OnlineTitle(
            "ns2-" + e.Nsuid,
            new[] { e.Zht, e.Ja }.Concat(e.Extra ?? new List<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().ToList(),
            string.IsNullOrEmpty(e.Icon) ? Array.Empty<string>() : new[] { string.Format(CoverUrl, e.Icon) }));

    private async Task<List<Ns2Entry>> FetchJpListAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, Ns2Entry>(StringComparer.Ordinal);
        for (var page = 1; page <= 15; page++)
        {
            using var response = await _http.GetAsync(string.Format(JpSearchUrl, page), ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("result", out var resultEl)
                || !resultEl.TryGetProperty("items", out var items)
                || items.GetArrayLength() == 0)
            {
                break;
            }

            foreach (var item in items.EnumerateArray())
            {
                var nsuid = (Str(item, "nsuid") ?? Str(item, "id") ?? "").Split('-')[0];
                var icon = Str(item, "iurl");
                var title = Str(item, "title");

                // 7001… = 游戏本体；7005… = DLC，跳过
                if (!nsuid.StartsWith("7001", StringComparison.Ordinal) || string.IsNullOrEmpty(icon)
                    || string.IsNullOrEmpty(title) || result.ContainsKey(nsuid))
                {
                    continue;
                }

                result[nsuid] = new Ns2Entry
                {
                    Nsuid = nsuid,
                    Ja = title.Replace("（パッケージ版）", "").Replace("(パッケージ版)", "").Trim(),
                    Icon = icon,
                    Icode = (Str(item, "icode") ?? "").ToLowerInvariant(),
                };
            }
        }

        return result.Values.ToList();
    }

    private async Task<string?> TryFetchHkNameAsync(string icode, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(string.Format(HkGamePageUrl, icode), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var match = HtmlTitle.Match(html);
            return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<Ns2Entry> MergeEntries(IEnumerable<Ns2Entry> baseline, IEnumerable<Ns2Entry> newer)
    {
        var map = new Dictionary<string, Ns2Entry>(StringComparer.Ordinal);
        foreach (var e in baseline)
        {
            map[e.Nsuid] = e;
        }

        foreach (var e in newer)
        {
            if (map.TryGetValue(e.Nsuid, out var old))
            {
                e.Zht ??= old.Zht;
                e.Extra ??= old.Extra;
            }

            map[e.Nsuid] = e;
        }

        return map.Values.ToList();
    }

    private static List<Ns2Entry> ReadFile(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<Ns2Entry>>(File.ReadAllText(path), JsonOptions) ?? new List<Ns2Entry>()
                : new List<Ns2Entry>();
        }
        catch (Exception ex)
        {
            Log.Info("Switch 2 游戏列表缓存损坏，已忽略: " + ex.Message);
            return new List<Ns2Entry>();
        }
    }

    private static List<Ns2Entry> ReadEmbedded()
    {
        try
        {
            using var stream = typeof(OnlineTitleUpdater).Assembly.GetManifestResourceStream("SwitchAlbum.Resources.ns2-titles.json");
            return stream == null
                ? new List<Ns2Entry>()
                : JsonSerializer.Deserialize<List<Ns2Entry>>(stream, JsonOptions) ?? new List<Ns2Entry>();
        }
        catch (Exception ex)
        {
            Log.Error("内置 Switch 2 游戏列表读取失败", ex);
            return new List<Ns2Entry>();
        }
    }

    private static string? Str(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>一款 Switch 2 游戏（以任天堂日本 eShop 的 nsuid 为键）。</summary>
public sealed class Ns2Entry
{
    public string Nsuid { get; set; } = "";
    public string? Ja { get; set; }
    public string? Zht { get; set; }

    /// <summary>补充名称（英文名、简中官方名等手工补充，可选）。</summary>
    public List<string>? Extra { get; set; }

    /// <summary>任天堂图片 CDN 的图片哈希（img-eshop.cdn.nintendo.net/i/{icon}.jpg）。</summary>
    public string? Icon { get; set; }

    public string Icode { get; set; } = "";
}
