# Задание Sonnet: PoolType — выпадающий список вместо текстбокса (B1)

Просьба Влада (2026-07-16), источник — `docs/tasks/next-front-plan-2.md`, поток B.
Сейчас на `/Admin/Competitions` поле «Тип бассейна» — свободный текстовый input.
Опечатка («50 m», «50M») создаёт «новое» соревнование, потому что уникальный индекс
`(Name, Date, PoolType)` чувствителен к точному написанию. Надо select с каноническими
значениями + серверная валидация.

Задание самодостаточное. **Не запускай других агентов — делай всё сам.**

## Решения (зафиксированы, не пересматривать)

- Канонический набор — **`25m` и `50m`**. Проверено в проде-локале:
  `SELECT "PoolType", count(*) FROM "Competitions" GROUP BY 1` → только `25m` (311) и
  `50m` (305), пустых нет. Значит никакого «Distinct из БД + дефолты» не нужно —
  **список хардкодим константой**, как уже сделано в Import.cshtml. Динамика из БД
  отклонена: она бы легализовала любой мусор, который туда попадёт.
- Константа живёт **в одном месте** и переиспользуется и формой Competitions, и
  валидацией репозитория. Import.cshtml с его хардкод-`<option>` тоже переводим на неё.
- Поле **обязательное**: пустое значение больше не принимается на Admin/Competitions
  (в БД пустых нет — ничего не сломается). В Import.cshtml опция «—» (пусто)
  **остаётся** — там poolType опционален (парсер может определить сам).
- Валидация — на сервере, в `CompetitionAdminRepository.ValidateAsync`, до записи;
  текст ошибки русский, как у соседних проверок.
- Существующий контракт значений не меняем: клиент (`client/src/utils/interfaces/results.ts`)
  допускает и `'25'`/`'50'` — это не наша забота, не трогать.

## Что уже готово (не переделывать)

- Форма Create/Edit — одна страница (`IsNew => Id is null or 0`), отдельного Create нет:
  `server/Swimm.API/Pages/Admin/Competitions/Edit.cshtml:53-56`
  ```html
  <label class="flex flex-col gap-1">
      <span class="text-xs text-admin-text-muted">Тип бассейна</span>
      <input asp-for="Input.PoolType" class="admin-input" autocomplete="off" placeholder="25m / 50m" />
  </label>
  ```
  PageModel: `Edit.cshtml.cs:28-29` (`[BindProperty] public CompetitionForm Input`),
  `:52` (`public string PoolType { get; set; } = "";`), маппинг `ToForm`/`ToInput` (`:166`, `:179`).
- Образец select, рендерящегося из PageModel, — рядом в `Pages/Admin/Import.cshtml:83-86`
  (`pdf-format` из `Model.ParseFormats`). Хардкод-select бассейна — там же, `:89-96`
  (`id="pdf-pool-type"`, значение уходит в multipart-поле `poolType` на
  `POST /api/admin/parse-pdf`, `Import.cshtml:675-676`).
- Валидация: `server/Swimm.Infrastructure/Repositories/CompetitionAdminRepository.cs:438-466`
  — `private async Task<string?> ValidateAsync(CompetitionInputDto input, int? excludeId)`.
  Возвращает текст ошибки или `null`. Вызовы: `:256` (`CreateAsync`, `excludeId: null`),
  `:273` (`UpdateAsync`, `excludeId: id`). Нормализация перед записью — `:368`
  (`comp.PoolType = (input.PoolType ?? "").Trim();`). Гонку ловит `SaveAsync` (`:295-311`).
- Сущность: `server/Swimm.Domain/Entities/Competition.cs:30-32` — `[MaxLength(5)] public string
  PoolType { get; set; } = string.Empty;`. Индекс: `SwimmDbContext.cs:85`
  `HasIndex(e => new { e.Name, e.Date, e.PoolType }).IsUnique();`

## Шаги

1. Заведи константу в **Application**-слое (доступна и API, и Infrastructure), например
   `server/Swimm.Application/Constants/PoolTypes.cs`:
   ```csharp
   /// <summary>Канонические типы бассейна. Единственный источник правды для формы и валидации.</summary>
   public static class PoolTypes
   {
       public const string Short = "25m";
       public const string Long = "50m";
       public static readonly IReadOnlyList<string> All = new[] { Short, Long };
       public static bool IsValid(string? value) => value is not null && All.Contains(value);
   }
   ```
   (Положи в существующую папку, если в `Swimm.Application` уже есть подходящая —
   сначала посмотри структуру проекта и следуй ей.)
2. `Pages/Admin/Competitions/Edit.cshtml`: заменить `<input asp-for="Input.PoolType">`
   на `<select asp-for="Input.PoolType" class="admin-input">` с `<option>` из
   `PoolTypes.All` (без пустой опции). Для новой записи дефолт — не подставлять
   молча какой-то бассейн вслепую: сделай первую опцию
   `<option value="" disabled selected>— выберите —</option>`, чтобы админ выбрал
   осознанно; при Edit выбранное значение подставляет `asp-for`.
   Класс `admin-input` уже стилизует select (проверь по Import.cshtml).
3. `Edit.cshtml.cs`: на `CompetitionForm.PoolType` повесь `[Required(ErrorMessage = "Выберите тип бассейна")]`.
   Если в PageModel есть паттерн «списки для select» — добавь свойство
   `public IReadOnlyList<string> PoolTypeOptions => PoolTypes.All;` и используй его в разметке.
4. `CompetitionAdminRepository.ValidateAsync`: после проверки имени добавь
   ```csharp
   var pool = (input.PoolType ?? "").Trim();
   if (!PoolTypes.IsValid(pool))
       return $"Тип бассейна должен быть одним из: {string.Join(" / ", PoolTypes.All)}";
   ```
   и переиспользуй уже посчитанный `pool` в проверке уникальности ниже
   (не плоди вторую переменную).
5. `Pages/Admin/Import.cshtml:89-96`: `<option>` для 25m/50m рендерить из `PoolTypes.All`
   (пустую опцию `—` **оставить**). Если для этого нужно свойство в
   `Import.cshtml.cs` — добавь по образцу `ParseFormats`.
6. Проверь, что уникальный индекс и фильтры не ломаются: значения те же строки,
   что уже в БД, — миграция и правки индекса **не нужны**.

## Тесты (обязательно)

Тестов на `CompetitionAdminRepository` в проекте нет — заведи файл
`server/Swimm.Tests/CompetitionAdminRepositoryTests.cs` по образцу
`server/Swimm.Tests/ResultRepositoryTests.cs` (xUnit, EF InMemory, имя БД = `nameof(TestMethod)`,
репозиторий создаётся напрямую). `ValidateAsync` приватный — проверяй **через
`CreateAsync`/`UpdateAsync`**:
- `Create_RejectsInvalidPoolType` — `PoolType = "50 m"` → результат содержит ошибку про
  тип бассейна, запись в БД не создана.
- `Create_RejectsEmptyPoolType` — `PoolType = ""` → ошибка.
- `Create_AcceptsCanonicalPoolType` — `PoolType = "50m"` → успех, запись создана.
Посмотри фактическую сигнатуру/возврат `CreateAsync` и подстройся под неё
(ошибка может возвращаться как строка/Result — не выдумывай, читай код).

## Проверка

```bash
dotnet build server/Swimm.sln --configuration Release
dotnet test server/Swimm.sln --configuration Release
```
Живая проверка страницы (желательно): API на :5079 (`--configuration Release`),
`ASPNETCORE_ENVIRONMENT=Development` → `DevAdminBypass` пускает в админку без логина →
открой `/Admin/Competitions`, Edit любого соревнования: поле «Тип бассейна» — select
с 25m/50m, сохранение работает. **После проверки останови процесс** (иначе build-lock).

## Footguns

- **Tailwind**: после правок классов в `Pages/Admin/**` обязательно
  `cd server/Swimm.API && npm run css:build` и **закоммить** `wwwroot/css/admin.min.css`.
  Если ты не добавлял новых классов (используешь существующий `admin-input`) — всё равно
  прогони `css:build` и посмотри, изменился ли файл.
- **Build-lock** `:5078`: `MSB3027/MSB3021 … "Swimm.API.dll" is locked` → жив прошлый
  `dotnet run`. Собирай в Release, API поднимай на `:5079`, осиротевшее гаси:
  `Get-CimInstance Win32_Process -Filter "Name='Swimm.API.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`
- Clean Architecture: константа — в `Swimm.Application`, Infrastructure и API зависят
  внутрь, не наоборот. В `Swimm.Domain` не тащи (Domain ни от кого не зависит, но
  форма/репозиторий берут из Application — держи один источник).
- Два DbContext: любые EF-команды с `--context SwimmDbContext` (здесь не нужны).
- Комментарии русские, идентификаторы английские.

## Вне скоупа (не делать)

- Никаких миграций, CHECK-констрейнтов в БД, изменений `[MaxLength(5)]`.
- Не трогать клиент (`client/`) и его тип `pool_type: '25' | '50' | '25m' | '50m'`.
- Не менять PoolType у `Record`/`NormativeStandard`/`TrainingSession` и их формы.
- Не делать B2 (массовое удаление сирот) — он не согласован.
- Один коммит в текущей ветке, не пушить.
