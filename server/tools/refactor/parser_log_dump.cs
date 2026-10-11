#:project ../../Swimm.Parsing/Swimm.Parsing.csproj
#:property PublishAot=false
// Журнал разбора (GetDebugLog) по всем PDF-протоколам фикстур — по файлу на протокол.
// Запуск из этой папки: dotnet run parser_log_dump.cs -- <outDir> [<fixturesDir>]
// До и после правки IsrOrgCompetitionParser — diff -r каталогов должен быть пуст.
using Swimm.Parsing.Parsers.IsrOrg;
var fx = args.Length > 1 ? args[1] : "../../Swimm.Tests/Fixtures/Parsing";
var outDir = args[0]; Directory.CreateDirectory(outDir);
foreach (var f in Directory.GetFiles(fx, "*.pdf").Where(f => !Path.GetFileName(f).StartsWith("loglig-") && !Path.GetFileName(f).StartsWith("isr-age")))
{
    var lang = Path.GetFileName(f).Contains("_EN") ? "EN" : "HE";
    using var fs = File.OpenRead(f);
    var n = IsrOrgCompetitionParser.ParseCompetitions(fs, lang).Count();
    File.WriteAllText(Path.Combine(outDir, Path.GetFileName(f) + ".log"), IsrOrgCompetitionParser.GetDebugLog());
    Console.WriteLine($"{Path.GetFileName(f)} {lang} events={n}");
}
