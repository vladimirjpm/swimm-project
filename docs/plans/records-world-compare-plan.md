# Рекорды пловца против мирового: карточки вместо строк (Р1–Р7)

**Статус: НЕ НАЧАТО.** Ветка `records-world-compare` создана от master (`e0aeb83`), в ней пока
ни одного коммита. Это и план, и передача — читать первым, если продолжаешь эту работу.

Дизайн-пакет: `!design_handoff/design_handoff_wr_card/` (README пакета + макет
`wr-card-h2h.dc.html` + `h2h-record.additions.css`). Пакет — **референс в HTML, не код**.

## 1. Что делаем

На странице пловца, таб **Records & PB**, секция «Official records» сейчас рисует рекорды
обычными строками `SwimRow`. Заменяем их карточками семейства `UI_H2H*` в новом
варианте `variant="record"`: слева время пловца, справа — **мировой рекорд мастерс** той же
ступени с держателем, флагом и датой, посередине разрыв `+X.XX`. Рекорды группируются по
возрастной ступени, у каждой группы — золотой заголовок.

Смотреть глазами (дев поднимается связкой `stack-5181-api5079`, см. §6):

- [7571](http://localhost:5181/swimmers/7571?tab=results&view=records) — Игорь Кравков,
  54 рекорда, все мастерские, есть `Relay lead-off`. Главный экран проверки.
- [7467](http://localhost:5181/swimmers/7467?tab=results&view=records) — Зив Калонтаров,
  1 мастерский + 2 возрастных: проверка пустой правой стороны.

## 2. Решения Влада (20.09.2026)

1. **Карточками рисуются ВСЕ официальные рекорды**, не только мастерские. У немастерских
   (ISR age, ISR open) правая сторона **остаётся пустой** — мировых рекордов по возрастам у нас
   нет. Это «пока»: появится справочник — правая сторона заполнится сама, менять разметку
   не придётся.
2. Заголовок группы «MASTERS WR» справа печатается **только у групп, где WR есть**. Иначе шапка
   обещает то, чего в группе нет (решение агента, Влад не возражал).
3. `.h2h-event__dist` из `h2h-record.additions.css` в прод **не переносится** — красную
   дистанцию на иконке рисует сам `UI_SwimmStyleIcon`, в макете это костыль.

## 3. Что уже выяснено — не перепроверять

- **Источник правой стороны — таблица `Records`**, `RegionType='world'`, `Category='masters'`:
  1095 строк, 17 ступеней, оба бассейна (25m 562 / 50m 533). У всех заполнены `HolderName`,
  `HolderCountry` (alpha-3), `RecordDate`.
- **Ступени совпадают один в один** с ISR-мастерс (25-29 … 90-94), поэтому матч точный:
  `AgeKey + Gender + PoolType + Style + Distance`. Никакой эвристики не нужно.
- **`GetRecordsHeldAsync` (SwimmerPageRepository) кэша НЕ имеет** — второй запрос меток кэша
  не ломает. Захочется закэшировать — `GetOrCreateAsync` навесит метки сам (К3).
- **Флаг уже умеет alpha-3 → alpha-2**: `UI_FlagEmoji` (`components/mix/flag-icon/`). Свой
  `<img src="flagcdn…">` из макета не нужен.
- **Парсер времени на клиенте есть**: `timeToMs` в `utils/helpers/recalculate-positions.ts`.
- Секция сегодня — функция `HeldRecordsSection` в
  `client/src/projects/swimmer-project/components/swimmer-panels.tsx` (~стр. 503).

```sql
-- мировые рекорды мастерс: покрытие
select "PoolType", count(*), count(distinct "AgeKey")
from "Records" where "RegionType"='world' and "Category"='masters' group by 1;
```

## 4. Этапы

- **Р1. Сервер: WR к строке рекорда.** Поле `WorldRecord { Time, Date, Holder, HolderCountry }`
  (nullable) в `HeldRecordRow` (`Swimm.Application/Mapping/SeasonAggregator.cs`, ~стр. 102) и в
  `SwimmerHeldRecordDto` (`Dtos/SwimmerPageDtos.cs`, ~стр. 224), заполнение в
  `SwimmerPageRepository.GetRecordsHeldAsync` (~стр. 133) — один дополнительный запрос к
  `Records` по `world/masters` с фильтром по ступеням найденных рекордов, матч в памяти.
  Немастерские строки получают `null`. Маппинг — `SwimmerPageBuilder` (~стр. 519).
- **Р2. Клиент: тип.** `worldRecord?: { time; date; holder; countryCode }` в
  `SwimmerHeldRecord` (`swimmer-project/use-swimmer-profile.ts`, ~стр. 21).
- **Р3. Компоненты h2h — четыре добавки, вариант `"h2h"` НЕ трогать** (в макете он же 4a —
  контроль регрессии):
  - `UI_H2HEventCard`: `variant?: 'h2h' | 'record'` → класс `h2h-event--record`;
  - `UI_H2HPoolRow`: `deltaTone?: 'win' | 'behind'`;
  - `UI_H2HTimeCell` / `H2HPoolSide`: `who?: { name, countryCode }`, `extras?: ReactNode`.
- **Р4. CSS.** `h2h-record.additions.css` переносится в `client/src/projects/components/mix/h2h/h2h.css`
  как есть, кроме `.h2h-event__dist` (см. §2-3).
- **Р5. `HeldRecordsSection` на карточки.** Группировка `ageKey` → `stroke+distance` →
  `poolType`; заголовок `.h2h-group__head` («🏆 ISR · MASTERS 45-49» + `UI_RecordBadge` +
  линия + «MASTERS WR» по §2-2). Время пловца — ссылка на протокол (как сейчас,
  `routes.competitionSwims`), WR — без ссылки, с `title`. Разрыв считается на клиенте через
  `timeToMs`, тон всегда `behind`; нет WR → правая ячейка `—`, разрыв не рисуется.
  `Relay lead-off` переезжает в `extras` левой стороны.
- **Р6. Проверка в превью** — обе страницы из §1, в трёх рамках макета: 640px тёмная,
  343px (срабатывает `@container h2h (max-width:560px)`), светлая тема.
- **Р7. Тесты и доки.** Юнит-тесты: серверный матч WR (совпал / нет WR / другой бассейн) и
  клиентская группировка с разрывом; регрессия варианта `"h2h"`. Доки: строка в
  [../ui-components.md](../ui-components.md) (правило 5 pre-push-rules) и отметка в
  [athlete-page-plan.md](athlete-page-plan.md).

## 5. Открытые вопросы

Пока ни одного — развилка «что делать с немастерскими» закрыта решением §2-1.

## 6. Как поднять и посмотреть

```
preview_start { name: "stack-5181-api5079" }   # API 5079 + клиент 5181 одним слотом
```

⚠ Лимит 5 dev-серверов на папку; связка занимает один слот. «Failed to connect 5445» —
выключен Docker Desktop (Postgres в контейнере `swimm-postgres`, хост-порт 5445).

Макет открывается файлом: `!design_handoff/design_handoff_wr_card/wr-card-h2h.dc.html`
(двойной клик; из корня пакета, иначе не подхватятся `support.js`, `_ds/`, `assets/`).
