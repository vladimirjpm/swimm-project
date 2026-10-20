using System.Security.Cryptography;

namespace Swimm.API.Cli;

/// <summary>Консольные команды Web Push (docs/plans/web-push-reminders-plan.md); вызов — <see cref="CliCommands"/>.</summary>
internal static class PushCli
{
    // Пара ключей VAPID (P-256) для секции WebPush:
    //   dotnet run --project server/Swimm.API -- --generate-vapid-keys
    // ⚠ Смена ключей на проде аннулирует ВСЕ подписки — генерировать один раз. Ключи в базу не
    // пишутся и в лог не идут: только в консоль, откуда их кладут в App Service / Local.json.
    internal static Task GenerateVapidKeysAsync(WebApplication app, string[] args)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(includePrivateParameters: true);
        // Публичный — несжатая точка 0x04 || X || Y (65 байт), приватный — скаляр D (32 байта); оба base64url.
        var publicKey = Base64Url(new byte[] { 0x04 }.Concat(p.Q.X!).Concat(p.Q.Y!).ToArray());
        var privateKey = Base64Url(p.D!);

        Console.WriteLine("VAPID keys (generate ONCE — changing them invalidates every subscription):");
        Console.WriteLine();
        Console.WriteLine($"WebPush__PublicKey={publicKey}");
        Console.WriteLine($"WebPush__PrivateKey={privateKey}");
        Console.WriteLine("WebPush__Subject=mailto:noreply@swimhub.co.il");
        Console.WriteLine();
        Console.WriteLine("appsettings.Local.json:");
        Console.WriteLine($"  \"WebPush\": {{ \"PublicKey\": \"{publicKey}\", \"PrivateKey\": \"{privateKey}\" }}");
        return Task.CompletedTask;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
