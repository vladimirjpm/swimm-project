# /Admin/Settings — параметры приложения

Файлы: `Pages/Admin/Settings.cshtml`; API `/api/admin/settings`
(`AdminSettingsService` — in-memory, перечитываются фоновыми джобами каждый тик).

Ключевые ключи: DiscoveryEnabled/DiscoveryIntervalHours,
LogligVerifyEnabled/LogligVerifyIntervalHours (дефолт вкл/24ч),
LogligBatchEnabled/LogligBatchPerRun/LogligBatchIntervalHours (дефолт ВЫКЛ/50/24ч).
Scope: admin / livesite / both.

Кнопка «Пересчитать / сбросить кэш» (фаза 7.3 op#3) → `POST /api/admin/cache/invalidate`
(`ICacheService.InvalidateAllAsync` + аудит `cache.invalidate`). Агрегаты (club-summary,
клубные очки, результаты) не персистятся — считаются на чтение и кэшируются в памяти с
TTL, поэтому «пересчёт» = сброс кэша: свежие данные соберутся при следующем публичном
запросе. Полезно после ручных правок результатов / переноса.
