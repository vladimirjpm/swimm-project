# docs/plans — рабочие планы

Здесь живут планы работ, к которым можно вернуться через недели: этапы с критериями приёмки,
контракты API, принятые и **открытые** решения, раздел «как вернуться к работе».

Правила: фазы и их статусы — в [`../ROADMAP.md`](../ROADMAP.md) (single source of truth);
план в этой папке раскрывает **одну** фазу или задачу. Дизайн-материал (инвентарь данных,
блоки UI, footguns) — в `docs/design-handoff-*.md`, не дублируется в планах.

## Активные (не начаты)

| План | Что | Фаза | Ждёт решений |
|---|---|---|---|
| [azure-deploy-plan.md](azure-deploy-plan.md) | деплой на Azure: App Service + Flexible PG, один origin | «Хостинг» | **6** (§3): судьба Static Web App, какая главная, порядок миграций, ветка-триггер, регион, данные с нуля или дамп |
| [entity-pages-plan.md](entity-pages-plan.md) | публичные страницы спортсмена и клуба | 10 | **3** (§4): суммировать ли очки клуба за сезон, поле логотипа в БД, страница у псевдоклубов |
| [records-all-countries-plan.md](records-all-countries-plan.md) | рекорды всех стран + рейтинг + head-to-head | 11 | **3** (§7): все страны или подмножество, расхождение кодов страны, колонка `TimeMs` |

Хендоффы к фазе 10: [спортсмен](../design-handoff-athlete-page.md) ·
[клуб](../design-handoff-club-page.md).

## Выполненные / исторические

| План | Что |
|---|---|
| [admin-dashboard-health-2-plan.md](admin-dashboard-health-2-plan.md) | дашборд «здоровье данных» 2.0 |
| [admin-dashboard-status-cards-plan.md](admin-dashboard-status-cards-plan.md) | карточки статуса на дашборде |
| [import-upsert-plan.md](import-upsert-plan.md) | upsert при переимпорте |
| [relay-surname-truncation-followup.md](relay-surname-truncation-followup.md) | обрезка фамилий в эстафетах |

## Порядок зависимостей между активными

```
Хостинг (Azure) — независим, можно в любой момент
Фаза 10: 10.1 (сезонный агрегат) ──► спортсмен (10.2–10.3)
                                 └─► клуб (10.4–10.7)
Фаза 11: 11.1 (импорт) ──► 11.2 (рейтинг) ──► 11.3 (head-to-head)
         11.1 разблокирует 9.9 (переключатель региона рекордов)
```
