# Задание (Sonnet 5): фаза 3, этап 3.2-client — paged-режим загрузки результатов

Контекст: фаза 3 роадмапа ([docs/ROADMAP.md](../ROADMAP.md)). **Сначала прочитай контракт**:
[phase3-paged-results-contract.md](phase3-paged-results-contract.md) — он первичен; это
задание — его клиентская проекция. Серверная часть готова и проверена (2026-07-09):
все параметры §2 контракта, `total` в ответе, лимит глубины, публичные
`GET /api/results/filter-hints`.

## Что уже готово (не переделывать)

- Шов: админ-настройка `ResultsLoadMode` + `GET /api/client-config` +
  `ResultsLoadModeHelper` (`client/src/utils/helpers/results-load-mode.ts`). Режим `paged`
  сейчас честно работает как `full` (см. TODO в `loadFromApi`,
  `client/src/projects/components/filter-data-source-ddl/filter-data-source-ddl.tsx`).
- Сервер: `/api/results` понимает все фильтры §2 контракта и отдаёт
  `{ page, pageSize, hasMore, total, data }`. В локальной БД синтетика 3 млн строк
  (600 соревнований «SYNTH Meet NNNN» по 5000 строк) — на ней paged-режим и проверяй.
- **Full-режим не трогаем** — он остаётся дефолтом и эталоном сравнения. Всё новое
  ветвится по `mode === 'paged'`.

## Шаг 1 — Redux: состояние пагинации

В `store/store.ts` (единый rootSlice, поле в `StateInterface` + `initialState`):

```ts
resultsPaging?: {
  page: number;       // последняя загруженная страница
  pageSize: number;   // 100
  total: number;      // из ответа сервера
  hasMore: boolean;
};
```

В paged-режиме `dataSourceSelected.results` содержит **накопленные загруженные страницы**
(v1 — паттерн «Show more»: страницы аппендятся), а не весь датасет. Смена источника или
любого фильтра сбрасывает: `results = страница 1`, `page = 1`.

## Шаг 2 — загрузка: paged-ветка в `loadFromApi`

- `mode === 'paged'`: один fetch одной страницы (`pageSize=100`) с фильтрами
  query-параметрами; вернуть `{ results, paging }`, а не крутить `while hasMore`.
- Маппинг клиентских фильтров (`state.filterSelected`) → query (§2 контракта):

| Клиент | Query | Примечание |
|---|---|---|
| `selected_name` | `name` | prefix |
| `club` | `club` | prefix |
| `style_name` | `styleName` | |
| `style_len` | `distance` | строкой |
| `gender` | `gender` | |
| `pool_type` | `poolType` | |
| `age` / `age_to` (обычный режим) | `birthYearFrom` / `birthYearTo` | это ГОДЫ РОЖДЕНИЯ; одиночный год → from=to |
| `age` (masters-режим, строки «25-29») | `ageGroup` | |
| `position_filter` | `position` | `all` не передавать; `top` / `podium` |
| `event_date` | `eventDate` | `dd/MM/yyyy`; `all` не передавать |
| источник (селектор) | `eventId` / `competitionId` | как в full |

- Значения «all»/пустые — параметр не передаётся вообще (чистые URL, кэш-ключи сервера).
- Текстовые фильтры (name/club) — debounce 300 мс перед запросом.
- Ошибка fetch → не рушить экран: оставить текущие результаты, лог в консоль.

## Шаг 3 — «Show more» + счётчик

- Под таблицей результатов в paged-режиме — кнопка «Show more» (видна пока `hasMore`):
  грузит `page + 1` с теми же фильтрами и аппендит к `results`.
- Счётчик «showing N of {total}» — из `resultsPaging.total`, а не из длины массива.
- Сервер отвечает 400 на `page*pageSize > 10000` — UI до этого не доводит (100 страниц
  по 100 никто не долистает), но обработай 400 молча-без-краша на всякий случай.

## Шаг 4 — фильтры: опции и скрытие несерверных

- В paged-режиме доступные опции фильтров (клубы, стили, дистанции, имена) нельзя
  вычислять из `results` (там только загруженные страницы). Источник — публичный
  `GET /api/results/filter-hints?field=<style|distance|club|competition|name>&q=&limit=`
  (сервер кэширует, prefix-поиск для name/club по мере ввода).
- По §5 контракта в paged-режиме v1 **скрыть** (не disable — именно не рендерить):
  - фильтр уровня (`level_filter`, filter-level-buttons) — бейджи уровня на строках
    ОСТАЮТСЯ (они считаются по загруженным строкам);
  - тумблер пересчёта мест (`is_recalculated`, filter-recalculate);
  - переключатель `activity_type` — paged всегда competition.
- Каждое изменение фильтра → сброс на страницу 1 + новый запрос (см. Шаг 1).

## Что НЕ входит в задание

- Серверные аналоги level_filter / is_recalculated — фаза 3.4.
- ETag/HTTP-кэш публичных GET — отдельное задание 3.1 (может идти параллельно, файлы
  почти не пересекаются; конфликт возможен только в `ResultsController` — его в этом
  задании не трогаешь вообще).
- Изменения full-режима, сервера, контракта.

## Приёмка

- `npx tsc --noEmit` чистый; `dotnet build/test` не трогал → зелёные по определению.
- Живая проверка на синтетике (API поднят, Vite dev, `?loadMode=paged`):
  - выбрать «SYNTH Meet …» (5000 строк): открывается мгновенно, в сети ОДИН запрос
    страницы (не 10 по 500), «showing 100 of 5000», Show more догружает;
  - каждый фильтр из таблицы Шага 2 по отдельности: total меняется, строки соответствуют;
  - фильтры level/recalculate/activity_type не видны; в full-режиме — видны как раньше.
- Паритет с full (§7 контракта): на РЕАЛЬНОМ соревновании (не SYNTH) одинаковый набор
  строк в full и paged при: без фильтров, style+distance, gender, position=podium,
  age-диапазон. (Сравнивай по количеству и первым/последним строкам.)
- Регресс full-режима: `?loadMode=full` — поведение не изменилось.

## Правила репо

- RU-комментарии/EN-идентификаторы; не коммитить без просьбы; паттерн фичи и карту `src/`
  смотри в `client/CLAUDE.md`. Store — единый rootSlice, новых slice не заводить.
- Контракт менять нельзя; если он где-то не бьётся с реальностью клиента — стоп и вопрос,
  а не самодеятельность.
