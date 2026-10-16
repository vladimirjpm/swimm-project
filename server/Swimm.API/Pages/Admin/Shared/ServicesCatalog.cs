namespace Swimm.API.Pages.Admin.Shared;

/// <summary>Ссылка внутри карточки сервиса.</summary>
public sealed record ServiceLink(string Label, string Url);

/// <summary>
/// Внешний сервис проекта: что это, зачем он Владу, где лежит доступ и куда нажимать.
/// <see cref="Access"/> — только «где взять доступ» (какой аккаунт, какая запись в менеджере
/// паролей), никогда сами пароли и ключи: репозиторий публичный.
/// </summary>
public sealed record ServiceEntry(
    string Name,
    string Why,
    IReadOnlyList<ServiceLink> Links,
    string? Access = null,
    string? Cost = null);

public sealed record ServiceGroup(string Title, IReadOnlyList<ServiceEntry> Services);

/// <summary>
/// Все сайты и кабинеты, на которых держится SwimHub, — страница /Admin/Services
/// (docs/admin-pages/services.md). Новый сервис = запись здесь; разметка не меняется.
/// Ссылки — общие (без id подписки и прочих идентификаторов аккаунта), кроме проекта Google,
/// чей id и так есть в документации.
/// </summary>
public static class ServicesCatalog
{
    private const string GoogleProject = "api-project-836477468327";

    public static readonly IReadOnlyList<ServiceGroup> Groups =
    [
        new("Сайт",
        [
            new("SwimHub — прод",
                "Сам сайт. /healthz — жив ли процесс, /readyz — видит ли базу, /version — какой коммит выкачен " +
                "(после деплоя меняется через 1–3 минуты). Админки на проде нет — она здесь, в ProdAdmin.",
                [
                    new("swimhub.co.il", "https://swimhub.co.il"),
                    new("/readyz", "https://swimhub.co.il/readyz"),
                    new("/version", "https://swimhub.co.il/version"),
                    new("sitemap.xml", "https://swimhub.co.il/sitemap.xml"),
                ]),
            new("UptimeRobot — мониторинг (завести)",
                "Каждые 5 минут открывает https://swimhub.co.il/readyz и пишет письмо, если сайт или база не " +
                "отвечают (и когда поднялись). Ловит то, о чём Azure не скажет: упавшую базу, истёкший " +
                "сертификат, слетевший DNS. Бесплатно. ☐ Завести: монитор HTTP(s) на /readyz, интервал 5 минут, " +
                "уведомление на почту.",
                [new("Dashboard", "https://dashboard.uptimerobot.com")],
                Access: "аккаунт UptimeRobot (вход через Google)"),
        ]),

        new("Хостинг — Microsoft Azure",
        [
            new("Azure Portal — группа swimm-rg",
                "Всё железо сайта в одной группе (регион Israel Central): сайт swimhub, план swimhub-plan, " +
                "база swimhub-pg. Перезапуск сайта, логи, переменные окружения, firewall базы. " +
                "Группу VS-clt-… не трогать — это Azure DevOps с привязанной оплатой.",
                [
                    new("Portal", "https://portal.azure.com/#home"),
                    new("Группы ресурсов", "https://portal.azure.com/#browse/resourcegroups"),
                    new("App Services", "https://portal.azure.com/#browse/Microsoft.Web%2Fsites"),
                    new("PostgreSQL", "https://portal.azure.com/#browse/Microsoft.DBforPostgreSQL%2FflexibleServers"),
                ],
                Access: "аккаунт Microsoft Влада; в терминале — az login",
                Cost: "≈ $35/мес (сайт B1 + база B1ms + диск)"),
            new("База swimhub-pg: бэкапы и firewall",
                "Бэкапы — 14 дней, восстановление на любую минуту в НОВЫЙ сервер (docs/deploy.md, «Если сломалось»). " +
                "Firewall: домашний IP + исходящие адреса сайта; сменился домашний IP — ProdAdmin не подключится, " +
                "нужно новое правило.",
                [new("PostgreSQL → swimhub-pg", "https://portal.azure.com/#browse/Microsoft.DBforPostgreSQL%2FflexibleServers")],
                Access: "пароли ролей — менеджер паролей Google, сайт swimhub-pg.postgres.database.azure.com"),
            new("Cost Management",
                "Сколько стоит сайт за месяц. Стоит завести бюджет с письмом (например $50/мес, письмо на 80 %).",
                [new("Расходы", "https://portal.azure.com/#view/Microsoft_Azure_CostManagement/Menu/~/overview")]),
        ]),

        new("Код и выкатка — GitHub",
        [
            new("Репозиторий swimm-project",
                "Код (публичный). Выкатка на прод — workflow «Деплой на Azure App Service» по кнопке, из master. " +
                "Secret scanning и Push protection включены — настоящий ключ запушить не даст.",
                [
                    new("Репозиторий", "https://github.com/vladimirjpm/swimm-project"),
                    new("Деплой (Actions)", "https://github.com/vladimirjpm/swimm-project/actions/workflows/azure-deploy.yml"),
                    new("Pull requests", "https://github.com/vladimirjpm/swimm-project/pulls"),
                    new("Secret scanning", "https://github.com/vladimirjpm/swimm-project/security/secret-scanning"),
                    new("Secrets и variables", "https://github.com/vladimirjpm/swimm-project/settings/secrets/actions"),
                ],
                Access: "аккаунт GitHub vladimirjpm; в терминале — gh auth"),
        ]),

        new("Домен, DNS и почта",
        [
            new("Cloudflare — DNS swimhub.co.il",
                "Записи домена: сайт (A @, CNAME www — только DNS only, серое облако!), почта Resend, проверка " +
                "Google Search Console, SPF/DKIM/DMARC. Входящая почта info@ и privacy@ пересылается в Gmail " +
                "(Email Routing). Записи asuid и google-site-verification не удалять.",
                [new("Dashboard", "https://dash.cloudflare.com")],
                Access: "аккаунт Cloudflare Влада (Free)"),
            new("MyNames — регистратор домена",
                "Где куплен swimhub.co.il: оплачен до 14.10.2031, автопродление и блокировка переноса включены. " +
                "Нужен, только если меняются серверы имён (сейчас — Cloudflare) или владелец домена.",
                [new("mynames.co.il", "https://www.mynames.co.il")],
                Access: "аккаунт MyNames Влада"),
            new("Resend — отправка писем",
                "Письма сайта от noreply@swimhub.co.il: подтверждение почты, сброс пароля, решения по заявкам групп. " +
                "Emails/Logs — дошло ли письмо, отказы, жалобы. Бесплатно 3 000 писем/мес, 100/день.",
                [
                    new("Resend", "https://resend.com/"),
                    new("Emails", "https://resend.com/emails"),
                    new("Домены", "https://resend.com/domains"),
                    new("API-ключи", "https://resend.com/api-keys"),
                ],
                Access: "ключ swimhub-prod — менеджер паролей (resend.com); на проде Email__Smtp__Password"),
            new("Gmail — входящие info@ и privacy@",
                "Сюда приходят письма людей: вопросы, просьбы скрыть пловца из поиска, удалить аккаунт " +
                "(по закону — ответ на запрос доступа в течение 30 дней).",
                [new("Gmail", "https://mail.google.com")]),
        ]),

        new("Google",
        [
            new("Google Cloud — вход через Google",
                "OAuth-клиенты: SwimHub prod (сайт, redirect на swimhub.co.il и azurewebsites) и SwimProjectOAuth2 " +
                "(dev и ProdAdmin, localhost:5078). Branding — что видит человек в окне входа; Audience — " +
                "должно быть In production.",
                [
                    new("Console", $"https://console.cloud.google.com/home/dashboard?project={GoogleProject}"),
                    new("Clients", $"https://console.cloud.google.com/auth/clients?project={GoogleProject}"),
                    new("Branding", $"https://console.cloud.google.com/auth/branding?project={GoogleProject}"),
                    new("Audience", $"https://console.cloud.google.com/auth/audience?project={GoogleProject}"),
                ],
                Access: "секреты — менеджер паролей (console.cloud.google.com)"),
            new("Google Search Console",
                "Как сайт виден в поиске: «Эффективность» — по каким запросам находят и кликают; «Страницы» — " +
                "что проиндексировано и почему нет (страницы детей — noindex, так задумано); «Удаления» — срочно " +
                "спрятать адрес из выдачи. Смотреть раз в неделю-две.",
                [
                    new("Обзор", "https://search.google.com/search-console?resource_id=sc-domain%3Aswimhub.co.il"),
                    new("Sitemaps", "https://search.google.com/search-console/sitemaps?resource_id=sc-domain%3Aswimhub.co.il"),
                    new("Удаления", "https://search.google.com/search-console/removals?resource_id=sc-domain%3Aswimhub.co.il"),
                ]),
            new("Менеджер паролей Google",
                "Все пароли и ключи проекта: роли базы (swimhub-pg…), Google OAuth (console.cloud.google.com), " +
                "Resend (resend.com, swimhub-prod).",
                [new("passwords.google.com", "https://passwords.google.com")]),
        ]),

        new("Источники данных",
        [
            new("loglig.com",
                "Протоколы соревнований федерации: затягивание, промежуточные, стартовые протоколы, привязка " +
                "пловцов к профилям. Ходит туда только локальная админка (ProdAdmin), не прод.",
                [new("loglig.com", "https://loglig.com")]),
            new("isr.org.il — федерация плавания",
                "Официальные результаты и рекорды Израиля; отсюда — список входящих соревнований.",
                [new("isr.org.il", "https://isr.org.il")]),
            new("World Aquatics",
                "Мировые рекорды, юниорские и мастерс, рекорды стран (прогон «по странам» в /Admin/Records).",
                [new("Рекорды", "https://www.worldaquatics.com/swimming/records")]),
            new("serper.dev",
                "Поиск в Google для кандидатов loglig-профилей пловцов. Только в админке; ключ — " +
                "CandidateSearch:ApiKey в appsettings.ProdAdmin.Local.json.",
                [new("serper.dev", "https://serper.dev")]),
        ]),
    ];
}
