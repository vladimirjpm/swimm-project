# Перед каждым push — «поменял X → обнови Y»

Правила синхронизации: документация, зеркала и сгенерированные файлы, которые обязаны
меняться вместе с кодом. Каждое по отдельности записано где-то в CLAUDE.md или в доке
подсистемы, но вспоминают их по одному и поздно — поэтому здесь они собраны в один проход
(решение Влада 11.09.2026).

**Как проходить.** Перед push посмотри, что уходит:

```bash
git diff --name-only @{upstream}...HEAD
```

(ветка ещё не пушилась — `git diff --name-only master...HEAD`). Для каждой строки таблицы, чей
«если» совпал с диффом, проверь, что «то» сделано в этом же push. Не сделано — доделай до push
или скажи Владу явно, что осталось. Правило не совпало — делать ничего не надо.

**Новое правило такого вида** («поменяешь X — не забудь Y») пишется строкой сюда, а не
россыпью по докам: иначе его опять будут вспоминать по одному.

**Напоминание приходит само.** Хук `.claude/hooks/pre-push-reminder.js` (PreToolUse на Bash и
PowerShell, подключён в `.claude/settings.local.json` на машине Влада) видит `git push` в команде
агента и добавляет в его контекст напоминание пройти этот файл. Push он не блокирует и ничего не
спрашивает.

| # | Если в диффе… | …то в этом же push | Откуда правило |
|---|---|---|---|
| 1 | **кэш**: `ICacheService`, `MemoryCacheService`, `CachedJsonExtensions`, `CacheTags`, `CacheValueRules`, константы `Cache-Control`/`CacheControlValue`, TTL (`PayloadTtl`, `*Ttl`), сброс (`InvalidateAllAsync`/`InvalidateTagsAsync`) | тексты `/Admin/Cache` (`Pages/Admin/Cache.cshtml`), карта `WhereOnSite` в `Cache.cshtml.cs`, [admin-pages/cache.md](admin-pages/cache.md); правила или этапы кэша — [plans/cache-tags-plan.md](plans/cache-tags-plan.md) | решение Влада 11.09.2026 |
| 2 | новая страница в `server/Swimm.API/Pages/Admin/**` | MD в `docs/admin-pages/` + строка в его README; пункт сайдбара в `Shared/AdminUi.cs` | корневой CLAUDE.md, «Admin pages map» |
| 3 | новые Tailwind-классы в Admin-разметке или `wwwroot/admin-home.html` | `npm run css:build` в `server/Swimm.API` и закоммичен `wwwroot/css/admin.min.css`; нет `[#hex]/opacity` (CI сверяет бандл байт-в-байт) | корневой CLAUDE.md, «Admin/home CSS» |
| 4 | маршрут витрины (чистый URL) | все три зеркала: `client/src/utils/routes.ts`, `cleanUrlRewrite` в `client/vite.config.js`, rewrite-middleware в `server/Swimm.API/Program.cs` | client/CLAUDE.md, «Чистые URL» |
| 5 | новый `UI_*` в `components/mix/` или крупный общий блок; своя вёрстка вместо компонента | строка в [ui-components.md](ui-components.md) (§1–§4 — компонент, §6 — почему своё) | client/CLAUDE.md, «сначала ищи компонент» |
| 6 | новая публичная таблица (не `Sys_*`) | грант в `server/db/02-grants.sql`; решено, едет ли она в прод-наполнение (`server/db/seed-tables.txt`) | корневой CLAUDE.md, «Database» |
| 7 | закрыт этап фазы | отметка в [ROADMAP.md](ROADMAP.md); открытые решения — в плане фазы (`docs/plans/`) | корневой CLAUDE.md |
| 8 | решение или инцидент по данным («в протоколе одно, в базе другое») | запись в [data-integrity.md](data-integrity.md) — это их единственное место | корневой CLAUDE.md |
| 9 | новый ключевой документ в `docs/` (правило, решение, справочник подсистемы) | строка в `DocsCatalog.Sections` (`Pages/Admin/Shared/DocsCatalog.cs`) — раздел «Документация» админки | решение Влада 11.09.2026, [admin-pages/docs.md](admin-pages/docs.md) |
| 10 | новое правило или решение Влада, которое нельзя потерять | пункт в [important.md](important.md) со ссылкой на источник | решение Влада 11.09.2026 |
