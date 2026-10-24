using System.Text.RegularExpressions;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сторож XSS в админке (найдено 23.10.2026 при ревью удаления аккаунта): строка пользователя
/// внутри JS-строки в атрибуте — <c>onsubmit="return confirm('… @u.Email …')"</c>. Razor
/// кодирует <c>'</c> как <c>&amp;#x27;</c>, браузер декодирует его ДО запуска JS, и email
/// <c>x'-alert`1`-'@a.co</c> (его пропускает регистрация — <c>MailAddress</c> считает такое
/// валидным) выполняет код с сессией админа — в ProdAdmin против прод-базы.
///
/// Правильно: текст — в <c>data-confirm="…"</c> (обычное HTML-кодирование атрибута), обработчик —
/// <c>confirm(this.dataset.confirm)</c>. Тест ловит Razor-выражение внутри JS-строки любого
/// <c>on*</c>-атрибута; числа (<c>@Model.Count</c>) безопасны, но правило одно для всех —
/// проще писать сразу через data-атрибут.
/// </summary>
public class AdminInlineJsInjectionTests
{
    // on*="…'…@Выражение…" — Razor-выражение после открытой одинарной кавычки внутри обработчика.
    private static readonly Regex RazorInsideJsString = new(
        @"\bon[a-z]+=""[^""]*'[^""']*@(?!@)[A-Za-z(]", RegexOptions.Compiled);

    // Уже существующие места, где в JS-строку подставляется не строка пользователя: числа (две
    // страницы соревнований) и метод/путь из каталога API, который пишем мы сами.
    private static readonly HashSet<string> NumericOnly = new(StringComparer.Ordinal)
    {
        "Swimm.API/Pages/Admin/Competitions/Edit.cshtml",
        "Swimm.API/Pages/Admin/Competitions/MoveResults.cshtml",
        "Swimm.API/Pages/Admin/Shared/_ApiEndpointCard.cshtml",
    };

    [Fact]
    public void NoRazorExpressionsInsideInlineJsStrings()
    {
        var server = ServerDirectory();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(server, "Swimm.API", "Pages"), "*.cshtml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(server, file).Replace('\\', '/');
            if (NumericOnly.Contains(relative)) continue;
            var n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                if (RazorInsideJsString.IsMatch(line)) offenders.Add($"{relative}:{n}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Razor-выражение внутри JS-строки в on*-атрибуте — XSS, если там строка пользователя. " +
            "Перенесите текст в data-confirm=\"…\" и зовите confirm(this.dataset.confirm): " +
            string.Join(", ", offenders));
    }

    private static string ServerDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Swimm.sln"))) return dir.FullName;

        throw new InvalidOperationException(
            $"Не нашёл Swimm.sln выше {AppContext.BaseDirectory} — тест смотрит исходники и без них не работает.");
    }
}
