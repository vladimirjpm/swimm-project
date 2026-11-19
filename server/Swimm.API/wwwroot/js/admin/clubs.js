// Скрипт страницы Pages/Admin/Clubs/Index.cshtml. Вынесен из разметки как есть (10.10.2026):
// классический скрипт, не модуль — функции из onclick в разметке остаются глобальными.
// Tailwind-классы отсюда собирает @source в Styles/admin.css.
(function () {
    const API = '/api/admin/clubs';
    let data = null;
    let dryRunOkPairs = null; // пары, прошедшие dry-run без конфликтов/ошибок — только их можно merge'ить
    // Пары, добавленные руками по Id (переименование обычно не кандидат: названия разные).
    // Живут до перезагрузки страницы, «Обновить кандидатов» их не стирает.
    const manualPairs = [];
    // Вид склейки пары (план swimmer-club-change §5, Н2): «canon-dup» → duplicate | rename.
    // Отдельно от строк, чтобы выбор пережил перерисовку списка.
    const pairKinds = new Map();
    const kindOf = (c) => pairKinds.get(`${c.canonicalId}-${c.duplicateId}`) ?? 'duplicate';
    const allRows = () => [...manualPairs, ...(data?.candidates ?? [])];

    // Deep-link фильтр из дашборда: ?filter=dedup-unsure|dedup-sure
    const qsFilter = new URLSearchParams(location.search).get('filter');
    let dedupFilter = qsFilter === 'dedup-sure' ? 'sure' : qsFilter === 'dedup-unsure' ? 'unsure' : null;

    async function apiJson(url, opts = {}) {
        opts.headers = { 'X-XSRF-TOKEN': _csrfToken, ...(opts.headers ?? {}) };
        const res = await fetch(url, { credentials: 'same-origin', ...opts });
        let body = null;
        try { body = await res.json(); } catch { }
        if (!res.ok) throw new Error(body?.error ?? `${res.status} ${res.statusText}`);
        return body;
    }

    function heuristicLabel(c) {
        switch (c.heuristic) {
            case 'same-name': return 'Одинаковое имя';
            case 'name-en': return 'Имя = английское имя клуба';
            case 'suffix': return 'Мусорный хвост';
            case 'word-order': return 'Другой порядок слов';
            case 'swimmers': return `Общие пловцы (${c.sharedSwimmers})`;
            case 'levenshtein': return 'Опечатка';
            case 'manual': return 'Руками';
            default: return c.heuristic;
        }
    }

    function sureBadge(sure) {
        return sure
            ? '<span class="admin-badge border-admin-success/30 bg-admin-success/15 text-[#81c784]">уверенная</span>'
            : '<span class="admin-badge border-admin-border bg-admin-surface-hover text-admin-text-muted">спорная</span>';
    }

    // ── loglig по общим пловцам (Р112): сводка в колонке, список по клику, кнопка проверки ──
    // Открытые списки: «canon-dup» → отчёт сервера (общие пловцы + их клуб на loglig).
    const logligOpen = new Map();
    const pairKey = (c) => `${c.canonicalId}-${c.duplicateId}`;

    const LOGLIG_VERDICT = {
        a: { label: 'канон', cls: 'text-[#81c784]' },
        b: { label: 'дубль', cls: 'text-[#4fc3f7]' },
        both: { label: 'оба', cls: 'text-[#81c784]' },
        other: { label: 'другой', cls: 'text-[#ffb74d]' },
        archive: { label: 'архив', cls: 'text-[#ffb74d]' },
        'no-loglig-id': { label: 'без ID', cls: 'text-[#546e7a]' },
        unchecked: { label: 'не проверено', cls: 'text-[#546e7a]' },
    };

    function logligSummaryHtml(s) {
        const parts = [
            ['a', s.clubA], ['b', s.clubB], ['both', s.both], ['other', s.other],
            ['archive', s.archive], ['no-loglig-id', s.noLogligId], ['unchecked', s.unchecked],
        ].filter(([, n]) => n > 0)
            .map(([v, n]) => `<span class="${LOGLIG_VERDICT[v].cls}">${LOGLIG_VERDICT[v].label} ${n}</span>`);
        // Общие пловцы у loglig стоят в ОБОИХ клубах пары — это два живых клуба, а не дубль.
        const warn = s.clubA > 0 && s.clubB > 0
            ? '<div class="text-[11.5px] text-[#ef9a9a]">loglig видит оба клуба — похоже на два клуба</div>' : '';
        return `<span class="text-[12px]">${parts.join(' · ') || '—'}</span>${warn}`;
    }

    function logligCell(c, i) {
        if (c.heuristic !== 'swimmers' || !c.loglig) return '<span class="text-[#546e7a]">—</span>';
        return `
            <button class="pair-loglig-open cursor-pointer text-left hover:underline" data-i="${i}" title="Список общих пловцов и их клуб на loglig">${logligSummaryHtml(c.loglig)}</button>
            <div><button class="pair-loglig-check admin-btn admin-btn-ghost mt-1 py-0.5 px-2 text-[11.5px] disabled:opacity-40" data-i="${i}"${logligRunActive() ? ' disabled' : ''}
                title="Открыть карточки loglig общих пловцов, которым больше суток (~1 в секунду, в фоне)">${pairButtonText(c)}</button></div>`;
    }

    function fmtDate(iso) {
        if (!iso) return '';
        const d = new Date(iso);
        return `${String(d.getDate()).padStart(2, '0')}.${String(d.getMonth() + 1).padStart(2, '0')}.${d.getFullYear()}`;
    }

    function logligDetailRow(c) {
        const r = logligOpen.get(pairKey(c));
        if (!r) return '';
        const rows = r.swimmers.map(s => {
            const v = LOGLIG_VERDICT[s.verdict] ?? { label: s.verdict, cls: '' };
            const loglig = s.logligClubName
                ? `<span dir="rtl">${esc(s.logligClubName)}</span> <span class="${v.cls}">(${v.label})</span> <span class="text-[#546e7a]">${fmtDate(s.checkedAt)}</span>`
                : `<span class="${v.cls}">${v.label}</span>`;
            return `<tr class="border-b border-white/[.04]">
                <td class="py-1 px-2" dir="rtl" style="text-align:left"><a href="/Admin/Swimmers?club=${s.swimmerId}" target="_blank" class="text-admin-accent hover:underline" title="Клуб пловца в админке">${esc(s.name)}</a>
                    <span class="text-[#546e7a]">#${s.swimmerId}${s.birthYear ? ' · ' + s.birthYear : ''}</span></td>
                <td class="py-1 px-2" dir="rtl" style="text-align:left">${s.cardClubName ? esc(s.cardClubName) + ` <span class="text-[#546e7a]">#${s.cardClubId}</span>` : '<span class="text-[#546e7a]">—</span>'}</td>
                <td class="py-1 px-2" style="text-align:left">${loglig}</td>
                <td class="py-1 px-2">${s.logligUrl ? `<a href="${esc(s.logligUrl)}" target="_blank" rel="noopener" class="text-admin-accent hover:underline">профиль #${s.logligId}</a>` : ''}</td>
            </tr>`;
        }).join('');
        return `<tr class="pair-loglig-detail"><td></td><td colspan="7" class="pb-3 px-3 text-[12.5px] text-admin-text-muted">
            <div class="mb-1">Канон: <span dir="rtl">${esc(r.clubAName)}</span> #${r.clubAId} · дубль: <span dir="rtl">${esc(r.clubBName)}</span> #${r.clubBId} · общих пловцов: ${r.swimmers.length}</div>
            <table class="admin-table"><thead><tr><th>Пловец</th><th>Клуб в карточке</th><th>Клуб на loglig</th><th>loglig</th></tr></thead>
            <tbody>${rows}</tbody></table></td></tr>`;
    }

    // Перерисовка из-за loglig не должна сбрасывать отмеченные пары (и прошедший dry-run).
    function rerender() {
        const rows = allRows();
        const checked = new Set([...document.querySelectorAll('.pair-cb:checked')].map(cb => pairKey(rows[+cb.dataset.i])));
        render();
        [...document.querySelectorAll('.pair-cb')].forEach(cb => { cb.checked = checked.has(pairKey(rows[+cb.dataset.i])); });
    }

    async function toggleLoglig(c) {
        const key = pairKey(c);
        if (logligOpen.has(key)) { logligOpen.delete(key); rerender(); return; }
        try {
            logligOpen.set(key, await apiJson(`${API}/dedup-candidates/loglig?a=${c.canonicalId}&b=${c.duplicateId}`));
            rerender();
        } catch (e) { toast('Ошибка: ' + e.message, 5000); }
    }

    // ── Прогон «Проверить в loglig» (одна пара или все): фоновая задача, страница опрашивает статус ──
    let logligRun = null;      // последний известный статус прогона
    let logligPoll = null;     // таймер опроса
    let logligApplied = 0;     // сколько пройденных пар прогона уже перенесено в строки

    const logligRunActive = () => !!logligRun?.active;
    const runProgress = (st) => st.state === 'queued' ? 'в очереди…'
        : st.swimmersTotal ? `${st.swimmersDone} / ${st.swimmersTotal}` : 'считаю…';

    function pairButtonText(c) {
        const st = logligRun;
        if (!st?.active) return 'Проверить в loglig';
        const mine = st.allPairs
            ? st.currentClubAId === c.canonicalId && st.currentClubBId === c.duplicateId
            : st.clubAId === c.canonicalId && st.clubBId === c.duplicateId;
        return mine ? `Проверяю ${runProgress(st)}` : 'Проверить в loglig';
    }

    // Подписи кнопок — на месте, без перерисовки таблицы (опрос раз в 1,5 с).
    function updateLogligButtons() {
        const rows = allRows();
        document.querySelectorAll('.pair-loglig-check').forEach(btn => {
            btn.textContent = pairButtonText(rows[+btn.dataset.i]);
            btn.disabled = logligRunActive();
        });
        const all = document.getElementById('btn-loglig-all');
        all.disabled = logligRunActive();
        all.textContent = logligRunActive()
            ? `loglig: ${runProgress(logligRun)}${logligRun.allPairs ? ` · пар ${logligRun.pairsDone} / ${logligRun.pairsTotal || '…'}` : ''}`
            : 'Проверить в loglig все пары';
    }

    async function startLogligRun(url, body) {
        try {
            const res = await fetch(url, {
                method: 'POST', credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': _csrfToken },
                body: body ? JSON.stringify(body) : null,
            });
            let st = null;
            try { st = await res.json(); } catch { }
            if (res.status === 409) toast('Проверка в loglig уже идёт — показываю её ход', 4000);
            else if (!res.ok) throw new Error(st?.error ?? `${res.status} ${res.statusText}`);
            attachLogligRun(st);
        } catch (e) { toast('Ошибка: ' + e.message, 6000); }
    }

    function attachLogligRun(st) {
        if (logligRun?.runId !== st.runId) logligApplied = 0;
        applyLogligRun(st);
        if (st.active && !logligPoll) logligPoll = setInterval(pollLogligRun, 1500);
    }

    async function pollLogligRun() {
        try { applyLogligRun(await apiJson(`${API}/dedup-candidates/loglig/runs/${logligRun.runId}`)); }
        catch (e) {
            clearInterval(logligPoll); logligPoll = null;
            logligRun = null; updateLogligButtons();
            toast('Ошибка проверки loglig: ' + e.message, 6000);
        }
    }

    // Пройденные пары → свежие сводки в строках (и открытые списки перечитываются).
    async function applyLogligRun(st) {
        const fresh = st.pairs.slice(logligApplied);
        logligApplied = st.pairs.length;
        const wasActive = logligRunActive();
        logligRun = st;
        if (fresh.length) {
            for (const p of fresh) {
                const c = data?.candidates.find(x => x.canonicalId === p.clubAId && x.duplicateId === p.clubBId);
                if (c) c.loglig = p.summary;
                const key = `${p.clubAId}-${p.clubBId}`;
                if (logligOpen.has(key)) {
                    try { logligOpen.set(key, await apiJson(`${API}/dedup-candidates/loglig?a=${p.clubAId}&b=${p.clubBId}`)); } catch { }
                }
            }
            rerender();
        }
        updateLogligButtons();
        if (!st.active) {
            clearInterval(logligPoll); logligPoll = null;
            if (!fresh.length) rerender();
            if (wasActive) toast(st.state === 'failed' ? `Проверка loglig упала: ${st.error}`
                : !st.swimmersTotal ? 'Все ответы loglig свежие (моложе суток)'
                : `loglig: пар ${st.pairsDone}, открыто карточек ${st.swimmersDone}${st.failed ? `, не открылось ${st.failed}` : ''}`, 5000);
        }
    }

    // После загрузки страницы — подхватить идущий прогон (страницу могли перезагрузить посреди).
    async function resumeLogligRun() {
        try {
            const res = await fetch(`${API}/dedup-candidates/loglig/runs/current`, { credentials: 'same-origin' });
            if (res.status !== 200) return;
            const st = await res.json();
            if (st.active) attachLogligRun(st);
        } catch { }
    }

    document.getElementById('btn-loglig-all').addEventListener('click', () =>
        startLogligRun(`${API}/dedup-candidates/loglig/check-all`, null));

    function checkLoglig(c) {
        return startLogligRun(`${API}/dedup-candidates/loglig/check`, { idA: c.canonicalId, idB: c.duplicateId });
    }

    function render() {
        const tbody = document.getElementById('cand-body');
        const allCands = data.candidates;
        const rows = allRows();
        const cands = manualPairs.concat(dedupFilter === 'sure' ? allCands.filter(c => c.sure)
            : dedupFilter === 'unsure' ? allCands.filter(c => !c.sure)
            : allCands);
        document.getElementById('empty-msg').classList.toggle('hidden', cands.length > 0);
        document.getElementById('info').innerHTML =
            `Реальных клубов: ${data.realClubs}; кандидатов: ${cands.length}${dedupFilter ? ` из ${allCands.length}` : ''} (уверенных ${allCands.filter(c => c.sure).length})`
            + (dedupFilter ? ` <button id="btn-clear-filter" class="ml-2 cursor-pointer rounded px-2 py-0.5 text-xs border border-admin-border text-admin-text-muted hover:text-admin-text">показать все ✕</button>` : '');
        if (dedupFilter) {
            document.getElementById('btn-clear-filter').addEventListener('click', () => {
                dedupFilter = null;
                render();
            });
        }

        tbody.innerHTML = cands.map((c) => {
            const i = rows.indexOf(c);
            const kind = kindOf(c);
            return `
            <tr class="border-b border-white/[.04] hover:bg-white/[.03]">
                <td class="py-2 px-3"><input type="checkbox" class="pair-cb cursor-pointer" data-i="${i}"></td>
                <td class="py-2 px-3" dir="rtl" style="text-align:left">
                    <a href="/Admin/Clubs/Edit?id=${c.canonicalId}" target="_blank" class="text-admin-accent hover:underline" title="Открыть / переименовать">${esc(c.canonicalName)}</a>${c.canonicalNameEn ? ' <span class="text-[#546e7a]">(' + esc(c.canonicalNameEn) + ')</span>' : ''}
                    <span class="text-[#546e7a]">#${c.canonicalId}</span> · <span class="text-[#90a4ae]">рез. ${c.canonicalResults}</span>
                </td>
                <td class="py-2 px-3" dir="rtl" style="text-align:left">
                    <a href="/Admin/Clubs/Edit?id=${c.duplicateId}" target="_blank" class="text-admin-accent hover:underline" title="Открыть / переименовать">${esc(c.duplicateName)}</a>${c.duplicateNameEn ? ' <span class="text-[#546e7a]">(' + esc(c.duplicateNameEn) + ')</span>' : ''}
                    <span class="text-[#546e7a]">#${c.duplicateId}</span> · <span class="text-[#90a4ae]">рез. ${c.duplicateResults}</span>
                </td>
                <td class="py-2 px-3 text-[#90a4ae]">${esc(heuristicLabel(c))}</td>
                <td class="py-2 px-3">
                    <select class="pair-kind admin-input" data-i="${i}" title="Дубль — один клуб двумя названиями; Переименование — клуб сменил название (старое уйдёт в историю клуба)">
                        <option value="duplicate"${kind === 'duplicate' ? ' selected' : ''}>Дубль</option>
                        <option value="rename"${kind === 'rename' ? ' selected' : ''}>Переименование</option>
                    </select>
                </td>
                <td class="py-2 px-3">${c.manual ? '' : sureBadge(c.sure)}</td>
                <td class="py-2 px-3">${logligCell(c, i)}</td>
                <td class="py-2 px-3"><button class="pair-hide cursor-pointer text-[#546e7a] hover:text-[#ef9a9a]" data-i="${i}" title="${c.manual ? 'Убрать пару из списка' : 'Не дубли — скрыть пару навсегда'}">✕</button></td>
            </tr>${logligDetailRow(c)}`;
        }).join('');

        [...document.querySelectorAll('.pair-loglig-open')].forEach(btn =>
            btn.addEventListener('click', () => toggleLoglig(rows[+btn.dataset.i])));
        [...document.querySelectorAll('.pair-loglig-check')].forEach(btn =>
            btn.addEventListener('click', () => checkLoglig(rows[+btn.dataset.i])));

        // Любое изменение выбора инвалидирует прошлый dry-run
        [...document.querySelectorAll('.pair-cb')].forEach(cb =>
            cb.addEventListener('change', () => { dryRunOkPairs = null; document.getElementById('btn-merge').disabled = true; }));

        // Вид склейки — часть пары: смена вида тоже инвалидирует dry-run.
        [...document.querySelectorAll('.pair-kind')].forEach(sel =>
            sel.addEventListener('change', () => {
                const c = rows[+sel.dataset.i];
                pairKinds.set(`${c.canonicalId}-${c.duplicateId}`, sel.value);
                dryRunOkPairs = null;
                document.getElementById('btn-merge').disabled = true;
            }));

        // «Не дубли»: пара уходит в Sys_DedupIgnoredPairs и больше не всплывает.
        // Пара, добавленная руками, просто убирается из списка.
        [...document.querySelectorAll('.pair-hide')].forEach(btn =>
            btn.addEventListener('click', async () => {
                const c = rows[+btn.dataset.i];
                if (c.manual) {
                    manualPairs.splice(manualPairs.indexOf(c), 1);
                    dryRunOkPairs = null;
                    document.getElementById('btn-merge').disabled = true;
                    render();
                    return;
                }
                const ok = await adminConfirm({
                    title: 'Скрыть пару как «не дубли»?',
                    message: `«${c.canonicalName}» ↔ «${c.duplicateName}» больше не будет предлагаться. Вернуть можно из блока «Скрытые пары».`,
                    confirmText: 'Скрыть',
                });
                if (!ok) return;
                try {
                    await apiJson(`${API}/dedup-ignore`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ idA: c.canonicalId, idB: c.duplicateId })
                    });
                    toast('Пара скрыта', 3000);
                    await load();
                } catch (e) { toast('Ошибка: ' + e.message, 5000); }
            }));
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

    // keepStatus — не прятать строку статуса (после apply отчёт должен остаться на экране).
    async function load(keepStatus) {
        document.getElementById('info').textContent = 'Ищу кандидатов…';
        try {
            data = await apiJson(`${API}/dedup-candidates`);
            dryRunOkPairs = null;
            document.getElementById('btn-merge').disabled = true;
            if (!keepStatus) document.getElementById('status-line').classList.add('hidden');
            render();
            await loadIgnored();
        } catch (e) { toast('Ошибка: ' + e.message, 5000); }
    }

    function selectedPairs() {
        const rows = allRows();
        return [...document.querySelectorAll('.pair-cb:checked')].map(cb => {
            const c = rows[+cb.dataset.i];
            return { canonicalId: c.canonicalId, duplicateId: c.duplicateId, kind: kindOf(c) };
        });
    }

    function showReport(report) {
        const line = document.getElementById('status-line');
        line.classList.remove('hidden');
        document.getElementById('status-title').textContent = report.dryRun
            ? 'Dry-run: план (БД не изменена)' : 'ПРИМЕНЕНО';

        const merged = report.pairs.filter(p => p.status === 'merged').length;
        const dryRunN = report.pairs.filter(p => p.status === 'dry-run').length;
        const conflict = report.pairs.filter(p => p.status === 'conflict').length;
        const error = report.pairs.filter(p => p.status === 'error').length;
        document.getElementById('status-summary').textContent =
            `Всего пар: ${report.pairs.length}` +
            (merged ? `; merged: ${merged}` : '') +
            (dryRunN ? `; dry-run: ${dryRunN}` : '') +
            (conflict ? `; conflict: ${conflict}` : '') +
            (error ? `; error: ${error}` : '');

        document.getElementById('status-body').innerHTML = report.pairs.map(p => `
            <details class="mb-2">
                <summary class="cursor-pointer"><b>[${esc(p.status)}]</b> canonical #${p.canonicalId} ← duplicate #${p.duplicateId} · ${p.kind === 'rename' ? 'переименование' : 'дубль'}</summary>
                <ul class="list-disc pl-5 text-admin-text-muted">
                    ${p.actions.map(a => `<li>${esc(a)}</li>`).join('')}
                    ${p.conflicts.map(c => `<li class="text-[#ef9a9a]">!! ${esc(c)}</li>`).join('')}
                </ul>
            </details>`).join('');
        line.scrollIntoView({ behavior: 'smooth' });
    }

    document.getElementById('btn-reload').addEventListener('click', () => load());

    // Пара руками по Id: переименование (Hapoel → Aluma) среди кандидатов обычно не всплывает —
    // названия разные. Имена клубов покажет dry-run первой строкой отчёта.
    document.getElementById('btn-manual-add').addEventListener('click', () => {
        const canon = +document.getElementById('manual-canon').value;
        const dup = +document.getElementById('manual-dup').value;
        const kind = document.getElementById('manual-kind').value;
        if (!canon || !dup || canon === dup) { toast('Нужны два разных Id: канон и дубль'); return; }
        // Пара уже среди кандидатов (у Hapoel/Aluma — «общие пловцы») — не дублируем строку, а
        // ставим ей выбранный вид и отмечаем её.
        const existing = allRows().find(c => c.canonicalId === canon && c.duplicateId === dup);
        if (!existing) {
            manualPairs.push({
                canonicalId: canon, canonicalName: `#${canon}`, canonicalResults: '?',
                duplicateId: dup, duplicateName: `#${dup}`, duplicateResults: '?',
                heuristic: 'manual', sure: false, manual: true,
            });
        }
        pairKinds.set(`${canon}-${dup}`, kind);
        dryRunOkPairs = null;
        document.getElementById('btn-merge').disabled = true;
        const checked = new Set(selectedPairs().map(p => `${p.canonicalId}-${p.duplicateId}`));
        checked.add(`${canon}-${dup}`);
        if (existing && dedupFilter && !(dedupFilter === 'sure' ? existing.sure : !existing.sure)) dedupFilter = null;
        render();
        const rows = allRows();
        [...document.querySelectorAll('.pair-cb')].forEach(cb => {
            const c = rows[+cb.dataset.i];
            cb.checked = checked.has(`${c.canonicalId}-${c.duplicateId}`);
        });
        if (existing) toast('Пара уже среди кандидатов — отмечена, вид выставлен', 3000);
    });

    document.getElementById('btn-check-sure').addEventListener('click', () => {
        const rows = allRows();
        [...document.querySelectorAll('.pair-cb')].forEach(cb => {
            cb.checked = !!rows[+cb.dataset.i].sure;
        });
        dryRunOkPairs = null;
        document.getElementById('btn-merge').disabled = true;
    });

    document.getElementById('btn-dryrun').addEventListener('click', async () => {
        const pairs = selectedPairs();
        if (!pairs.length) { toast('Отметь хотя бы одну пару'); return; }
        try {
            const report = await apiJson(`${API}/merge`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pairs, apply: false })
            });
            showReport(report);
            const bad = report.pairs.filter(p => p.status !== 'dry-run');
            dryRunOkPairs = bad.length ? null : pairs;
            document.getElementById('btn-merge').disabled = !dryRunOkPairs;
            if (bad.length) toast(`Есть конфликты/ошибки (${bad.length}) — merge заблокирован`, 6000);
        } catch (e) { toast('Ошибка: ' + e.message, 5000); }
    });

    document.getElementById('btn-merge').addEventListener('click', async () => {
        if (!dryRunOkPairs) return;
        const ok = await adminConfirm({
            title: 'Применить склейку клубов?',
            message: `Будет применена склейка ${dryRunOkPairs.length} пар(ы) — НЕОБРАТИМО: связи дублей переедут на канон, дубли станут надгробиями.`
                + (dryRunOkPairs.some(p => p.kind === 'rename') ? ' У переименований старое название уйдёт в историю клуба.' : ''),
            confirmText: 'Merge',
            danger: true,
        });
        if (!ok) return;
        try {
            const report = await apiJson(`${API}/merge`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pairs: dryRunOkPairs, apply: true })
            });
            showReport(report);
            toast('Склейка применена', 4000);
            await load(true);
        } catch (e) { toast('Ошибка применения: ' + e.message, 6000); }
    });

    // ── Качество данных (T3b, deep-link ?filter=no-swimmers|no-country): сворачиваемая секция,
    // грузится лениво — только по deep-link или клику на заголовок/кнопку фильтра. ──
    (function () {
        const QAPI = '/api/admin/clubs/quality';
        const wrap = document.getElementById('quality-wrap');
        const body = document.getElementById('quality-body');
        const delAllBtn = document.getElementById('btn-del-empty');

        async function loadQuality(filter) {
            body.textContent = 'Загружаю…';
            try {
                const r = await fetch(`${QAPI}?filter=${encodeURIComponent(filter)}`, { credentials: 'same-origin' });
                const data = await r.json();
                // Удалять предлагаем только в no-swimmers: там строки пусты по определению
                // (0 пловцов и 0 результатов). В no-country клубы живые — им нужна страна, не нож.
                const canDelete = filter === 'no-swimmers';
                body.innerHTML = data.items.length === 0
                    ? `<div class="italic py-2">Нет строк (показаны ${data.items.length} из ${data.total}).</div>`
                    : `<div class="mb-1">Показаны ${data.items.length} из ${data.total}</div>
                       <div class="overflow-x-auto"><table class="admin-table">
                        <thead><tr><th>Id</th><th>Название</th><th>EN</th><th>CountryId</th>${canDelete ? '<th></th>' : ''}</tr></thead>
                        <tbody>${data.items.map(c => `
                            <tr data-club="${c.id}"><td>${c.id}</td>
                                <td dir="rtl" style="text-align:left"><a href="/Admin/Clubs/Edit?id=${c.id}" class="text-admin-accent hover:underline">${esc(c.name)}</a></td>
                                <td>${esc(c.nameEn ?? '')}</td><td>${c.countryId ?? '—'}</td>${canDelete
                                    ? `<td><button class="club-del cursor-pointer text-[#546e7a] hover:text-[#ef9a9a]" data-id="${c.id}" data-name="${esc(c.name)}" title="Удалить пустой клуб">✕</button></td>`
                                    : ''}</tr>`).join('')}</tbody>
                       </table></div>`;

                if (canDelete) [...body.querySelectorAll('.club-del')].forEach(btn =>
                    btn.addEventListener('click', () => deleteEmpty(btn, filter)));

                // «Удалить все» — только в no-swimmers и только когда есть что удалять.
                // Считает по data.total: сервер чистит ВЕСЬ список, а не видимые топ-200.
                delAllBtn.classList.toggle('hidden', !canDelete || data.total === 0);
                delAllBtn.textContent = `Удалить все пустые (${data.total})`;
                delAllBtn.dataset.total = data.total;
            } catch (e) { body.textContent = 'Ошибка: ' + e.message; }
        }

        async function deleteEmpty(btn, filter) {
            const id = btn.dataset.id;
            const ok = await adminConfirm({
                title: 'Удалить пустой клуб?',
                message: `«${btn.dataset.name}» (#${id}) будет удалён из справочника НЕОБРАТИМО. Это безопасно только потому, что на клубе нет ни пловцов, ни результатов — сервер перепроверит это ещё раз.`,
                confirmText: 'Удалить',
                danger: true,
            });
            if (!ok) return;
            try {
                await apiJson(`${API}/${id}`, { method: 'DELETE' });
                toast(`Клуб #${id} удалён`, 3000);
                await loadQuality(filter);
            } catch (e) { toast('Ошибка: ' + e.message, 6000); }
        }

        delAllBtn.addEventListener('click', async () => {
            const total = +delAllBtn.dataset.total || 0;
            const ok = await adminConfirm({
                title: 'Удалить все пустые клубы?',
                message: `Будут удалены ВСЕ ${total} клуб(ов) без пловцов и результатов — НЕОБРАТИМО. Клубы с избранным или заявкой на статус останутся, сервер их пропустит и покажет причину.`,
                confirmText: 'Удалить все',
                danger: true,
                requireText: 'удалить',
            });
            if (!ok) return;
            try {
                const res = await apiJson(`${API}/delete-empty`, { method: 'POST' });
                toast(`Удалено ${res.deleted.length}, пропущено ${res.skipped.length}`, 5000);
                await loadQuality('no-swimmers');
                // После перерисовки списка — иначе loadQuality затрёт отчёт.
                if (res.skipped.length) body.insertAdjacentHTML('afterbegin',
                    `<div class="mb-2 text-[#ef9a9a]">Пропущены:<br>${res.skipped.map(esc).join('<br>')}</div>`);
            } catch (e) { toast('Ошибка: ' + e.message, 6000); }
        });

        document.querySelectorAll('[data-qfilter]').forEach(btn =>
            btn.addEventListener('click', () => { wrap.open = true; loadQuality(btn.dataset.qfilter); }));

        // Deep-link с дашборда: ?filter=no-swimmers|no-country — раскрыть и загрузить сразу.
        const qf = qsFilter === 'no-swimmers' || qsFilter === 'no-country' ? qsFilter : null;
        if (qf) {
            wrap.open = true;
            loadQuality(qf);
            setTimeout(() => wrap.scrollIntoView({ behavior: 'smooth', block: 'start' }), 300);
        }
    })();

    load().then(resumeLogligRun);
})();
