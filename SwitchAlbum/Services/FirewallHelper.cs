namespace SwitchAlbum.Services;

/// <summary>
/// Windows 防火墙放行：iPhone 扫码后一直转圈，最常见的原因是首次运行时防火墙弹窗被取消，
/// 或当前网络被设为「公用网络」。这里用 netsh 为本程序添加入站放行规则（需要管理员权限，会弹 UAC）。
/// </summary>
public static class FirewallHelper
{
    private const string RuleName = "SwitchAlbum iPhone 局域网传输";

    /// <returns>true = 已执行（用户同意了 UAC 且 netsh 成功）；false = 用户取消或执行失败。</returns>
    public static bool TryAllowThisApp()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return false;
        }

        // 先删同名旧规则（exe 位置可能变了），再添加；profile=any 覆盖专用/公用网络
        var args = $"/c netsh advfirewall firewall delete rule name=\"{RuleName}\" >nul 2>&1 & " +
                   $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow " +
                   $"program=\"{exe}\" enable=yes profile=any protocol=TCP";
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            if (process == null)
            {
                return false;
            }

            process.WaitForExit(15000);
            var ok = process.HasExited && process.ExitCode == 0;
            Log.Info($"防火墙规则添加{(ok ? "成功" : "失败")}: {exe}");
            return ok;
        }
        catch (Exception ex)
        {
            // 用户在 UAC 中点了「否」会抛 Win32Exception
            Log.Info("防火墙规则添加未执行: " + ex.Message);
            return false;
        }
    }
}
