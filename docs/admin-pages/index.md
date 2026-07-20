# /Admin — Dashboard

Файлы: `Pages/Admin/Index.cshtml(.cs)`; API: `AdminController` (`/api/admin/*`),
`SwimmersAdminController` (attention-summary).

Что есть:
- статистика (swimmers/results/competitions/clubs) — `#stats-grid`;
- «Требует внимания» `#attention-grid` (скрыт при нулях): сироты и спорные дубли
  пловцов из `GET /api/admin/swimmers/attention-summary`;
- список пользователей с фильтрами (поиск/роль/статус), смена ролей.

План развития: постоянные карточки «Статус данных» (дубли swimmers/clubs, loglig
привязки, discovery imported/new) — `docs/plans/admin-dashboard-status-cards-plan.md`.
