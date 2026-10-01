# ПЕРЕДАЧА: редизайн таба Trainings — старт в новом чате (обновлено 01.10.2026, вечер)

Короткая точка входа. Сам план — [trainings-tab-redesign-plan.md](trainings-tab-redesign-plan.md);
здесь только то, что нужно, чтобы продолжить без разведки заново.

## 0. С чего начать новый чат — по порядку

1. **Т7 — принять работу Сонета.** Задание `docs/tasks/trainings-tab-t7-sonnet.md` (вид Lanes:
   участнику план, тренеру доведённый редактор по Р6). Сонет закончил 01.10.2026 вечером, `tsc`
   и `vite build` зелёные (проверено Opus), **живой проверки ещё не было**. Код закоммичен вместе
   с Т2–Т6 (см. §5) — приёмка правит поверх.
   **Выжимка отчёта Сонета** (сам отчёт остался в прошлом чате):
   - новые файлы `components/group-lane-{circle,chip,move-row,savebar,toolbar}.tsx`; переписаны
     `group-lanes.tsx`, `group-lane-editor.tsx`, `group-lane-board.tsx`; `trainings-context.tsx`:
     `lanesDirty` / `setLanesDirty`, `setView` при правках показывает `ConfirmDialog`
     «Discard unsaved changes?» (рисует провайдер — одно место); `lane-plan-section.tsx` передаёт
     `desktop`, `onDirtyChange`, `onPlansChanged`;
   - отклонения: дата — `formatPlanDate` как есть («Wed, Sep 30», не «Wed 30 Sep»; в тулбаре
     свой `shortDay` «Sun 27»); «← Back to Next» остался отдельной строкой над шапкой (две
     строки, а не одна как в 6b); тулбар на десктопе — порталом в узел шапки (`toolbarHost`,
     первый рендер без тулбара); у «Published» фон прозрачный (токена `--deep-live-chip` нет);
     новый план без правок сразу «Unsaved changes · Save», но «Discard?» — только при реальных
     правках; после Save и «Copy from» редактор пересоздаётся (`key` = `${date}:${nonce}`),
     выбор и раскрытый Not today сбрасываются; кнопки «Create plan» / «Copy from» — 40px, `--deep-ow`;
   - сомнения Сонета — проверить глазами: drag-and-drop на десктопе, прозрачный `<input
     type="date">` поверх даты (`showPicker`), липкая панель над COACH TOOLS на телефоне,
     6 дорожек на ~960px (колонка ~100px, имена с многоточием — возможно, порог колонок надо
     снизить или поставить по ширине).
   - приёмка по §5 плана: дифф целиком против задания (решения 1–13), `cd client && npx tsc
     --noEmit -p .` и `npx vite build`, вживую (§4 ниже) тренер и участник, 375 и 1280, обе
     темы: перенос тапом («Move … to», «Lead»), перетаскивание и вставка перед плашкой на
     десктопе, колонки ≤ 6 и сетка > 6 без горизонтальной прокрутки, липкая панель «Unsaved
     changes · Save» → «Saved · draft · Publish» → «Saved · published · Unpublish», «Discard
     unsaved changes?» при уходе (стрелки дат, сегменты, «← Back to Next»), Copy from с
     подтверждением, Not today свёрнут; у участника — «Your lane» крупно и золотая заметка
     «Coach moved you from lane 3 to 2» (перенос в ОПУБЛИКОВАННОМ плане → открыть участником);
   - отметить Т7 ✅ в §4 плана, строку здесь и в `docs/plans/README.md`.
2. **Т8** (метки внимания) → **Т9** (пустые состояния) — каждый через `/delegate`, как Т3–Т7.
3. **Т10** — сам: финальная сверка с прототипом, доки, полный прогон тестов, затем коммит(ы)
   по команде Влада (см. §5 — почти всё ещё не закоммичено).

## 1. Где мы

| Этап | Статус | Что важно знать |
|---|---|---|
| Т0 | ✅ | Р1–Р6 решены (§6 плана); правило модульных секций — §3½ |
| Т1а | ✅ | контракт ленты — §3 «Контракт Т1а»; миграция `AddTrainingKeySet` применена к локальной БД |
| Т1б | ✅ | `next_trainings`, «новые видео», «тебя перенесли» — §3 «Контракт Т1б»; миграция `AddTrainingMarks` применена. **Сервер готов** |
| Т2 | ✅ | каркас — таблица «Каркас Т2» в §3½ |
| Т3 | ✅ | `DeepSheet` (`components/deep/deep-sheet.tsx`), формы `trainings/sheets/`, лайтбокс |
| Т4 | ✅ | лента `trainings/feed/`; `group-sessions.tsx` удалён |
| Т5 | ✅ | Next участника `trainings/next/`, секция `member-next`; контекст `upcoming` (ответы на 3 занятия) |
| Т6 | ✅ | Next тренера `trainings/coach/`, секция `coach-next`, шторка `person`, тост Undo; **нижней панели тренера нет** (на телефоне уже панель каркаса COACH TOOLS) |
| **Т7** | ⏳ **код Сонета закоммичен, ждёт приёмки** (живой проверки не было) | §0 п.1 |
| Т8 | — | метки `use-training-marks.ts` по `marks-spec.md`, `PUT …/sessions/seen` при открытии Trainings, `PUT …/lane-plans/seen` при открытии Lanes; число неразложенных в «Draft · N unassigned» (Т6 пишет пока «Draft plan») |
| Т9 | — | пустые состояния 4a, `club-media.tsx:115` `--deep-scrim` → `--t-scrim` |
| Т10 | — | сам: сверка, доки, тесты |

Подробности каждого сделанного этапа (что поправлено за Сонетом, что проверено) — в задании
этапа `docs/tasks/trainings-tab-t<N>-sonnet.md` и в §4 плана; приёмки Т3–Т6 нашли и
поправили: подвал лайтбокса на десктопе, боковой отступ ленты на телефоне, подпись таба
«trainings · lanes», красную метку на перерыве.

## 2. Что прочитать перед работой

1. [trainings-tab-redesign-plan.md](trainings-tab-redesign-plan.md) — §3 (контракты Т1а/Т1б), §3½
   (модульные секции + таблица «Каркас Т2»), §4, §6 (решения Влада).
2. `!design_handoff/design_handoff_trainings_tab/README.md` и `marks-spec.md`; прототип
   `Trainings Tab.dc.html` — читать вёрстку грепом по секциям (`id="2a"`, `id="1e"`…), в панели
   браузера он открывается статикой без движка.
3. Перед клиентскими этапами — `client/CLAUDE.md` (роли `--t-*`, порталу свой класс темы) и
   `docs/ui-components.md`.
4. Образец задания Сонету — любое `docs/tasks/trainings-tab-t[3-7]-sonnet.md` (разделы:
   контекст, решения, готово, шаги, тесты, проверка, footguns, вне скоупа).

## 3. Разделение работы

- **Opus сам:** Т0–Т2 (сделаны), приёмка каждого этапа, Т10.
- **Сонет через `/delegate`:** Т3–Т9. Задание → отдельный коммит «Docs: задание Sonnet — …» (так
  велит скилл) → агент `sonnet-implementer` в фоне → приёмка: отчёт и «Отклонения», дифф, `tsc`
  + `vite build`, стенд. Мелочь — правлю сам, крупное — назад Сонету.

## 4. Как проверять вживую (рецепт, отработан на Т3–Т6)

- **Стенд**: `preview_start` с именем `stack-5183-api5082-personas` (API :5082 + Vite :5183,
  `--no-build`). Серверный код не менялся с Т1б — пересборка не нужна; поменяешь сервер —
  `dotnet build server/Swimm.API` до старта.
- **Скриншоты — headless gstack `/browse`, не панель браузера** (панель ~280px, эмуляция 1280
  нечитаема). `B="$HOME/.claude/skills/gstack/browse/dist/browse"`; `$B viewport 375x812` /
  `1280x900`; персонаж — `$B goto http://localhost:5183/api/dev/persona?as=utest-coach`
  (`utest-member`, `default` — вернуть Влада); тема — `$B js "localStorage.setItem('swimm-mode','dark')"`
  до `goto` страницы; `$B screenshot --viewport <файл в scratchpad>` и `Read` файла. В `$B js`
  выражения с `await` часто ничего не печатают — результат проверяй вторым вызовом или по базе.
  Кука персонажа у headless своя; у панели браузера — своя: верни обе на `default`.
- **Тестовая группа** `utest-open` (id 285): расписания у неё НЕТ — для Next/перерывов дай
  временно и потом верни `null`:
  ```sql
  update "HubGroups" set "TrainingSchedule"='{"slots":[{"day":2,"start":"20:00","end":"21:00"},{"day":6,"start":"10:00","end":"12:00"},{"day":7,"start":"20:00","end":"21:00"}],"place":"Test pool","pool_type":"25m","usual_lanes":4}' where "Id"=285 and "TrainingSchedule" is null;
  ```
  (через `docker exec swimm-postgres psql -U swimm -d swimm -c '…'`). Тренировка с временами —
  сессия 13 (сет 1 — 4×50, пловец 59912 «ברק יערה» в нём есть). Привязать `utest-member` (user 12)
  к пловцу: `update "Sys_HubGroupUserMembers" set "SwimmerId"=59912 where "HubGroupId"=285 and "UserId"=12`.
  Записи через API из `$B js`: токен — `fetch('/api/antiforgery/token')` → заголовок `X-XSRF-TOKEN`.
- **Уборка после проверки** (база должна вернуться к: 1 тренировка, 0 медиа/ответов/перерывов/
  планов, расписания нет, `KeySetNo` null, привязка 12 → null, `Sys_HubGroupUserSeen` пуста):
  `delete from "Sys_HubGroupTrainingRsvps"|"Sys_HubGroupBreaks"|"Sys_LanePlans" where "HubGroupId"=285`,
  расписание → `null`; затем `$B stop`, `preview_stop`, убить процессы с 5082/5183, проверить порты.

## 5. Ловушки и состояние рабочей копии

- **API Влада на :5078 держит Debug-сборку** → `dotnet build`/`test` падают MSB3027. Чужой процесс
  НЕ убивать. Тесты: `dotnet test server/Swimm.Tests --artifacts-path server/Swimm.Tests/obj/art`
  (папку потом удалить; вне репозитория нельзя — тесты-сторожа ищут `Swimm.sln`). На 01.10 вечером
  :5078 был свободен.
- **Стенд с `--no-build`**; первая навигация после старта часто «denied or failed» — повторить.
- **Всё своё закоммичено 01.10.2026 вечером** (по команде Влада): план `c31f5ee7`; задания
  Сонету Т3–Т7 — `23c7e799`, `1a8e5df0`, `2961c72a`, `179a07d8`, `6568b11f`; сервер Т1а–Т1б —
  `8449bb3e` (тесты 2649/2649); клиент Т2–Т7 + доки — следующий коммит за ним (Т7 в нём НЕ
  принят — см. §0). Не запушено.
- **Чужие файлы Влада в копии — не трогать и не коммитить:** `docs/plans/lane-plans-plan.md`,
  `!design_handoff/…`. Коммитить только свои пути, `git add -A` нельзя.
