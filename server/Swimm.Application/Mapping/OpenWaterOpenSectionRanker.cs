using System.Collections.Generic;
using System.Linq;

namespace Swimm.Application.Mapping;

/// <summary>Строка общей категории («כללי») для пересчёта мест: ключ блока — дисциплина.</summary>
public sealed record OpenSectionRow(long Id, string BlockKey, string? Gender, int? Position);

/// <summary>
/// Места общей категории («כללי») открытой воды — внутри ПОЛА (решение Влада 07.10.2026,
/// docs/data-integrity.md, Р78).
///
/// Откуда берётся кривое место. На loglig мужская и женская дистанции открытой воды — РАЗНЫЕ
/// дисциплины («1600 חופשי נשים», «1600 חופשי גברים»), и по каждой из них «כללי» свой. А PDF
/// всего соревнования печатает обе одним блоком «1600»: секции «בנות…», «בנים…» и в конце ОДНА
/// общая секция на оба пола, пронумерованная подряд. Пола в ней нет вовсе (парсер ставит
/// «none», пол строке дописывает импорт из карточки, Р47). Итог на Эйлате-2026: עומר שורץ —
/// единственная женщина в общей категории 1600 м, у нас 2-е место, у loglig и по регламенту
/// («3 המנצחות ו-3 המנצחים») — 1-е; מרסי אן נגירה на 5 км — 3-е вместо 1-го.
///
/// Пересчёт сохраняет ПОРЯДОК напечатанных мест и делёж (одинаковое место остаётся одинаковым),
/// а номер считает только среди своего пола. Блок одного пола не трогается — там напечатанное
/// место и так верное. Повторный прогон ничего не меняет.
///
/// ⚠ Только для открытой воды: смешанные личные блоки бассейна («mix-shabbat», детские лиги)
/// могут быть настоящим общим зачётом, и там напечатанное место — правда протокола (Р76).
/// </summary>
public static class OpenWaterOpenSectionRanker
{
    /// <summary>Новые места строк, у которых место изменилось: Id → место.</summary>
    public static IReadOnlyDictionary<long, int> Rerank(IEnumerable<OpenSectionRow> rows)
    {
        var changes = new Dictionary<long, int>();

        foreach (var block in rows.GroupBy(r => r.BlockKey))
        {
            var genders = block.Select(r => RelayGender.Normalize(r.Gender)).Where(g => g != null).Distinct().Count();
            if (genders < 2) continue;

            foreach (var side in block.GroupBy(r => RelayGender.Normalize(r.Gender)))
            {
                if (side.Key is null) continue;     // пол неизвестен — не гадаем

                var placed = side.Where(r => r.Position is > 0).ToList();
                foreach (var row in placed)
                {
                    var place = 1 + placed.Count(o => o.Position < row.Position);
                    if (place != row.Position) changes[row.Id] = place;
                }
            }
        }

        return changes;
    }
}
