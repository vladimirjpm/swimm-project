# /Admin/Categories — категории соревнований

Файлы: `Pages/Admin/Categories/Index.cshtml`, `Edit.cshtml`;
API через `CategoryAdminRepository`.

Категории — бейджи соревнований; участвуют в фильтре на /Admin/Competitions
и в Discovery (присвоение категории при импорте).

## Возрастная лестница (2026-07-31)

| Key | Название | עברית | Бейдж | Таб на сайте |
|---|---|---|---|---|
| `results-kids-team` | Kids (8–11) | ילדים | K | `kids8_11` |
| `results-youth-team` | Young (11–14) | צעירים | Y | `young11_14` |
| `results-junior-results` | Juniors | נוער | J | `juniors` |
| `results-main` | Adults | בוגרים | A | `adults` |
| `results-masters` | Masters | מסטרס | M | `masters` |

Всё остальное (напр. `result-maccabiah`) — кастомная категория: своего канонического
таба нет, соревнование видно в «All» и в отдельном табе категории.

⚠ **Ключи ротировались.** До 2026-07-31 `results-youth-team` означал Kids 8–11, а
`results-junior-results` — 11–14. Миграция `CategoryLadderRenameAndHebrew` сдвинула ключи
по кругу, оставив соревнования в своих возрастных полосах. Поэтому старые ссылки
(`?category=junior`, `?category=young8_11`, `?cat=results-…`) читаются по таблице истории
`LEGACY_CATEGORY_ALIASES` в `client/src/utils/constants/results-categories.ts` — она отражает
значение ключа **на момент ссылки**, а не текущее содержимое БД. Не «чини» её по текущим Key.

## Что зашито в код

Ключи из `Category.ReservedKeys` (все пять выше) переименовать или удалить через админку
нельзя — на них завязаны `Competition.IsMasters` (`results-masters`), выбор канонического
таба в `ResultRepository.CategoryFor` и маппинг `CategoryHelper.CANONICAL_TO_DB_KEY`
на клиенте. Название, иврит, бейдж и порядок меняются свободно.

## Footgun: новая категория не видна в чек-листах импорта/превью

`/api/categories` отдаёт `Cache-Control: public, max-age=300` — рассчитан на публичный сайт.
Чек-листы категорий на `/Admin/Import` и превью `/Admin/Competitions` дёргают тот же
эндпоинт; их `fetch` явно ставит `cache: 'no-store'` (иначе браузер до 5 минут отдаёт
закешированный ответ **из диска**, не доходя до сервера, — серверная инвалидация кэша
после создания категории тут ни при чём). Если завёл новую категорию и она не появилась —
жёсткий Ctrl+Shift+R/новая вкладка снимает вопрос сразу; если где-то завели ещё один
`fetch('/api/categories', …)` без `cache: 'no-store'`, там тот же баг вернётся.

## Поле «Название на иврите» (`NameHe`)

Заполнено в БД и отдаётся в `/api/categories` как `name_he`, но публичный клиент его не
показывает — UI сайта английский. Добавлено про запас, чтобы перевод не пришлось вносить
второй раз при появлении локализации.
