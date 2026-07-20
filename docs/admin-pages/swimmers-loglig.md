# /Admin/Swimmers/Loglig — привязка к loglig.com

Файлы: `Pages/Admin/Swimmers/Loglig.cshtml(.cs — прокидывает Loglig:SeasonId)`;
API `LogligAdminController` (`/api/admin/loglig`): list (query/status/take),
config (searchConfigured), find, set, unlink, batch (take≤200).
Сервисы: `LogligLinkService` (оркестрация), `SerperCandidateSearchProvider` (поиск),
`LogligClient` (карточка), `LogligMatchService` (сверка). План: `docs/loglig-id-plan.md`.

Логика: «Find loglig ID» — поиск по имени через serper.dev → карточки → сверка
(дата+дистанция+стиль+время ±20мс, год рождения); ровно один AutoVerify → привязка
Verified/auto, иначе список кандидатов с кнопкой «Привязать». Ручная привязка ссылкой
(ID извлекает JS, на сервер только int — анти-SSRF). Verified залочен до «Отвязать».
«Батч ×20» — платные Serper-запросы (подтверждение). Ссылки на карточки — с
`?seasonId=` из конфига `Loglig:SeasonId` (без него loglig отдаёт 500!).
Краудсорс-предложения пользователей (Suggested/Rejected) видны в статусах; ночные
джобы: LogligSuggestionVerification (вкл по умолчанию), LogligBatch (выкл).
