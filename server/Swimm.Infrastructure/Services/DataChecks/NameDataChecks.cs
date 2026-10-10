using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;
using Swimm.Application.Mapping;

namespace Swimm.Infrastructure.Services.DataChecks;

/// <summary>
/// Английское имя пловца не подходит к ивритскому (10.10.2026, docs/plans/en-names-handoff.md).
///
/// Две болезни одной проверкой, обе — от двуязычного импорта пары HE+EN:
/// - <b>иврит в латинском поле</b> — EN-строка протокола потеряла фамилию (перенос имени
///   вместе с клубом), и импорт подставил ивритскую («ברוטמן | BRUTMAN»);
/// - <b>чужое имя</b> — EN-строка съехала на соседа (июльский разбор comp 1486: «פוגל דודוביץ
///   איתן» ↔ «ELKIN DANIEL»), близнецы с переставленными именами, просто другой человек.
///
/// Правило — <see cref="EnglishNameFit"/>: каждое латинское слово обязано найтись среди
/// ивритских. Прозвище или второе имя только в латинице («MOTI» у «מרדכי», «ALEKSANDR» у
/// «סשה») — ложная находка, её закрывают «Принять». Лечение — <c>--fix-en-names</c>
/// (карточка loglig) или правка руками на /Admin/Swimmers.
/// </summary>
public sealed class SwimmerEnglishNameCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "swimmers.en-name-mismatch";
    public string Title => "Английское имя не подходит к ивритскому";
    public string Description =>
        "Иврит в латинском поле или латинское слово, не похожее ни на одно ивритское (сравнение по "
        + "согласным). Обычно — сдвиг строки в паре протоколов HE+EN или потерянная EN-фамилия. "
        + "Чинится командой --fix-en-names по карточке loglig; прозвища и вторые имена — «Принять».";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    /// <summary>Сколько находок показать. На 10.10.2026 их ~70 — показываем все.</summary>
    internal const int Shown = 200;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var swimmers = await db.Swimmers.AsNoTracking()
            .Where(s => s.LastNameEn != "" || s.FirstNameEn != "")
            .Select(s => new { s.Id, s.LastName, s.FirstName, s.LastNameEn, s.FirstNameEn, s.BirthYear, s.LogligId })
            .ToListAsync(ct);

        var found = swimmers
            .Select(s => (S: s, A: EnglishNameFit.Assess(s.LastName, s.FirstName, s.LastNameEn, s.FirstNameEn)))
            .Where(x => x.A.Verdict is EnglishNameVerdict.HebrewInLatin or EnglishNameVerdict.Foreign)
            .OrderBy(x => x.A.Verdict == EnglishNameVerdict.HebrewInLatin ? 0 : 1)
            .ThenBy(x => x.A.Score)
            .ThenBy(x => x.S.Id)
            .ToList();

        return new DataCheckOutcome(found.Count, found
            .Take(Shown)
            .Select(x => new DataCheckItem(
                "Swimmer", x.S.Id,
                $"{x.S.LastName} {x.S.FirstName} ({x.S.BirthYear}) ↔ «{x.S.LastNameEn} | {x.S.FirstNameEn}»",
                x.A.Verdict == EnglishNameVerdict.HebrewInLatin
                    ? "иврит в латинском поле"
                    : $"«{x.A.Word}» не похоже ни на одно ивритское слово ({x.A.Score:0.00})"
                      + (x.S.LogligId is null ? "; loglig-id нет — только руками" : ""),
                $"/Admin/Swimmers/Edit?id={x.S.Id}",
                PublicRoutes.Swimmer(x.S.Id)))
            .ToList());
    }
}
