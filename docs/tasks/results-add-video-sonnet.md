# Таск: «Add video» на странице результатов (results_main)

## Контекст

Сейчас залогиненный пользователь может привязать видео к заплыву только со страницы
My media (`client/media.html`), где надо вручную каскадом выбирать пловца → соревнование →
заплыв. Решение: вернуть добавление видео прямо на строку заплыва в таблице результатов
(`client/results_main.html` → фича `client/src/projects/results-table/`), потому что там
пользователь уже нашёл нужный заплыв фильтрами.

UX утверждён Владом: в шапке таблицы (в колонке избранного, над сердечками) — **иконка-тумблер
«режим добавления видео»**; пока тумблер выключен, таблица выглядит как сейчас. При включении
на каждой строке под сердечком появляется маленькая иконка «добавить видео»; клик открывает
существующий попап `AddLinkModal` в single-step режиме (`fixedResultId`) — пользователь вводит
только URL.

## Решения (зафиксированы, не пересматривать)

1. **Тумблер и иконки — иконки, не чекбокс и не текст.** Символ камеры/видео (🎥 или SVG-подобный
   спан — смотри, как в проекте делают такие мелкие контролы; допустим emoji, как в
   `AddLinkModal` кнопке `📎`). Тумблер в активном состоянии визуально подсвечен
   (акцентный цвет), tooltip `title="Add video mode"`.
2. **Показывать только залогиненным**: `isAuthenticated` из `useFavoritesContext()` — уже
   доступен в `results-table.tsx:37`. Гостю не рендерим ни тумблер, ни иконки на строках.
3. **Состояние тумблера — локальный `useState` в `ResultsTable`**, НЕ Redux, НЕ persist:
   сбрасывается при перезагрузке страницы.
4. **Иконка на строке** появляется только если: режим включён, `res.swimmer_id != null`,
   `typeof (res as any).id === 'number'` (числовой id Result с API), `!res.is_relay`.
   Для строк, не проходящих условие, ничего не рендерим (без плейсхолдера).
5. **Попап — переиспользуем `AddLinkModal` как есть**
   (`client/src/projects/my-media-project/components/add-link-modal.tsx`), новый попап НЕ писать.
   Кросс-импорт из `my-media-project` в `results-table` — ок, компонент задуман переиспользуемым.
   Пропсы:
   - `fixedResultId={(res as any).id}`
   - `initialSwimmerId={res.swimmer_id}` — **обязательно**, иначе `save()` молча не сработает
     (внутри guard `swimmerId == null`);
   - `swimmers={[]}` (шаги 2–3 в single-step не показываются);
   - `contextLabel` — карточка контекста: имя пловца, `{len}m {style}`, время, соревнование, дата
     (поля `res.first_name/last_name`, `res.event_style_len`, `res.event_style_name`, `res.time`,
     `res.competition`, `res.date`);
   - `onSave` / `onClose` — см. ниже.
6. **Сохранение — тот же API `POST /api/me/media`.** В
   `client/src/projects/my-media-project/use-all-my-media.ts` внутри хука `useAllMyMedia`
   уже есть `add` (строка ~125) с antiforgery-логикой. Хук на страницу результатов НЕ тащить
   (он грузит всё медиа юзера). Вместо этого: **вынести из `add` standalone-функцию**
   `export async function addUserMedia(input: AddMediaInput): Promise<AllUserMediaDto | null>`
   в том же файле (fetch antiforgery-токена там уже оформлен отдельной функцией, ~строка 46),
   а хук `useAllMyMedia.add` переписать так, чтобы он вызывал её и обновлял свой локальный стейт.
   Поведение My media не должно измениться.
7. **После успешного сохранения** — закрыть попап и обновить медиа-иконки строк:
   `useCompetitionMedia` (`client/src/hooks/useCompetitionMedia.ts`) дополнить функцией
   `refresh()` (возврат из хука сделать объектом или кортежем — смотри по месту, но не сломай
   существующего потребителя в `results-table.tsx:46`), и дёрнуть её после успешного add.
   Если внутри хука есть кэш — при refresh его для текущих параметров сбросить.
8. **Раскладки.** Оркестратор `results-table.tsx` рендерит два варианта строк: mobile
   (`results-table-mobile.tsx`) и desktop (`results-table-desktop.tsx`). `results-table-2xl.tsx`
   оркестратором НЕ используется — его не трогать.
   - Desktop-шапка (`results-header.tsx`, view `desktop`): первая ячейка сетки сейчас пустая
     (`<div aria-hidden />`, строка 52) — тумблер ставим туда.
   - Mobile-шапка (view `mobile`): по образцу закомментированного «Show all open»
     (строки 30–42) — абсолютно спозиционированная иконка `right-3`. Комментированный блок
     не удалять.
   - Пропсы тумблера прокинуть в `ResultsHeader` (`addVideoMode?: boolean`,
     `onToggleAddVideoMode?: () => void`; рендерить тумблер только когда колбэк передан —
     это и есть признак «залогинен»).
9. **Иконка на строке — в колонке FAV**, сразу под `UI_FavoriteControls`
   (desktop: `results-table-desktop.tsx:53-61`; mobile — найди аналогичный блок с
   `UI_FavoriteControls`, ~строка 66). В строчные компоненты прокинуть один опциональный
   колбэк `onAddVideo?: () => void` через `ResultsTableRowProps`
   (`client/src/projects/results-table/components/types.ts`); если он не передан — не рендерим.
   Логика «когда передавать» (п.4) живёт в оркестраторе `results-table.tsx`.
10. **UI-тексты только English** (правило проекта). Комментарии в коде — RU, как вокруг.

## Что уже готово (не переделывать)

- `AddLinkModal` — single-step режим полностью рабочий (используется в My media на строке
  заплыва). Ничего в нём менять не нужно.
- Antiforgery + `credentials: 'include'` — уже оформлены в `use-all-my-media.ts`.
- `res.swimmer_id` и числовой `(res as any).id` уже есть в данных строк (см. использование
  `mediaByResultId.get((res as any).id)` в `results-table.tsx:402`).
- Сервер (`POST /api/me/media`) менять НЕ нужно: он принимает `swimmer_id` + `result_id`,
  сам выводит level и competition, создаёт медиа с `Visibility=private`.

## Шаги

1. `use-all-my-media.ts`: вынести `addUserMedia()` (п.6), хук использует её.
2. `useCompetitionMedia.ts`: добавить `refresh()` (п.7), поправить потребителя.
3. `types.ts` (results-table): `onAddVideo?: () => void` в `ResultsTableRowProps`.
4. `results-header.tsx`: пропсы + тумблер в desktop и mobile шапках (п.8).
5. `results-table-desktop.tsx` и `results-table-mobile.tsx`: иконка под сердечком (п.9).
6. `results-table.tsx`: `useState` режима, передача колбэков, стейт «какой результат добавляем»
   (`addVideoFor: {resultId, swimmerId, label...} | null`), рендер `AddLinkModal`, `onSave` через
   `addUserMedia` → при успехе `refresh()` медиа и закрытие.

## Тесты

Юнит-тестов на клиенте в проекте нет (Swimm.Tests — только сервер, серверное поведение не
меняется) — проверка типами и вживую:

## Проверка

```bash
npx --prefix client tsc --noEmit
npm --prefix client run build
```

Вживую: API на :5079 (`dotnet run --project server/Swimm.API --configuration Release --urls http://localhost:5079`),
клиент `npm --prefix client run dev` с конфигом client-5079 из `.claude/launch.json` (proxy на 5079).
Прокликать на `results_main.html`:
- гость: тумблера нет;
- залогиненный: тумблер в шапке (desktop и mobile ширины), включил → иконки на строках
  (кроме эстафет), клик → попап с контекстом заплыва, вставил YouTube-URL → Save →
  попап закрылся, на строке появилась иконка галереи (медиа private, видно владельцу);
- My media (`media.html`) работает как раньше (добавление оттуда не сломано).

## Footguns

- Visual Studio может держать :5078 и лочить билд — работай через `--configuration Release`
  и порт :5079 (см. выше), не убивай чужие процессы.
- `store.ts` — единый rootSlice; сюда НИЧЕГО не добавляем, состояние тумблера локальное.
- Правило парных токенов темизации: тумблер в шапке красить токенами
  `--theme-mode-*` (шапка — surface-alt), проверить light и dark (`data-mode` на `<html>`).
- `AddLinkModal` стилизован фиксированной тёмной палитрой — это ок, он так задуман, не темизируй.

## Вне скоупа (не делать)

- Серверные изменения (rate limit на `POST /api/me/media` — отдельная задача, делает Fable).
- Изменение шагов/порядка глобального «+ Add link» на My media.
- `results-table-2xl.tsx` и что-либо в Redux store.
- PATCH/relink медиа, публикации в группы.
