// Скрипт страницы Pages/Admin/Swimmers/Index.cshtml. Вынесен из разметки как есть (10.10.2026):
// классический скрипт, не модуль — функции из onclick в разметке остаются глобальными.
// Tailwind-классы отсюда собирает @source в Styles/admin.css.
(function () {
    const API = '/api/admin/swimmers';
    let data = null;
    let dryRunOkPairs = null; // пары, прошедшие dry-run — только их можно применить

    // Deep-link фильтр из дашборда: ?filter=dedup-unsure|dedup-sure|orphans
    const qsFilter = new URLSearchParams(location.search).get('filter');
    let dedupFilter = qsFilter === 'dedup-sure' ? 'sure' : qsFilter === 'dedup-unsure' ? 'unsure' : null;
    const scrollToOrphans = qsFilter === 'orphans';

    async function apiJson(url, opts = {}) {
        opts.headers = { 'X-XSRF-TOKEN': _csrfToken, ...(opts.headers ?? {}) };
        const res = await fetch(url, { credentials: 'same-origin', ...opts });
        let body = null;
        try { body = await res.json(); } catch { }
        if (!res.ok) throw new Error(body?.error ?? `${res.status} ${res.statusText}`);
        return body;
    }

    function render() {
        const tbody = document.getElementById('cand-body');
        const allCands = data.candidates;
        const cands = dedupFilter === 'sure' ? allCands.filter(c => c.sure)
            : dedupFilter === 'unsure' ? allCands.filter(c => !c.sure)
            : allCands;
        document.getElementById('empty-msg').classList.toggle('hidden', cands.length > 0);
        document.getElementById('info').innerHTML =
            `Реальных пловцов: ${data.realSwimmers}; кандидатов: ${cands.length}${dedupFilter ? ` из ${allCands.length}` : ''} (уверенных ${allCands.filter(c => c.sure).length}); сирот: ${data.orphans.length}`
            + (dedupFilter ? ` <button id="btn-clear-filter" class="ml-2 cursor-pointer rounded px-2 py-0.5 text-xs border border-admin-border text-admin-text-muted hover:text-admin-text">показать все ✕</button>` : '');
        if (dedupFilter) {
            document.getElementById('btn-clear-filter').addEventListener('click', () => {
                dedupFilter = null;
                render();
            });
        }

        tbody.innerHTML = cands.map((c, i) => {
            const dataI = allCands.indexOf(c);
            return `
            <tr class="border-b border-white/[.04] hover:bg-white/[.03]">
                <td class="py-2 px-3"><input type="checkbox" class="pair-cb cursor-pointer" data-i="${dataI}"></td>
                <td class="py-2 px-3" dir="rtl" style="text-align:left">${esc(c.canonicalName)} <span class="text-[#546e7a]">#${c.canonicalId}</span></td>
                <td class="py-2 px-3" dir="rtl" style="text-align:left">${esc(c.duplicateName)} <span class="text-[#546e7a]">#${c.duplicateId}</span></td>
                <td class="py-2 px-3 text-[#90a4ae]">${c.birthYear || '—'}</td>
                <td class="py-2 px-3 text-[#90a4ae]" dir="rtl" style="text-align:left">${esc(c.canonicalClub ?? '—')}${c.duplicateClub !== c.canonicalClub ? ' / ' + esc(c.duplicateClub ?? '—') : ''}</td>
                <td class="py-2 px-3 text-[#90a4ae]">${c.canonicalResults}+${c.duplicateResults}</td>
                <td class="py-2 px-3 text-[#90a4ae]">${c.distance}</td>
                <td class="py-2 px-3">${c.sure
                    ? '<span class="inline-block py-0.5 px-2 rounded text-xs font-semibold bg-[rgba(102,187,106,0.2)] text-[#66bb6a]">уверенная</span>'
                    : '<span class="inline-block py-0.5 px-2 rounded text-xs font-semibold bg-[rgba(255,213,79,0.15)] text-[#ffd54f]">спорная</span>'}</td>
                <td class="py-2 px-3"><button class="pair-hide cursor-pointer text-[#546e7a] hover:text-[#ef9a9a]" data-i="${dataI}" title="Не дубли — скрыть пару навсегда">✕</button></td>
            </tr>`;
        }).join('');

        // «Не дубли»: пара уходит в Sys_DedupIgnoredPairs и больше не всплывает.
        [...document.querySelectorAll('.pair-hide')].forEach(btn =>
            btn.addEventListener('click', async () => {
                const c = data.candidates[+btn.dataset.i];
                if (!confirm(`Скрыть пару «${c.canonicalName}» ↔ «${c.duplicateName}» как НЕ дубли? Вернуть можно из блока «Скрытые пары».`)) return;
                try {
                    await apiJson(`${API}/dedup-ignore`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ idA: c.canonicalId, idB: c.duplicateId })
                    });
                    toast('Пара скрыта', 3000);
                    await load();
                } catch (e) { toast('Ошибка: ' + e.message, 5000); }
            }));

        document.getElementById('orphans').innerHTML = data.orphans.length
            ? data.orphans.map(o => `<span dir="rtl">${esc(o.name)}</span> <span class="text-[#546e7a]">#${o.id} (${o.birthYear || '—'}, ${esc(o.club ?? '—')})</span>`).join(' · ')
            : 'нет';

        const btnDel = document.getElementById('btn-delete-orphans');
        btnDel.textContent = `Удалить всех сирот (${data.orphans.length})`;
        btnDel.disabled = data.orphans.length === 0;

        // Любое изменение выбора инвалидирует прошлый dry-run
        [...document.querySelectorAll('.pair-cb')].forEach(cb =>
            cb.addEventListener('change', () => { dryRunOkPairs = null; document.getElementById('btn-apply').disabled = true; }));
    }

    async function load() {
        document.getElementById('info').textContent = 'Ищу кандидатов…';
        try {
            data = await apiJson(`${API}/dedup-candidates`);
            dryRunOkPairs = null;
            document.getElementById('btn-apply').disabled = true;
            document.getElementById('plan-card').classList.add('hidden');
            render();
            await loadIgnored();
            if (scrollToOrphans) {
                const el = document.getElementById('orphans');
                el.scrollIntoView({ behavior: 'smooth', block: 'center' });
                el.style.outline = '2px solid var(--color-admin-accent, #4fc3f7)';
                el.style.outlineOffset = '4px';
                setTimeout(() => { el.style.outline = ''; el.style.outlineOffset = ''; }, 1500);
            }
        } catch (e) { toast('Ошибка: ' + e.message, 5000); }
    }

    async function loadIgnored() {
        const list = await apiJson(`${API}/dedup-ignore`);
        const wrap = document.getElementById('ignored-wrap');
        wrap.classList.toggle('hidden', list.length === 0);
        document.getElementById('ignored-summary').textContent = `Скрытые пары (${list.length})`;
        document.getElementById('ignored-body').innerHTML = list.map(p => `
            <div class="mb-1">
                <span dir="rtl">${esc(p.nameA)}</span> <span class="text-[#546e7a]">#${p.idA}</span> ↔
                <span dir="rtl">${esc(p.nameB)}</span> <span class="text-[#546e7a]">#${p.idB}</span>
                <button class="pair-unhide cursor-pointer ml-2 text-admin-accent hover:underline" data-a="${p.idA}" data-b="${p.idB}">вернуть</button>
            </div>`).join('');
        [...document.querySelectorAll('.pair-unhide')].forEach(btn =>
            btn.addEventListener('click', async () => {
                try {
                    await apiJson(`${API}/dedup-ignore/remove`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ idA: +btn.dataset.a, idB: +btn.dataset.b })
                    });
                    toast('Пара возвращена в кандидаты', 3000);
                    await load();
                } catch (e) { toast('Ошибка: ' + e.message, 5000); }
            }));
    }

    function selectedPairs() {
        return [...document.querySelectorAll('.pair-cb:checked')].map(cb => {
            const c = data.candidates[+cb.dataset.i];
            return { canonicalId: c.canonicalId, duplicateId: c.duplicateId };
        });
    }

    function showPlan(report, applied) {
        const card = document.getElementById('plan-card');
        card.classList.remove('hidden');
        document.getElementById('plan-title').textContent = applied
            ? 'ПРИМЕНЕНО' : 'Dry-run: план (БД не изменена)';
        document.getElementById('plan-body').innerHTML = report.pairs.map(p => `
            <div class="mb-3">
                <b>[${p.status}]</b> canonical #${p.canonicalId} ← duplicate #${p.duplicateId}
                <ul class="list-disc pl-5 text-admin-text-muted">
                    ${p.actions.map(a => `<li>${esc(a)}</li>`).join('')}
                    ${p.conflicts.map(c => `<li class="text-[#ef9a9a]">!! ${esc(c)}</li>`).join('')}
                </ul>
            </div>`).join('');
        card.scrollIntoView({ behavior: 'smooth' });
    }

    document.getElementById('btn-reload').addEventListener('click', load);

    document.getElementById('btn-check-sure').addEventListener('click', () => {
        [...document.querySelectorAll('.pair-cb')].forEach(cb => {
            cb.checked = data.candidates[+cb.dataset.i].sure;
        });
        dryRunOkPairs = null;
        document.getElementById('btn-apply').disabled = true;
    });

    document.getElementById('btn-dryrun').addEventListener('click', async () => {
        const pairs = selectedPairs();
        if (!pairs.length) { toast('Отметь хотя бы одну пару'); return; }
        try {
            const report = await apiJson(`${API}/merge`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pairs, apply: false })
            });
            showPlan(report, false);
            const bad = report.pairs.filter(p => p.status !== 'dry-run');
            dryRunOkPairs = bad.length ? null : pairs;
            document.getElementById('btn-apply').disabled = !dryRunOkPairs;
            if (bad.length) toast(`Есть конфликты/ошибки (${bad.length}) — применение заблокировано`, 6000);
        } catch (e) { toast('Ошибка: ' + e.message, 5000); }
    });

    document.getElementById('btn-apply').addEventListener('click', async () => {
        if (!dryRunOkPairs) return;
        if (!confirm(`Применить склейку ${dryRunOkPairs.length} пар? Дубли будут удалены НЕОБРАТИМО.`)) return;
        try {
            const report = await apiJson(`${API}/merge`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pairs: dryRunOkPairs, apply: true })
            });
            showPlan(report, true);
            toast('Склейка применена', 4000);
            await load();
        } catch (e) { toast('Ошибка применения: ' + e.message, 6000); }
    });

    document.getElementById('btn-delete-orphans').addEventListener('click', async () => {
        const n = data.orphans.length;
        if (!n) return;
        if (!confirm(`Удалить ${n} карточек без единой связи? Пловцы с результатами, тренировками и членствами не могут быть удалены. Действие необратимо.`)) return;
        try {
            const report = await apiJson(`${API}/orphans/delete`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({})
            });
            toast(`Удалено сирот: ${report.deleted}`, 4000);
            await load();
        } catch (e) { toast('Ошибка удаления: ' + e.message, 6000); }
    });

    // ── Качество данных (T3b, deep-link ?filter=no-org-id|no-results): сворачиваемая секция,
    // грузится лениво — только по deep-link или клику на заголовок/кнопку фильтра. ──
    (function () {
        const QAPI = '/api/admin/swimmers/quality';
        const wrap = document.getElementById('quality-wrap');
        const body = document.getElementById('quality-body');
        let qLoaded = null;

        async function loadQuality(filter) {
            qLoaded = filter;
            body.textContent = 'Загружаю…';
            try {
                const r = await fetch(`${QAPI}?filter=${encodeURIComponent(filter)}`, { credentials: 'same-origin' });
                const data = await r.json();
                body.innerHTML = data.items.length === 0
                    ? `<div class="italic py-2">Нет строк (показаны ${data.items.length} из ${data.total}).</div>`
                    : `<div class="mb-1">Показаны ${data.items.length} из ${data.total}</div>
                       <div class="overflow-x-auto"><table class="admin-table">
                        <thead><tr><th>Id</th><th>Фамилия</th><th>Имя</th><th>Год</th><th>Клуб</th></tr></thead>
                        <tbody>${data.items.map(s => `
                            <tr><td>${s.id}</td><td dir="rtl" style="text-align:left">${esc(s.lastName)}</td>
                                <td dir="rtl" style="text-align:left">${esc(s.firstName)}</td><td>${s.birthYear || '—'}</td>
                                <td dir="rtl" style="text-align:left">${esc(s.clubName ?? '—')}</td></tr>`).join('')}</tbody>
                       </table></div>`;
            } catch (e) { body.textContent = 'Ошибка: ' + e.message; qLoaded = null; }
        }

        document.querySelectorAll('[data-qfilter]').forEach(btn =>
            btn.addEventListener('click', () => { wrap.open = true; loadQuality(btn.dataset.qfilter); }));

        // Deep-link с дашборда: ?filter=no-org-id|no-results — раскрыть и загрузить сразу.
        const qf = qsFilter === 'no-org-id' || qsFilter === 'no-results' ? qsFilter : null;
        if (qf) {
            wrap.open = true;
            loadQuality(qf);
            setTimeout(() => wrap.scrollIntoView({ behavior: 'smooth', block: 'start' }), 300);
        }
    })();

    // ── Скрыты из поиска (A10 шаг 1): noindex + вне sitemap.xml по просьбе. Не удаление. ──
    (function () {
        const body = document.getElementById('search-hidden-body');
        const input = document.getElementById('search-hide-id');

        async function setHidden(id, hidden) {
            await apiJson(`${API}/${id}/search-hidden`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ hidden })
            });
        }

        async function loadHidden() {
            try {
                const list = await apiJson(`${API}/search-hidden`);
                document.getElementById('search-hidden-summary').textContent = `Скрыты из поиска (${list.length})`;
                body.innerHTML = list.length === 0
                    ? '<div class="italic py-1">Никого.</div>'
                    : list.map(s => `
                        <div class="mb-1">
                            <a href="/swimmers/${s.id}" target="_blank" rel="noopener" dir="rtl" class="text-admin-text hover:underline">${esc(s.name)}</a>
                            <span class="text-[#546e7a]">#${s.id} (${s.birthYear || '—'}, ${esc(s.clubName ?? '—')})</span>
                            <button class="search-unhide cursor-pointer ml-2 text-admin-accent hover:underline" data-id="${s.id}">вернуть в поиск</button>
                        </div>`).join('');
                [...body.querySelectorAll('.search-unhide')].forEach(btn =>
                    btn.addEventListener('click', async () => {
                        try {
                            await setHidden(+btn.dataset.id, false);
                            toast('Пловец возвращён в поиск', 3000);
                            await loadHidden();
                        } catch (e) { toast('Ошибка: ' + e.message, 5000); }
                    }));
            } catch (e) { body.textContent = 'Ошибка: ' + e.message; }
        }

        document.getElementById('btn-search-hide').addEventListener('click', async () => {
            const id = parseInt(input.value, 10);
            if (!(id > 0)) { toast('Укажи Id пловца', 3000); return; }
            try {
                await setHidden(id, true);
                input.value = '';
                toast(`Пловец #${id} скрыт из поиска`, 3000);
                await loadHidden();
            } catch (e) { toast('Ошибка: ' + e.message, 5000); }
        });

        loadHidden();
    })();

    // ── Клуб пловца (Р95): клуб карточки ставит импорт сам; здесь — поправить и закрепить. ──
    (function () {
        const wrap = document.getElementById('club-wrap');
        const card = document.getElementById('club-card');
        const input = document.getElementById('club-swimmer-id');
        const journal = document.getElementById('club-changes-body');
        const SRC = { import: 'импорт', cli: 'догон', admin: 'руками' };
        const club = (id, name) => id == null ? '—'
            : `<a href="/clubs/${id}" target="_blank" rel="noopener" dir="auto" class="text-admin-text hover:underline">${esc(name ?? '')}</a> <span class="text-[#546e7a]">#${id}</span>`;
        const dt = s => s ? new Date(s).toLocaleString('ru-RU', { dateStyle: 'short', timeStyle: 'short' }) : '';

        async function loadSwimmer(id) {
            try {
                const d = await apiJson(`${API}/${id}/club`);
                card.classList.remove('hidden');
                const differs = d.ruleClubId != null && d.ruleClubId !== d.clubId;
                card.innerHTML = `
                    <div class="mb-2"><a href="/swimmers/${d.swimmerId}" target="_blank" rel="noopener" dir="rtl" class="text-admin-text font-semibold hover:underline">${esc(d.swimmerName)}</a>
                        <span class="text-[#546e7a]">#${d.swimmerId}</span></div>
                    <div class="mb-1">В карточке: ${club(d.clubId, d.clubName)} ${d.clubLocked ? '<span class="ml-1 rounded bg-admin-accent/15 px-1.5 py-0.5 text-[11px] text-admin-accent">🔒 закреплён</span>' : ''}</div>
                    <div class="mb-3 text-admin-text-muted">По правилу (последнее соревнование): ${d.ruleClubId == null ? 'не определить — ничья или нет строк с настоящим клубом' : club(d.ruleClubId, d.ruleClubName)}
                        ${differs ? '<span class="ml-1 text-admin-warning">≠ карточке</span>' : ''}</div>
                    <div class="mb-1 text-[12px] uppercase tracking-wide text-admin-text-muted">Клубы по протоколам</div>
                    <div class="mb-3">${d.periods.length === 0 ? '<span class="italic text-admin-text-muted">личных стартов нет</span>' : d.periods.map(p => `
                        <div class="flex flex-wrap items-baseline gap-2 py-0.5">
                            <span>${club(p.clubId, p.clubName)}</span>
                            <span class="text-admin-text-muted text-[12px]">${p.first} — ${p.last} · строк ${p.rows}</span>
                            <button class="club-pick cursor-pointer text-admin-accent text-[12px] hover:underline" data-club="${p.clubId}">выбрать</button>
                        </div>`).join('')}</div>
                    <div class="flex flex-wrap items-center gap-2">
                        <input id="club-new-id" type="number" min="1" value="${d.clubId ?? ''}" placeholder="Id клуба"
                               class="w-28 rounded-md border border-admin-border bg-transparent px-2 py-1.5 text-admin-text" />
                        <label class="flex items-center gap-1.5 text-admin-text-muted"><input id="club-locked" type="checkbox" ${d.clubLocked ? 'checked' : ''} /> закрепить</label>
                        <button id="btn-club-save" class="admin-btn admin-btn-primary cursor-pointer rounded-md px-3 py-1.5 text-[13px] font-semibold">Сохранить</button>
                    </div>
                    ${d.history.length ? `<div class="mt-3 mb-1 text-[12px] uppercase tracking-wide text-admin-text-muted">История смен</div>${d.history.map(h => `
                        <div class="text-[12px] text-admin-text-muted">${dt(h.changedAt)} · ${esc(SRC[h.source] ?? h.source)}: <span dir="auto">${esc(h.fromClubName ?? '—')}</span> → <span dir="auto" class="text-admin-text">${esc(h.toClubName ?? '—')}</span></div>`).join('')}` : ''}`;

                card.querySelectorAll('.club-pick').forEach(b =>
                    b.addEventListener('click', () => { document.getElementById('club-new-id').value = b.dataset.club; }));
                document.getElementById('btn-club-save').addEventListener('click', async () => {
                    const raw = document.getElementById('club-new-id').value.trim();
                    const clubId = raw === '' ? null : parseInt(raw, 10);
                    const locked = document.getElementById('club-locked').checked;
                    try {
                        await apiJson(`${API}/${d.swimmerId}/club`, {
                            method: 'POST', headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ clubId, locked })
                        });
                        toast('Клуб пловца сохранён', 3000);
                        await loadSwimmer(d.swimmerId);
                        await loadJournal();
                    } catch (e) { toast('Ошибка: ' + e.message, 5000); }
                });
            } catch (e) { card.classList.remove('hidden'); card.textContent = 'Ошибка: ' + e.message; }
        }

        async function loadJournal() {
            try {
                const list = await apiJson(`${API}/club-changes?days=30`);
                document.getElementById('club-summary').textContent = `Клуб пловца (смен за 30 дней: ${list.length})`;
                journal.innerHTML = list.length === 0
                    ? '<div class="italic py-1">Смен нет.</div>'
                    : list.map(c => `
                        <div class="mb-1">
                            <a href="/swimmers/${c.swimmerId}" target="_blank" rel="noopener" dir="rtl" class="text-admin-text hover:underline">${esc(c.swimmerName)}</a>
                            <span class="text-[#546e7a]">#${c.swimmerId}</span>:
                            <span dir="auto">${esc(c.fromClubName ?? '—')}</span> → <span dir="auto" class="text-admin-text">${esc(c.toClubName ?? '—')}</span>
                            <span class="text-[12px]">· ${esc(SRC[c.source] ?? c.source)} · ${dt(c.changedAt)}</span>
                            <button class="club-open cursor-pointer ml-1 text-admin-accent text-[12px] hover:underline" data-id="${c.swimmerId}">править</button>
                        </div>`).join('');
                journal.querySelectorAll('.club-open').forEach(b => b.addEventListener('click', () => {
                    input.value = b.dataset.id;
                    loadSwimmer(+b.dataset.id);
                    card.scrollIntoView({ behavior: 'smooth', block: 'start' });
                }));
            } catch (e) { journal.textContent = 'Ошибка: ' + e.message; }
        }

        document.getElementById('btn-club-load').addEventListener('click', () => {
            const id = parseInt(input.value, 10);
            if (!(id > 0)) { toast('Укажи Id пловца', 3000); return; }
            loadSwimmer(id);
        });

        // Deep-link с дашборда: ?filter=club-changes — раскрыть журнал.
        if (qsFilter === 'club-changes') {
            wrap.open = true;
            setTimeout(() => wrap.scrollIntoView({ behavior: 'smooth', block: 'start' }), 300);
        }
        loadJournal();
    })();

    load();
})();
