using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// ЕДИНСТВЕННЫЙ ответ на вопрос «какой сезон витрина считает текущим» — витринный сезон
/// правила <see cref="Swimm.Domain.ShowcaseSeason"/> (docs/season-boundary-rule.md).
///
/// Шов появился 2026-09-01 по багу: правило было записано и реализовано, но спрашивали его
/// только двое (плитки шапки клуба и карусель сезонов пловца), а карточка Season best клуба
/// и страница <c>/season-best</c> продолжали брать КАЛЕНДАРНЫЙ <c>SeasonMath.CurrentStartYear()</c>.
/// Разницы не было видно до 31 августа — 1 сентября календарный сезон шагнул вперёд, и
/// витрины опустели: в новом сезоне стартов ещё нет по определению.
///
/// ⚠ Поэтому правило: умолчание сезона на витрине берётся ТОЛЬКО отсюда.
/// <c>SeasonMath.CurrentStartYear()</c> законен там, где речь о принадлежности заплыва
/// сезону (возраст в сезоне, ростер, фильтры, импорт), и запрещён как «сезон по умолчанию».
/// </summary>
public interface IShowcaseSeasonProvider
{
    /// <summary>Год НАЧАЛА витринного сезона на сейчас (метка — <c>SeasonMath.Label</c>).</summary>
    Task<int> CurrentStartYearAsync(CancellationToken ct = default);

    /// <summary>
    /// То же с явным «сейчас». Нужен тестам (иначе они протухают на переходе даты) и
    /// пересчётам задним числом: граница вычисляется по данным и известна только постфактум.
    /// </summary>
    Task<int> StartYearAtAsync(DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Пояснение для витрин season best: новый сезон уже идёт по календарю, но витрина
    /// держит прошлый — данные откроются после зимнего чемпионата.
    ///
    /// <c>null</c> — витринный сезон совпадает с календарным, объяснять нечего. Витрина
    /// ОБЯЗАНА показать эту заметку, когда она приходит: иначе пустая карточка в сентябре
    /// выглядит как поломка, а не как «сезон ещё не начался».
    /// </summary>
    Task<ShowcaseSeasonNoticeDto?> PendingNoticeAsync(CancellationToken ct = default);

    /// <summary>То же с явным «сейчас» — для тестов и пересчётов задним числом.</summary>
    Task<ShowcaseSeasonNoticeDto?> PendingNoticeAtAsync(DateTime now, CancellationToken ct = default);
}
