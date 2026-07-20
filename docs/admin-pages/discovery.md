# /Admin/Discovery — автозабор соревнований

Файлы: `Pages/Admin/Discovery/Index.cshtml`; API `/api/admin/discovery`
(`DiscoveryAdminController`: list, preview, import, pdf и др.).
Сервисы: `CompetitionDiscoveryService` + `CompetitionDiscoveryBackgroundService`
(настройки DiscoveryEnabled/DiscoveryIntervalHours), парсер —
`Swimm.Parsing/Discovery/IsrOrgDiscoveryProvider`.

«Входящие» с isr.org.il: `Sys_DiscoveredCompetitions` со статусами (new → imported;
полный набор проверять в коде). Превью распарсенного, присвоение события/категории,
импорт. Фикстуры парсера — `Swimm.Tests/Fixtures/Discovery`.
