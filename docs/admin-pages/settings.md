# /Admin/Settings — параметры приложения

Файлы: `Pages/Admin/Settings.cshtml`; API `/api/admin/settings`
(`AdminSettingsService` — in-memory, перечитываются фоновыми джобами каждый тик).

Ключевые ключи: DiscoveryEnabled/DiscoveryIntervalHours,
LogligVerifyEnabled/LogligVerifyIntervalHours (дефолт вкл/24ч),
LogligBatchEnabled/LogligBatchPerRun/LogligBatchIntervalHours (дефолт ВЫКЛ/50/24ч).
Scope: admin / livesite / both.
