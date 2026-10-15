using System.Net.Http;
using SwitchAlbum.Models;
using SwitchAlbum.Services;

namespace SwitchAlbum;

/// <summary>
/// 无界面冒烟测试（--smoke-test）：模拟设备全流程（扫描 → 存电脑 → 存手机 → 校验），
/// 结果写入 %TEMP%\SwitchAlbumSmokeTest.txt，退出码 0/1。
/// </summary>
public static class SmokeTest
{
    public static async void RunAsync()
    {
        var lines = new List<string>();
        var ok = true;
        var root = Path.Combine(Path.GetTempPath(), "sab-smoke-" + Guid.NewGuid().ToString("N"));

        try
        {
            DemoAlbumGenerator.Generate(root);
            var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
            var tracker = new DuplicateTracker(Path.Combine(root, "saved.json"));
            var settings = new SettingsService(Path.Combine(root, "settings.json"));
            settings.Current.SavePath = Path.Combine(root, "saved-pc");
            var saveService = new SaveService(tracker);
            var phoneSaveService = new PhoneSaveService(tracker, settings);

            var devices = await provider.GetDevicesAsync(CancellationToken.None);
            await using var switchSession = await provider.ConnectAsync(devices.Single(d => d.IsSwitch), CancellationToken.None);
            var scan = await AlbumScanner.ScanAsync(switchSession, null, CancellationToken.None);
            if (scan == null)
            {
                throw new Exception("scan null");
            }

            lines.Add($"games={scan.Games.Count} items={scan.AllItems.Count}");

            var pcResult = await saveService.SaveToPcAsync(
                scan.AllItems, switchSession, settings.Current.SavePath, autoRename: true, null, CancellationToken.None);
            lines.Add($"pc-save saved={pcResult.SavedCount} skipped={pcResult.SkippedCount} failed={pcResult.Failed.Count} cancelled={pcResult.Cancelled}");
            ok &= pcResult.SavedCount == scan.AllItems.Count && pcResult.Failed.Count == 0;

            // 按游戏分文件夹：所有文件位于 保存目录\游戏名\ 下
            var pcFiles = Directory.GetFiles(settings.Current.SavePath, "*", SearchOption.AllDirectories).Length;
            lines.Add($"pc-files={pcFiles}");
            ok &= pcFiles == scan.AllItems.Count;
            var pcGameDirs = Directory.GetDirectories(settings.Current.SavePath).Select(Path.GetFileName).ToArray();
            ok &= pcGameDirs.Contains("塞尔达传说 王国之泪");

            var phoneDevice = (await provider.GetPhoneCandidateDevicesAsync(CancellationToken.None)).Single();
            await using var phoneSession = await provider.ConnectAsync(phoneDevice, CancellationToken.None);
            var phoneResult = await phoneSaveService.SaveToPhoneAsync(
                scan.AllItems, switchSession, phoneSession, autoRename: true, null, CancellationToken.None);
            lines.Add($"phone-save saved={phoneResult.SavedCount} skipped={phoneResult.SkippedCount} failed={phoneResult.Failed.Count}");
            ok &= phoneResult.SavedCount == scan.AllItems.Count;

            var phoneDcim = Path.Combine(root, "phone", "Internal shared storage", "DCIM", "SwitchAlbum");
            var phoneFiles = Directory.Exists(phoneDcim)
                ? Directory.GetFiles(phoneDcim, "*", SearchOption.AllDirectories).Length
                : 0;
            lines.Add($"phone-files={phoneFiles}");
            ok &= phoneFiles == scan.AllItems.Count;
            ok &= Directory.Exists(Path.Combine(phoneDcim, "塞尔达传说 王国之泪"));   // 手机端按游戏分文件夹

            // 二次保存应全部跳过
            var again = await saveService.SaveToPcAsync(
                scan.AllItems, switchSession, settings.Current.SavePath, autoRename: true, null, CancellationToken.None);
            lines.Add($"pc-resave skipped={again.SkippedCount}");
            ok &= again.SkippedCount == scan.AllItems.Count && again.SavedCount == 0;

            // 本地相册页流程：本地目录会话扫描电脑保存目录 → 保存到手机
            await using var localSession = new LocalFolderSession(settings.Current.SavePath);
            var localScan = await AlbumScanner.ScanAsync(localSession, null, CancellationToken.None);
            lines.Add($"local-scan games={(localScan?.Games.Count ?? 0)} items={(localScan?.AllItems.Count ?? 0)}");
            ok &= localScan != null && localScan.AllItems.Count == scan.AllItems.Count;
            if (localScan != null)
            {
                // 与界面一致：把电脑里的文件指回对应的 Switch 原图记录 → 已传过手机的会被跳过，不再出现两份副本
                var index = tracker.SnapshotPcPathIndex();
                foreach (var item in localScan.AllItems)
                {
                    if (index.TryGetValue(Path.GetFullPath(localSession.ToLocalPath(item.DevicePath)), out var key))
                    {
                        item.TrackingKey = key;
                    }
                }

                var localSave = await phoneSaveService.SaveToPhoneAsync(
                    localScan.AllItems, localSession, phoneSession, autoRename: true, null, CancellationToken.None);
                lines.Add($"local-phone saved={localSave.SavedCount} skipped={localSave.SkippedCount} failed={localSave.Failed.Count}");
                ok &= localSave.SkippedCount == localScan.AllItems.Count && localSave.SavedCount == 0;
                var phoneFiles2 = Directory.Exists(phoneDcim)
                    ? Directory.GetFiles(phoneDcim, "*", SearchOption.AllDirectories).Length
                    : 0;
                lines.Add($"phone-files-after-local={phoneFiles2}");
                ok &= phoneFiles2 == scan.AllItems.Count;
            }

            // 封面链路：Switch 2 风格文件夹名模糊反查 → 内置封面包
            var titleDb = TitleDbService.LoadEmbedded();
            var tid = titleDb?.GetTitleIdByFolderName("塞尔达传说 王国之泪 Nintendo Switch 2 Edition");
            var coverService = new CoverService(Path.Combine(root, "covers"));
            try
            {
                var urls = tid != null ? titleDb?.GetCoverUrls(tid) : null;
                var cover = await coverService.ResolveAsync(tid, "塞尔达传说 王国之泪", urls, CancellationToken.None);
                lines.Add($"cover tid={tid} urls={(urls?.Count ?? 0)} path={cover}");
                ok &= tid == "0100F2C0115B6000" && cover != null && File.Exists(cover);
                if (cover != null && File.Exists(cover))
                {
                    // 内置封面包为 256px 降采样图，尺寸 ≤256 说明命中离线包而非网络源
                    using var image = System.Drawing.Image.FromFile(cover);
                    var fromBundle = image.Width <= 256;
                    lines.Add($"cover 尺寸={image.Width}x{image.Height} 来自内置包={fromBundle}");
                    ok &= fromBundle;
                }
            }
            catch (Exception ex)
            {
                ok = false;
                lines.Add("cover exception: " + ex);
            }

            // iPhone 网页服务器：首页 + 全部 ZIP（本地相册页保存目录为源）
            try
            {
                using var iphoneServer = new IphoneWebServer(settings.Current.SavePath, ip: "127.0.0.1", startPort: 53771);
                iphoneServer.Start();
                using var http = new HttpClient();

                var home = await http.GetAsync(iphoneServer.Url);
                var homeText = await home.Content.ReadAsStringAsync();
                lines.Add($"iphone-home status={(int)home.StatusCode} hasZip={homeText.Contains("下载全部 ZIP")}");
                ok &= home.StatusCode == System.Net.HttpStatusCode.OK && homeText.Contains("下载全部 ZIP");

                var zipUrl = iphoneServer.Url.Replace("/?t=", "/zip?game=__all__&t=");
                var zipResponse = await http.GetAsync(zipUrl);
                var entries = 0;
                if (zipResponse.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    await using var zipStream = await zipResponse.Content.ReadAsStreamAsync();
                    using var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read);
                    entries = archive.Entries.Count;
                }

                lines.Add($"iphone-zip status={(int)zipResponse.StatusCode} entries={entries}");
                ok &= zipResponse.StatusCode == System.Net.HttpStatusCode.OK && entries == scan.AllItems.Count;
            }
            catch (Exception ex)
            {
                ok = false;
                lines.Add("iphone exception: " + ex);
            }
        }
        catch (Exception ex)
        {
            ok = false;
            lines.Add("exception: " + ex);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // 清理失败不影响结果
            }
        }

        lines.Add(ok ? "SMOKE PASS" : "SMOKE FAIL");
        var resultPath = Path.Combine(Path.GetTempPath(), "SwitchAlbumSmokeTest.txt");
        File.WriteAllLines(resultPath, lines);
        Environment.Exit(ok ? 0 : 1);
    }
}
