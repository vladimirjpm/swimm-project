// Скрипт страницы Pages/Admin/Results/Index.cshtml. Вынесен из разметки как есть (10.10.2026):
// классический скрипт, не модуль — функции из onclick в разметке остаются глобальными.
// Tailwind-классы отсюда собирает @source в Styles/admin.css.
(function () {
    const API = '/api/admin/results/anomalies';
    // Deep-link фильтр с дашборда: ?filter=fk-anomaly|empty-relay — раскрыть секцию и подсветить нужную таблицу.
    const qsFilter = new URLSearchParams(location.search).get('filter');
    const highlight = qsFilter === 'fk-anomaly' ? 'fk'
        : qsFilter === 'empty-relay' ? 'relay'
        : qsFilter === 'no-gender' ? 'nogender'
        : qsFilter === 'duplicate-exact' ? 'dupes' : null;

    let loaded = false;
    async function load() {
        if (loaded) return;
        loaded = true;
        const body = document.getElementById('anomalies-body');
        body.textContent = 'Загружаю…';
        try {
            const r = await fetch(API, { credentials: 'same-origin' });
            const data = await r.json();
            body.innerHTML = `
                <div id="fk-block" class="mb-4">
                    <div class="mb-1 font-semibold text-admin-text">FK-аномалии — показаны ${data.fkAnomalies.items.length} из ${data.fkAnomalies.total}</div>
                    ${data.fkAnomalies.items.length === 0 ? '<div class="italic">Нет.</div>' : `
                    <div class="overflow-x-auto"><table class="admin-table">
                        <thead><tr><th>Result Id</th><th>Swimmer Id</th><th>Club Id</th><th>Competition Id</th></tr></thead>
                        <tbody>${data.fkAnomalies.items.map(x => `
                            <tr><td><a href="/Admin/Results/Edit?id=${x.resultId}" class="text-admin-accent hover:underline">${x.resultId}</a></td>
                                <td>${x.swimmerId}</td><td>${x.clubId}</td><td>${x.competitionId}</td></tr>`).join('')}</tbody>
                    </table></div>`}
                </div>
                <div id="relay-block" class="mb-4">
                    <div class="mb-1 font-semibold text-admin-text">Пустые эстафеты — показаны ${data.emptyRelays.items.length} из ${data.emptyRelays.total}</div>
                    ${data.emptyRelays.items.length === 0 ? '<div class="italic">Нет.</div>' : `
                    <div class="overflow-x-auto"><table class="admin-table">
                        <thead><tr><th>Relay Id</th><th>Competition Id</th></tr></thead>
                        <tbody>${data.emptyRelays.items.map(x => `
                            <tr><td>${x.relayId}</td><td>${x.competitionId}</td></tr>`).join('')}</tbody>
                    </table></div>`}
                </div>
                <div id="dupes-block" class="mb-4">
                    <div class="mb-1 font-semibold text-admin-text">Точные дубликаты — показаны ${data.exactDuplicates.items.length} из ${data.exactDuplicates.total}</div>
                    <div class="mb-1">Совпадает всё: соревнование, пловец, дисциплина, заплыв, дорожка и время.
                        Так не бывает — дорожку в заплыве занимает один пловец один раз, это след импорта.
                        Лечится переимпортом с «удалять лишние». Повтор дисциплины с <b>разным</b> временем
                        (предварительные/финал, перезаплыв) — норма и сюда не попадает.</div>
                    ${data.exactDuplicates.items.length === 0 ? '<div class="italic">Нет.</div>' : `
                    <div class="overflow-x-auto"><table class="admin-table">
                        <thead><tr><th>Result Id</th><th>Копий</th><th>Пловец</th><th>Соревнование</th><th>Дисциплина</th><th>Заплыв/дор.</th><th class="text-right">Время</th></tr></thead>
                        <tbody>${data.exactDuplicates.items.map(x => `
                            <tr><td><a href="/Admin/Results/Edit?id=${x.resultId}" class="text-admin-accent hover:underline">${x.resultId}</a></td>
                                <td>${x.copies}</td>
                                <td dir="rtl" style="text-align:left"><a href="/Admin/Swimmers/Edit?id=${x.swimmerId}" class="text-admin-accent hover:underline">${esc(x.swimmerName)}</a></td>
                                <td dir="rtl" style="text-align:left">${esc(x.competitionName)}</td>
                                <td>${esc(x.distance)} ${esc(x.styleName)}</td>
                                <td>${x.heat}/${x.lane}</td>
                                <td class="text-right tabular-nums">${esc(x.time)}</td></tr>`).join('')}</tbody>
                    </table></div>`}
                </div>
                <div id="nogender-block">
                    <div class="mb-1 font-semibold text-admin-text">Без пола — показаны ${data.noGender.items.length} из ${data.noGender.total}</div>
                    <div class="mb-1">В шапке протокола пола не было (смешанный заплыв, напр. «שומרי שבת»),
                        и у пловца он тоже неизвестен. Проставь пол пловцу — результаты подхватят его при переимпорте.</div>
                    ${data.noGender.items.length === 0 ? '<div class="italic">Нет.</div>' : `
                    <div class="overflow-x-auto"><table class="admin-table">
                        <thead><tr><th>Result Id</th><th>Пловец</th><th>Соревнование</th><th>Дисциплина</th></tr></thead>
                        <tbody>${data.noGender.items.map(x => `
                            <tr><td><a href="/Admin/Results/Edit?id=${x.resultId}" class="text-admin-accent hover:underline">${x.resultId}</a></td>
                                <td dir="rtl" style="text-align:left"><a href="/Admin/Swimmers/Edit?id=${x.swimmerId}" class="text-admin-accent hover:underline">${esc(x.swimmerName)}</a></td>
                                <td dir="rtl" style="text-align:left">${esc(x.competitionName)}</td>
                                <td>${esc(x.distance)} ${esc(x.styleName)}</td></tr>`).join('')}</tbody>
                    </table></div>`}
                </div>`;
            if (highlight) {
                const el = document.getElementById(
                    highlight === 'fk' ? 'fk-block'
                    : highlight === 'relay' ? 'relay-block'
                    : highlight === 'dupes' ? 'dupes-block' : 'nogender-block');
                if (el) {
                    el.style.outline = '2px solid var(--color-admin-accent, #4fc3f7)';
                    el.style.outlineOffset = '4px';
                    setTimeout(() => { el.style.outline = ''; el.style.outlineOffset = ''; }, 2000);
                }
            }
        } catch (e) {
            body.textContent = 'Ошибка: ' + e.message;
            loaded = false;
        }
    }

    document.getElementById('anomalies-wrap').addEventListener('toggle', (e) => {
        if (e.target.open) load();
    });

    if (highlight) {
        document.getElementById('anomalies-wrap').open = true;
        load();
        document.getElementById('anomalies-wrap').scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
})();
