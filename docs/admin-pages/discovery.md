# /Admin/Discovery — автозабор соревнований

Файлы: `Pages/Admin/Discovery/Index.cshtml`; API `/api/admin/discovery`
(`DiscoveryAdminController`: list, preview, import, pdf и др.).
Сервисы: `CompetitionDiscoveryService` + `CompetitionDiscoveryBackgroundService`
(настройки DiscoveryEnabled/DiscoveryIntervalHours), парсер —
`Swimm.Parsing/Discovery/IsrOrgDiscoveryProvider`.

«Входящие» с isr.org.il: `Sys_DiscoveredCompetitions` со статусами (new → imported;
полный набор проверять в коде). Превью распарсенного, присвоение события/категории,
импорт. Фикстуры парсера — `Swimm.Tests/Fixtures/Discovery`.

Кросс-линк с /Admin/Competitions по `OrgCompId`: у импортированной строки название
сматченного соревнования — ссылка «↗» на `/Admin/Competitions?org=<OrgCompId>`. Обратно:
`?org=<OrgCompId>` (переход из справочника) один раз после загрузки скроллит и
подсвечивает строку по `data-org` (если скрыта как ignored — включает «показывать скрытые»).
