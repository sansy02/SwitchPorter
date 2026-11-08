using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwitchAlbum.Services;

/// <summary>
/// 游戏名 ↔ TitleId 映射库，加载打包的 titles.json：
/// { "TID": { "zh": "...", "zht": "...", "en": "...", "icon": "https://..." } }。
/// 名称注册时同时登记繁→简（OpenCC TSCharacters）变体，兼容简体系统主机的文件夹名。
/// </summary>
public sealed class TitleDbService : ITitleDb
{
    /// <summary>
    /// 机械繁→简与官方简体名不一致的品牌名例外（简体 → 官方简体）。
    /// 例如 薩爾達傳說 机械转换为「萨尔达传说」，但官方简中为「塞尔达传说」。
    /// </summary>
    private static readonly (string From, string To)[] Aliases =
    {
        ("萨尔达", "塞尔达"),
        ("玛利欧", "马力欧"),
        ("玛莉欧", "马力欧"),
        ("瓦利欧", "瓦力欧"),
        ("马力欧赛车", "马力欧卡丁车"),
        ("玛利欧赛车", "马力欧卡丁车"),
        ("大金刚", "咚奇刚"),
        ("路易吉洋楼", "路易吉洋馆"),
        ("霍格华兹", "霍格沃茨"),
        ("霍格沃茨的传承", "霍格沃茨之遗"),
        ("霍格華茲", "霍格沃茨"),
    };

    /// <summary>文件夹名常见后缀（已规范化、按长度降序），反查时逐个剥离。</summary>
    private static readonly string[] FolderNameSuffixes =
    {
        "nintendoswitch2edition",
        "nintendoswitchedition",
        "switch2edition",
        "nintendoswitch2",
        "definitiveedition",
        "ultimateedition",
        "completeedition",
        "specialedition",
        "deluxeedition",
        "goldedition",
        "体验版",
        "體驗版",
        "体験版",
        "試玩版",
        "试玩版",
        "特別版",
        "特别版",
        "demo",
    };

    private static readonly Regex CompositeName = new(
        @"^(?<outer>[^(（\[]+)[(（\[](?<inner>[^)）\]]+)[)）\]]", RegexOptions.Compiled);

    private readonly Dictionary<string, TitleNames> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byName = new(StringComparer.Ordinal);
    private (string Name, string Tid)[] _byNameEntries;
    private readonly object _gate = new();
    private readonly Dictionary<string, string[]> _coverUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<char, string> _t2sChar = new();
    private readonly List<(string Key, string Value)> _t2sMulti = new();

    public static TitleDbService? LoadEmbedded()
    {
        var assembly = typeof(TitleDbService).Assembly;
        var jsonStream = assembly.GetManifestResourceStream("SwitchAlbum.Resources.titles.json");
        if (jsonStream == null)
        {
            return null;
        }

        var t2sStream = assembly.GetManifestResourceStream("SwitchAlbum.Resources.TSCharacters.txt");
        using (jsonStream)
        using (t2sStream)
        {
            return new TitleDbService(jsonStream, t2sStream);
        }
    }

    public TitleDbService(Stream json, Stream? t2s = null)
    {
        LoadT2S(t2s);

        using var doc = JsonDocument.Parse(json);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            var tid = property.Name;
            var value = property.Value;
            var names = new TitleNames(
                GetString(value, "zh"), GetString(value, "zht"), GetString(value, "en"), GetString(value, "ja"));
            if (names.Best == null)
            {
                continue;
            }

            _byId[tid] = names;

            var urls = new[] { GetString(value, "icon"), GetString(value, "banner"), GetString(value, "box") }
                .Where(u => !string.IsNullOrEmpty(u))
                .Cast<string>()
                .ToArray();
            if (urls.Length > 0)
            {
                _coverUrls[tid] = urls;
            }

            // titledb 里约 4 成是 DLC / 主题包。相册文件夹只会是游戏本体，DLC 名称进表只会抢走匹配
            if (IsBaseTitle(tid))
            {
                RegisterNameVariants(names.Zh, tid);
                RegisterNameVariants(names.ZhHant, tid);
                RegisterNameVariants(names.En, tid);
                RegisterNameVariants(names.Ja, tid);
            }
        }

        // 前缀匹配按名称长度降序尝试：最长命中最具体（如「马力欧卡丁车8 豪华版」优先于「马力欧卡丁车8」）
        _byNameEntries = _byName
            .Select(kv => (Name: kv.Key, Tid: kv.Value))
            .OrderByDescending(e => e.Name.Length)
            .ToArray();
    }

    /// <summary>16 位 TitleId 中游戏本体以 000 结尾（DLC 为 001 起，更新包为 800）；在线数据的 "ns2-…" 都是本体。</summary>
    private static bool IsBaseTitle(string titleId)
        => titleId.Length != 16 || titleId.EndsWith("000", StringComparison.Ordinal);

    public string? GetNameByTitleId(string titleId)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(titleId, out var names) ? names.Best : null;
        }
    }

    public string? GetTitleIdByName(string titleName)
    {
        lock (_gate)
        {
            return _byName.TryGetValue(Normalize(titleName), out var tid) ? tid : null;
        }
    }

    /// <summary>
    /// 合并在线更新的游戏（目前用于 Switch 2：内置库来自 Switch 1 eShop，没有 NS2 独占游戏）。
    /// 已有的名称不覆盖（内置库优先），新名称注册并重建包含匹配表。线程安全，可在扫描进行中调用。
    /// </summary>
    /// <returns>新增的游戏数量。</returns>
    public int Merge(IEnumerable<OnlineTitle> titles)
    {
        var added = 0;
        lock (_gate)
        {
            foreach (var title in titles)
            {
                var names = title.Names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
                if (names.Count == 0 || string.IsNullOrEmpty(title.Id))
                {
                    continue;
                }

                if (!_byId.ContainsKey(title.Id))
                {
                    added++;
                }

                // 名称优先级：中文（简/繁）→ 英文 → 日文，用于显示旧式文件夹名
                var zh = names.FirstOrDefault(ContainsCjkHan) ?? names[0];
                _byId[title.Id] = new TitleNames(null, zh, names.FirstOrDefault(n => !ContainsCjk(n)), names.FirstOrDefault(ContainsKana));

                if (title.CoverUrls.Count > 0)
                {
                    _coverUrls[title.Id] = title.CoverUrls.ToArray();
                }

                foreach (var name in names)
                {
                    RegisterNameVariants(name, title.Id);
                }
            }

            _byNameEntries = _byName
                .Select(kv => (Name: kv.Key, Tid: kv.Value))
                .OrderByDescending(e => e.Name.Length)
                .ToArray();
        }

        return added;
    }

    private static bool ContainsKana(string s) => s.Any(c => c is >= '\u3040' and <= '\u30FF');

    private static bool ContainsCjk(string s) => s.Any(c => c is >= '\u3040' and <= '\u9FFF');

    private static bool ContainsCjkHan(string s) => s.Any(c => c is >= '\u4E00' and <= '\u9FFF') && !ContainsKana(s);

    public string? GetTitleIdByFolderName(string folderName)
    {
        lock (_gate)
        {
            return GetTitleIdByFolderNameCore(folderName);
        }
    }

    private string? GetTitleIdByFolderNameCore(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        // 1. 规范化精确匹配（含繁体文件夹经繁→简后精确匹配）
        foreach (var candidate in NormalizeCandidates(folderName))
        {
            if (_byName.TryGetValue(candidate, out var tid))
            {
                return tid;
            }
        }

        // 2. 剥离常见后缀后精确匹配（在规范化串上剥离，规范化是幂等的）
        var nameNorm = Normalize(folderName);
        foreach (var suffix in FolderNameSuffixes)
        {
            if (!nameNorm.EndsWith(suffix, StringComparison.Ordinal) || nameNorm.Length <= suffix.Length)
            {
                continue;
            }

            var baseNorm = nameNorm[..(nameNorm.Length - suffix.Length)];
            foreach (var candidate in NormalizeCandidates(baseNorm))
            {
                if (_byName.TryGetValue(candidate, out var tid))
                {
                    return tid;
                }
            }
        }

        // 3. 剥离尾部括号注释（如「Splatoon 3（斯普拉遁 3）」）后精确匹配
        var withoutParen = StripTrailingParenthetical(folderName);
        if (withoutParen != folderName)
        {
            foreach (var candidate in NormalizeCandidates(withoutParen))
            {
                if (_byName.TryGetValue(candidate, out var tid))
                {
                    return tid;
                }
            }
        }

        // 4. 前缀匹配。宁可显示「无封面」让用户自己换，也不要显示别的游戏的封面，所以只认开头相同的名称，
        //    中间或结尾碰巧相同的不算（否则 Rewrite 会命中 RITE、SWEET POOL 会命中 POOL）。
        var folderNorm = Normalize(folderName);
        if (!IsSpecificName(folderNorm))
        {
            return null;
        }

        // 方向 A：文件夹 = 库名 + 额外内容（版本后缀/捆绑），最长的库名最具体
        foreach (var (name, tid) in _byNameEntries)
        {
            if (name.Length < folderNorm.Length
                && folderNorm.StartsWith(name, StringComparison.Ordinal)
                && IsSpecificName(name)
                && IsSameGameTail(folderName, name.Length))
            {
                return tid;
            }
        }

        // 方向 B：库名 = 文件夹 + 额外内容，取最短的。其余候选都得是它的延伸（本体、Demo、豪华版这类同一款游戏）；
        // 否则是几款不同的游戏碰巧开头相同，无法判断，不猜
        var candidates = _byNameEntries
            .Where(e => e.Name.Length > folderNorm.Length
                        && e.Name.StartsWith(folderNorm, StringComparison.Ordinal)
                        && !char.IsDigit(e.Name[folderNorm.Length]))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var shortest = candidates[^1]; // 表按长度降序
        return candidates.All(c => c.Name.StartsWith(shortest.Name, StringComparison.Ordinal)) ? shortest.Tid : null;
    }

    /// <summary>名称足够长才参与前缀匹配：CJK 至少 4 字、拉丁字母至少 8 个（Summer、Tetris 这类词太泛）。</summary>
    private static bool IsSpecificName(string normalized)
        => normalized.Sum(c => c is >= '\u3040' and <= '\u9FFF' ? 2 : 1) >= 8;

    /// <summary>
    /// 文件夹名去掉匹配到的前缀后，剩下的部分是否仍是同一款游戏：
    /// 不能断在单词中间（SCRAMBLED ≠ Scramble，VIII ≠ VII），后面也不能紧跟续作编号（Wizard of Legend II ≠ Wizard of Legend）。
    /// </summary>
    private static bool IsSameGameTail(string folderName, int normalizedLength)
    {
        var index = 0;
        for (var kept = 0; index < folderName.Length && kept < normalizedLength; index++)
        {
            if (Normalize(folderName[index].ToString()).Length > 0)
            {
                kept++;
            }
        }

        if (index >= folderName.Length)
        {
            return true;
        }

        if (index > 0 && char.IsAsciiLetterOrDigit(folderName[index]) && char.IsAsciiLetterOrDigit(folderName[index - 1]))
        {
            return false;
        }

        var nextWord = new string(folderName[index..]
            .SkipWhile(c => !char.IsLetterOrDigit(c))
            .TakeWhile(char.IsLetterOrDigit)
            .ToArray());
        return nextWord.Length == 0 || !(nextWord.All(char.IsDigit) || nextWord.All("IVXivx".Contains));
    }

    private IEnumerable<string> NormalizeCandidates(string name)
    {
        yield return Normalize(name);

        var simplified = Normalize(ToSimplified(name));
        if (simplified.Length > 0)
        {
            yield return simplified;
        }

        var aliased = Normalize(ApplyAliases(ToSimplified(name)));
        if (aliased.Length > 0)
        {
            yield return aliased;
        }
    }

    private static string StripTrailingParenthetical(string name)
    {
        var trimmed = name.Trim();
        var pairs = new[] { ('（', '）'), ('(', ')'), ('[', ']') };
        foreach (var (open, close) in pairs)
        {
            var closeIndex = trimmed.LastIndexOf(close);
            if (closeIndex == trimmed.Length - 1)
            {
                var openIndex = trimmed.LastIndexOf(open);
                if (openIndex >= 0)
                {
                    return trimmed[..openIndex].Trim();
                }
            }
        }

        return trimmed;
    }

    /// <summary>TitleId → 封面候选图直链（按 图标→横幅→盒装 顺序），无则空数组。</summary>
    public IReadOnlyList<string> GetCoverUrls(string titleId)
    {
        lock (_gate)
        {
            return _coverUrls.TryGetValue(titleId, out var urls) ? urls : Array.Empty<string>();
        }
    }

    public string? GetIconUrl(string titleId)
        => GetCoverUrls(titleId).FirstOrDefault();

    /// <summary>
    /// 规范化游戏名：小写、全角转半角、剔除空白与标点符号。
    /// 例如 "集合啦！动物森友会" → "集合啦动物森友会"。
    /// </summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "";
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            var ch = c switch
            {
                '　' => ' ',
                >= '！' and <= '～' => (char)(c - 0xFEE0),
                _ => c,
            };

            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch) || char.IsSeparator(ch))
            {
                continue;
            }

            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 注册名称及其括号内外的部分。库里不少名称是复合写法，如「Xenoblade2 (ゼノブレイド2) Nintendo Switch 2 Edition」、
    /// 「Splatoon Raiders (斯普拉遁 塗擊隊)」，相册文件夹只会是其中一种语言。
    /// </summary>
    private void RegisterNameVariants(string? name, string tid)
    {
        RegisterName(name, tid);
        var match = name == null ? Match.Empty : CompositeName.Match(name);
        if (!match.Success)
        {
            return;
        }

        foreach (var part in new[] { match.Groups["outer"].Value, match.Groups["inner"].Value })
        {
            if (IsSpecificName(Normalize(part)))
            {
                RegisterName(part, tid);
            }
        }
    }

    private void RegisterName(string? name, string tid)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _byName.TryAdd(Normalize(name), tid);

        var simplified = ToSimplified(name);
        if (!string.Equals(simplified, name, StringComparison.Ordinal))
        {
            _byName.TryAdd(Normalize(simplified), tid);
        }

        var aliased = ApplyAliases(simplified);
        if (!string.Equals(aliased, simplified, StringComparison.Ordinal))
        {
            _byName.TryAdd(Normalize(aliased), tid);
        }
    }

    private static string ApplyAliases(string name)
    {
        var result = name;
        foreach (var (from, to) in Aliases)
        {
            if (result.Contains(from, StringComparison.Ordinal))
            {
                result = result.Replace(from, to);
            }
        }

        return result;
    }

    private string ToSimplified(string name)
    {
        if (_t2sChar.Count == 0 && _t2sMulti.Count == 0)
        {
            return name;
        }

        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(_t2sChar.TryGetValue(c, out var simplified) ? simplified : c.ToString());
        }

        var result = sb.ToString();
        foreach (var (key, value) in _t2sMulti)
        {
            result = result.Replace(key, value);
        }

        return result;
    }

    private void LoadT2S(Stream? stream)
    {
        if (stream == null)
        {
            return;
        }

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var tab = line.IndexOf('\t');
            if (tab < 0)
            {
                continue;
            }

            var key = line[..tab];
            var values = line[(tab + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (values.Length == 0)
            {
                continue;
            }

            if (key.Length == 1)
            {
                _t2sChar[key[0]] = values[0];
            }
            else
            {
                _t2sMulti.Add((key, values[0]));
            }
        }

        _t2sMulti.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
    }

    private static string? GetString(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var prop) ? prop.GetString() : null;

    private sealed record TitleNames(string? Zh, string? ZhHant, string? En, string? Ja)
    {
        public string? Best => Zh ?? ZhHant ?? En ?? Ja;
    }
}

/// <summary>在线更新得到的一个游戏：Id（如 "ns2-70010000092842"）、各语言名称、封面直链。</summary>
public sealed record OnlineTitle(string Id, IReadOnlyList<string> Names, IReadOnlyList<string> CoverUrls);
