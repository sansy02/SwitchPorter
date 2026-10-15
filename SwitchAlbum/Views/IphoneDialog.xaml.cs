using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using QRCoder;
using SwitchAlbum.Resources;
using SwitchAlbum.Services;

namespace SwitchAlbum.Views;

public partial class IphoneDialog : Window
{
    private readonly IphoneWebServer _server;

    public IphoneDialog(IphoneWebServer server, string summary)
    {
        InitializeComponent();
        _server = server;
        SummaryText.Text = summary;

        var addresses = server.CandidateAddresses.Count > 0
            ? server.CandidateAddresses.ToList()
            : new List<string> { server.CurrentAddress };
        if (!addresses.Contains(server.CurrentAddress))
        {
            addresses.Insert(0, server.CurrentAddress);
        }

        AddressBox.ItemsSource = addresses;
        AddressBox.SelectedItem = server.CurrentAddress; // 触发 SelectionChanged → 生成二维码
        if (AddressBox.SelectedItem == null)
        {
            Refresh();
        }
    }

    private void AddressBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AddressBox.SelectedItem is string ip)
        {
            _server.SelectAddress(ip);
            Refresh();
        }
    }

    private void Refresh()
    {
        QrImage.Source = GenerateQr(_server.Url);
        CopyButton.ToolTip = _server.Url;
        CopyButton.Content = Strings.Iphone_CopyUrl;
    }

    private static BitmapImage GenerateQr(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        var png = qr.GetGraphic(12);

        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_server.Url);
            CopyButton.Content = Strings.Iphone_Copied;
        }
        catch
        {
            // 剪贴板占用时忽略
        }
    }

    private void Firewall_Click(object sender, RoutedEventArgs e)
    {
        FirewallStatus.Text = FirewallHelper.TryAllowThisApp()
            ? Strings.Iphone_FirewallDone
            : Strings.Iphone_FirewallFailed;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
