// Скрипт страницы /Admin/AppInsights (Pages/Admin/AppInsights.cshtml, docs/admin-pages/appinsights.md).
// Классический скрипт; esc() и _csrfToken — глобальные из _Layout. Данные — GET /api/admin/appinsights/overview.
// Тоны приходят с сервера готовыми (ok | warning | danger | info | neutral) — пороги здесь НЕ считаются.
// Весь текст из ответа (сообщения исключений и логов — недоверенный текст с прода) — только через esc().
// Tailwind-классы отсюда собирает @source в Styles/admin.css.
(() => {
    const RANGES = ['1h', '24h', '7d'];
    const ROLES = ['web', 'prodadmin'];
    const SECTION_IDS = ['slow', 'errors', 'exceptions', 'background'];

    // Как TileTone в Jobs.cshtml: рамка плитки и цвет числа.
    const TILE_TONE = {
        danger: ['border-admin-danger/60 bg-admin-danger/5', 'text-admin-danger'],
        warning: ['border-admin-warning/60 bg-admin-warning/5', 'text-admin-warning'],
        ok: ['border-admin-border', 'text-admin-success'],
        info: ['border-admin-accent/50', 'text-admin-accent'],
        neutral: ['border-admin-border', 'text-admin-text'],
    };
    const tone = t => TILE_TONE[t] || TILE_TONE.neutral;

    const $ = id => document.getElementById(id);
    const els = {
        conn: $('ai-conn'), process: $('ai-process'), last: $('ai-last'), generated: $('ai-generated'),
        links: $('ai-links'), reasons: $('ai-reasons'), body: $('ai-body'), refresh: $('ai-refresh'),
        range: $('ai-range'), role: $('ai-role'),
    };

    // Период и роль живут в query (?range=&role=), чтобы ссылка открывалась так же; неверное — умолчание.
    const params = new URLSearchParams(location.search);
    const state = {
        range: RANGES.includes(params.get('range')) ? params.get('range') : '24h',
        role: ROLES.includes(params.get('role')) ? params.get('role') : 'web',
        busy: false,
    };
    let seq = 0;

    const fmtTime = iso => iso ? new Date(iso).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' }) : '—';
    const fmtDT = iso => {
        if (!iso) return '—';
        const d = new Date(iso);
        return d.toLocaleDateString('ru-RU') + ' ' + d.toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });
    };
    const fmtMs = v => `${Math.round(v ?? 0).toLocaleString('ru-RU')} мс`;
    const fmtN = n => (n ?? 0).toLocaleString('ru-RU');

    function syncUrl() {
        const q = new URLSearchParams({ range: state.range, role: state.role });
        history.replaceState(null, '', `${location.pathname}?${q}${location.hash}`);
    }

    // Сегментные кнопки: активная — primary, остальные — ghost; на время запроса неактивны.
    function paintControls() {
        for (const [wrap, attr, cur] of [[els.range, 'range', state.range], [els.role, 'role', state.role]]) {
            wrap.querySelectorAll('button').forEach(b => {
                const on = b.dataset[attr] === cur;
                b.classList.toggle('admin-btn-primary', on);
                b.classList.toggle('admin-btn-ghost', !on);
                b.setAttribute('aria-pressed', on ? 'true' : 'false');
                b.disabled = state.busy;
            });
        }
        els.refresh.disabled = state.busy;
    }

    const skeleton = () => `
        <div class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3 mb-6">
            ${Array.from({ length: 8 }, () => `
            <div class="admin-card p-3 animate-pulse border-admin-border">
                <div class="h-3 w-20 rounded bg-admin-border"></div>
                <div class="mt-2 h-7 w-16 rounded bg-admin-border"></div>
                <div class="mt-2 h-3 w-24 rounded bg-admin-border"></div>
            </div>`).join('')}
        </div>
        <div class="text-sm text-admin-text-muted">Загрузка…</div>`;

    // --- Шапка ---
    function renderHead(d) {
        const c = d.connection || {};
        const dot = c.state === 'ok' ? 'bg-admin-success' : c.state === 'failed' ? 'bg-admin-warning' : 'bg-admin-border';
        els.conn.innerHTML = `<span class="inline-block w-2 h-2 rounded-full mr-2 align-middle ${dot}"></span>` +
            `<strong class="text-admin-text">${esc(c.title)}</strong>` +
            (c.state === 'ok' && c.text ? ` <span>— ${esc(c.text)}</span>` : '');

        const p = d.process;
        els.process.innerHTML = p
            ? `Этот процесс: ${p.sending
                ? `шлёт телеметрию как <code>${esc(p.roleName)}</code>`
                : 'телеметрию <strong class="text-admin-text">не шлёт</strong>'}`
            : '';
        els.last.textContent = d.lastTelemetryAgo ? `Последняя запись с прода: ${d.lastTelemetryAgo}` : '';
        // «Кэш 2 мин» — только у прочитанных данных: состояния без данных сервер не кэширует.
        els.generated.textContent = d.generatedAt && c.state === 'ok' ? `данные на ${fmtTime(d.generatedAt)} (кэш 2 мин)` : '';

        const l = d.links;
        const defs = l ? [['Failures', l.failures], ['Performance', l.performance], ['Live metrics', l.liveMetrics],
                          ['Logs', l.logs], ['Overview', l.overview]] : [];
        els.links.innerHTML = defs.filter(x => x[1]).map(([name, url]) =>
            `<a href="${esc(url)}" target="_blank" rel="noopener" class="text-admin-accent hover:underline">${esc(name)} ↗</a>`).join('');
    }

    // --- Карточка состояния без данных / сбой запроса ---
    function copyBlock(text) {
        return `<div class="mt-3 flex items-start gap-3">
            <pre class="m-0 flex-1 min-w-0 overflow-x-auto rounded-md border border-admin-border bg-admin-bg p-3 text-[0.8rem] text-admin-text">${esc(text)}</pre>
            <button type="button" data-copy="${esc(text)}" class="admin-btn admin-btn-ghost py-1 px-2.5 text-xs shrink-0">Скопировать</button>
        </div>`;
    }

    function stateCard(c) {
        const frame = c.state === 'failed' ? 'border-admin-warning/60 bg-admin-warning/5' : 'border-admin-border';
        const heading = c.state === 'failed' ? 'text-admin-warning' : 'text-admin-text';
        return `<div class="admin-card p-6 ${frame}">
            <div class="text-lg font-semibold ${heading}">${esc(c.title)}</div>
            <div class="mt-1 text-sm text-admin-text-muted leading-relaxed">${esc(c.text)}</div>
            ${c.hint ? copyBlock(c.hint) : ''}
        </div>`;
    }

    function failCard(msg) {
        return `<div class="admin-card p-6 border-admin-warning/60 bg-admin-warning/5">
            <div class="text-lg font-semibold text-admin-warning">Не удалось загрузить</div>
            <div class="mt-1 text-sm text-admin-text-muted">${esc(msg)}</div>
            <button type="button" id="ai-retry" class="admin-btn admin-btn-primary mt-3 py-1 px-3">Повторить</button>
        </div>`;
    }

    // --- Плитки и баннер причин ---
    function renderReasons(d) {
        const rs = d.reasons || [];
        if (!rs.length || d.connection?.state !== 'ok') { els.reasons.innerHTML = ''; return; }
        const [frame, text] = tone(d.tone);
        els.reasons.innerHTML = `<div class="admin-card py-3 px-4 mb-4 ${frame}">
            <ul class="m-0 pl-5 text-[0.85rem] ${text}">${rs.map(r => `<li>${esc(r)}</li>`).join('')}</ul></div>`;
    }

    function tiles(list) {
        return `<div id="ai-tiles" class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 gap-3 mb-6 scroll-mt-4">${list.map(t => {
            const [frame, value] = tone(t.tone);
            // Якорь — секция ниже; у «Принято сегодня» и «Последняя телеметрия» секции нет — ведёт к шапке.
            const key = String(t.href || '').replace(/^#/, '');
            const href = SECTION_IDS.includes(key) ? `#${key}` : '#ai-head';
            return `<a href="${esc(href)}" class="admin-card block w-full min-w-0 p-3 text-left no-underline transition-colors hover:border-admin-accent/60 hover:bg-admin-surface-hover ${frame}">
                <div class="text-[11px] font-semibold uppercase tracking-wide text-admin-text-muted">${esc(t.label)}</div>
                <div class="mt-1 break-words text-2xl font-bold leading-tight ${value}">${esc(t.value)}</div>
                <div class="mt-0.5 text-[11px] leading-snug text-admin-text-muted">${esc(t.sub)}</div>
            </a>`;
        }).join('')}</div>`;
    }

    // --- Таблицы ---
    function section(id, title, hint, headers, rows) {
        const table = rows.length
            ? `<div class="overflow-x-auto"><table class="admin-table"><thead><tr>${headers.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead>
               <tbody>${rows.join('')}</tbody></table></div>`
            : '<div class="px-4 py-3 text-sm text-admin-success">Пусто — хорошо.</div>';
        return `<div id="${id}" class="admin-card mb-4 overflow-hidden scroll-mt-4">
            <div class="px-4 pt-3 pb-2"><h2 class="m-0 text-base font-semibold text-admin-accent">${esc(title)}</h2>
            ${hint ? `<div class="text-[11px] text-admin-text-muted mt-0.5">${esc(hint)}</div>` : ''}</div>
            ${table}</div>`;
    }
    const clip = (text, cls = 'max-w-[26rem]') =>
        `<span class="block truncate ${cls}" title="${esc(text)}">${esc(text)}</span>`;
    const when = iso => `<td class="whitespace-nowrap text-xs">${esc(fmtDT(iso))}</td>`;

    function tables(d) {
        const exc = (d.exceptions || []).map(e => `<tr>
            <td class="text-xs">${clip(e.type, 'max-w-[16rem]')}</td>
            <td class="text-xs text-admin-text-muted">${clip(e.message)}</td>
            <td class="text-right font-semibold">${fmtN(e.count)}</td>
            ${when(e.lastAt)}
            <td class="text-xs">${e.operation ? clip(e.operation, 'max-w-[18rem]') : '<span class="text-admin-text-muted">фон</span>'}</td></tr>`);

        const slow = (d.slowOperations || []).map(o => `<tr>
            <td class="text-xs">${clip(o.operation, 'max-w-[26rem]')}</td>
            <td class="text-right">${fmtN(o.count)}</td>
            <td class="text-right whitespace-nowrap">${fmtMs(o.p50Ms)}</td>
            <td class="text-right whitespace-nowrap font-semibold ${o.p95Ms > 1000 ? 'text-admin-warning' : ''}">${fmtMs(o.p95Ms)}</td>
            <td class="text-right ${o.errors5xx > 0 ? 'text-admin-danger font-semibold' : 'text-admin-text-muted'}">${fmtN(o.errors5xx)}</td></tr>`);

        const errs = (d.errors || []).map(e => `<tr>
            <td class="text-xs">${clip(e.operation, 'max-w-[26rem]')}</td>
            <td class="font-semibold ${e.serverError ? 'text-admin-danger' : 'text-admin-text-muted'}">${esc(String(e.code))}</td>
            <td class="text-right ${e.serverError ? 'text-admin-danger font-semibold' : 'text-admin-text-muted'}">${fmtN(e.count)}</td>
            ${when(e.lastAt)}</tr>`);

        const bg = (d.background || []).map(b => `<tr>
            <td class="text-xs">${b.kind === 'exception' ? 'исключение' : 'лог'}</td>
            <td class="text-xs">${clip(b.source, 'max-w-[16rem]')}</td>
            <td class="text-xs text-admin-text-muted">${clip(b.message, 'max-w-[30rem]')}</td>
            <td class="text-right font-semibold">${fmtN(b.count)}</td>
            ${when(b.lastAt)}</tr>`);

        return section('exceptions', 'Частые исключения', 'до 10, частые сверху',
                ['Тип', 'Сообщение', 'Сколько', 'Последний раз', 'Операция'], exc) +
            section('slow', 'Самые медленные операции', 'по p95; жёлтым — выше 1000 мс',
                ['Операция', 'Сколько', 'p50', 'p95', '5xx'], slow) +
            section('errors', 'Ошибки по маршруту и коду', 'ответы 400+, серверные (5xx) первыми',
                ['Операция', 'Код', 'Сколько', 'Последний раз'], errs) +
            section('background', 'Сбои фоновых служб', 'вне запроса: исключения и логи Error+',
                ['Вид', 'Источник', 'Сообщение', 'Сколько', 'Последний раз'], bg);
    }

    function render(d) {
        renderHead(d);
        renderReasons(d);
        els.body.innerHTML = d.connection?.state === 'ok'
            ? tiles(d.tiles || []) + tables(d)
            : stateCard(d.connection || { state: 'failed', title: 'Нет данных', text: '' });
    }

    async function load(refresh) {
        const my = ++seq;
        state.busy = true;
        paintControls();
        syncUrl();
        els.body.innerHTML = skeleton();
        els.reasons.innerHTML = '';
        try {
            const q = new URLSearchParams({ range: state.range, role: state.role });
            if (refresh) q.set('refresh', 'true');
            const res = await fetch(`/api/admin/appinsights/overview?${q}`, { credentials: 'same-origin', cache: 'no-store' });
            if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
            const d = await res.json();
            if (my !== seq) return;
            render(d);
        } catch (err) {
            if (my !== seq) return;
            els.body.innerHTML = failCard(err.message);
        } finally {
            if (my === seq) { state.busy = false; paintControls(); }
        }
    }

    // --- События ---
    els.range.addEventListener('click', e => {
        const b = e.target.closest('button[data-range]');
        if (!b || state.busy || b.dataset.range === state.range) return;
        state.range = b.dataset.range;
        load(false);
    });
    els.role.addEventListener('click', e => {
        const b = e.target.closest('button[data-role]');
        if (!b || state.busy || b.dataset.role === state.role) return;
        state.role = b.dataset.role;
        load(false);
    });
    els.refresh.addEventListener('click', () => { if (!state.busy) load(true); });
    document.addEventListener('click', async e => {
        if (e.target.closest('#ai-retry')) { load(true); return; }
        const copy = e.target.closest('[data-copy]');
        if (!copy) return;
        const text = copy.dataset.copy;
        try { await navigator.clipboard.writeText(text); }
        catch {
            const ta = document.createElement('textarea');
            ta.value = text; document.body.appendChild(ta); ta.select();
            try { document.execCommand('copy'); } catch { /* буфера нет — текст виден в блоке */ }
            ta.remove();
        }
        const old = copy.textContent;
        copy.textContent = 'Скопировано';
        setTimeout(() => { copy.textContent = old; }, 1500);
    });

    load(false);
})();
