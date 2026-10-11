# Памятка «Нужна ли вам группа?» — читают перед созданием группы

Статус: **страница сделана 11.10.2026** — `/help/groups`, ссылка «Do you need a group?» на
`/groups` (гостю — в подсказке «Create your own group», вошедшему — рядом с «+ Create group»).
Черновик заведён 29.09.2026, текст дописан и вычитан со сверкой UI 11.10.2026.

**Текст живёт в файлах, а не здесь** (решение Влада 11.10.2026: длинные тексты — markdown на трёх
языках): `client/src/content/help/groups/{en,ru,he}.md`. Правишь памятку — правь их; иврит
переведён агентом, носителю стоит вычитать. Как устроено хранение — `client/CLAUDE.md`,
«длинный текст — файлом в `src/content`».

Ниже — откуда каждое утверждение, чтобы при правке текст не разошёлся с решениями.

---

## Откуда каждое утверждение (сверять при правке)

| Утверждение | Источник |
|---|---|
| Избранное личное, «семья» — золотое сердечко, `/my-favorites`; лимит 30 пловцов / 3 клуба | память parent-swimmer-link, `docs/plans/hubgroup-club-subscription-plan.md` (П1) |
| Following, роли наружу не отдаются, у клуба Swimmers | Р65 (`docs/data-integrity.md`) |
| Пловец без loglig виден только в группе; Deactivate / Restore / удаление из Inactive | Р71, `docs/hubgroups-architecture.md` §7 |
| Open / By request, карточка Joining в табе Admin (`group-join-policy.tsx`) и селект при создании; в приватную — только заявкой | `docs/hubgroups-architecture.md` §4, §4б; память club-not-a-group-decision |
| «Public group» снят → нет в каталоге, видят только участники | `docs/hubgroups-architecture.md` §4б (при настройке `HubGroupVisibility = perGroup`, дефолт) |
| Everyone 🌐 только у Trusted; Trusted ставит админ сайта; официальная — доверенная сама | Р56, Р65; тексты `TRUSTED_HOW_TO`, `EVERYONE_TRUSTED_ONLY` (`client/src/hooks/useUserMedia.ts`) |
| Жалобы прячут медиа везде | `docs/plans/entity-hero-roles-handoff.md` §3 (порог — настройка, дефолт 3) |
| Лимит 3, официальные в счёт, исключение по человеку, админство не считается | `docs/hubgroups-architecture.md` §3а (политика `any` — дефолт; при `coach`/`admin` текст про «любой» неверен) |
| Заявка — My groups → Edit → Official club status; одобрение: подписка на клуб, « · community», копии из каталога | `docs/hubgroups-architecture.md` §5; `club-request-panel.tsx`, `edit-group-card.tsx` |

⚠ Два места зависят от настроек админки: «создавать может любой» (политика `HubGroupCreationPolicy`)
и «Public group» (`HubGroupVisibility`). Поменяют настройку — поправить текст или брать значение
из `/api/client-config`, как это уже делает подсказка «Create your own group».
