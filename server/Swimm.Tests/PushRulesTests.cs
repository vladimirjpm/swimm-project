using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Правила push-подписок (<see cref="PushRules"/>, docs/plans/web-push-reminders-plan.md §6):
/// какой адрес подписки сервер примет, ключи и подпись устройства.
/// </summary>
public class PushRulesTests
{
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc:def")]
    [InlineData("https://web.push.apple.com/QGx1Y2E")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/gAAA")]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=BQYAAA")]
    public void Endpoint_KnownPushServices_Allowed(string endpoint)
    {
        Assert.True(PushRules.IsAllowedEndpoint(endpoint));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]          // не https
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc")]    // чужой порт
    [InlineData("https://user@fcm.googleapis.com/fcm/send/abc")]    // логин в адресе
    [InlineData("https://evil-fcm.googleapis.com.example.com/x")]   // суффикс не на границе домена
    [InlineData("https://notfcm.googleapis.com.evil/x")]
    [InlineData("https://localhost/x")]
    [InlineData("https://10.0.0.5/x")]
    [InlineData("not a url")]
    public void Endpoint_Other_Rejected(string? endpoint)
    {
        Assert.False(PushRules.IsAllowedEndpoint(endpoint));
    }

    [Fact]
    public void Endpoint_TooLong_Rejected()
    {
        Assert.False(PushRules.IsAllowedEndpoint("https://fcm.googleapis.com/" + new string('a', PushRules.EndpointMax)));
    }

    [Theory]
    [InlineData("BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM", true)]
    [InlineData("tBHItJI5svbpez7KI4CCXg==", true)]
    [InlineData("has space", false)]
    [InlineData("a+b/c", false)]
    [InlineData("", false)]
    public void Base64Url(string value, bool ok)
    {
        Assert.Equal(ok, PushRules.IsBase64Url(value, PushRules.KeyMax));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Mobile/15E148 Safari/604.1", "iPhone · Safari")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Mobile Safari/537.36", "Android · Chrome")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; SM-S921B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/25.0 Chrome/121.0 Mobile Safari/537.36", "Android · Samsung Internet")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0 Safari/537.36 Edg/129.0", "Windows · Edge")]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 14.5; rv:131.0) Gecko/20100101 Firefox/131.0", "Mac · Firefox")]
    [InlineData(null, "Device")]
    public void DeviceLabel_FromUserAgent(string? ua, string label)
    {
        Assert.Equal(label, PushRules.DeviceLabel(ua));
    }
}
