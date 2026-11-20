// /Admin/Jobs — страница: загрузка GET /api/admin/jobs/monitor, опрос раз в 30 с (не при скрытой
// вкладке), шапка, шесть счётчиков, переключатель «Таймлайн | Стена» (localStorage
// admin:jobs:view, ?view= в адресе главнее), клик по службе → панель деталей, стрелки по плиткам.
// Оба вида рисуются из одних данных — переключение без запроса и без перезагрузки.
(function () {
    const M = window.JobsMon;
    const esc = M.esc;
    const URL_MONITOR = '/api/admin/jobs/monitor';
    const POLL_MS = 30000;
    const VIEW_KEY = 'admin:jobs:view';
    const VIEWS = ['timeline', 'wall'];

    const state = { data: null, view: 'timeline', okAt: 0, loading: false, stopped: false };
    const $ = id => document.getElementById(id);

    function initialView() {
        const q = new URLSearchParams(location.search).get('view');
        if (VIEWS.includes(q)) return q;
        try { const s = localStorage.getItem(VIEW_KEY); if (VIEWS.includes(s)) return s; } catch { /* приватный режим */ }
        return 'timeline';
    }

    // ── Счётчики: пилюли в «Таймлайне», плитки в «Стене» ─────────────────────────────────
    const COUNTERS = [
        ['failed', 'упала', 'failed'], ['overdue', 'просрочена', 'overdue'], ['running', 'идут', 'running'],
        ['waiting', 'ждёт тебя', 'waiting'], ['ok', 'в норме', 'ok'], ['off', 'выключены', 'off'],
    ];
    function counters(d, view) {
        const c = d.counters || {};
        if (view === 'wall') {
            return `<ul class="m-0 grid list-none grid-cols-2 gap-2.5 p-0 sm:grid-cols-3 xl:grid-cols-6" aria-label="Сводка">${COUNTERS.map(([k, word, s]) => {
                const n = c[k] || 0;
                const m = M.st(s);
                return `<li class="admin-card flex items-center gap-2.5 px-4 py-3">
                    ${n ? M.statusDot(s, 'size-6') : `<span class="inline-flex size-6 shrink-0 items-center justify-center rounded-full border border-admin-border text-admin-text-muted">${M.icon(m.icon, 'size-3')}</span>`}
                    <span class="font-mono text-[26px] font-bold leading-none ${n ? m.text : 'text-admin-text-muted'}">${n}</span>
                    <span class="text-[13px] text-admin-text">${esc(word)}</span>
                </li>`;
            }).join('')}</ul>`;
        }
        return `<ul class="m-0 flex list-none flex-wrap items-center gap-2 p-0" aria-label="Сводка">${COUNTERS.map(([k, word, s]) => {
            const n = c[k] || 0;
            const m = M.st(s);
            return `<li class="inline-flex h-[26px] items-center gap-1.5 rounded-full bg-admin-surface px-2.5 text-xs">
                <span class="inline-flex ${n ? m.text : 'text-admin-text-muted'}">${M.icon(m.icon)}</span>
                <span class="font-mono font-semibold ${n ? m.text : 'text-admin-text-muted'}">${n}</span><span>${esc(word)}</span>
            </li>`;
        }).join('')}</ul>`;
    }

    function renderHeader(d) {
        $('jobs-env').innerHTML = `${M.icon('monitor', 'size-3.5')}<span>${esc(d.environment)}</span>`;
        $('jobs-env').hidden = false;
        $('jobs-journal').textContent = d.journal === 'prod' ? 'журнал прод-базы — общий с продом' : 'журнал локальной базы';
    }

    function renderView() {
        const d = state.data;
        if (!d) return;
        const view = state.view;
        document.querySelectorAll('[data-view]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.view === view)));
        $('jobs-counters').innerHTML = `<div class="flex flex-wrap items-center gap-x-4 gap-y-2">
            <div class="${view === 'wall' ? 'w-full' : ''}">${counters(d, view)}</div>
            ${view === 'timeline' ? `<div class="ml-auto">${M.timelineLegend()}</div>` : ''}
        </div>`;
        // Фокус внутри вида при перерисовке опросом — вернуть на ту же службу.
        const f = document.activeElement;
        const fKey = f && $('jobs-view').contains(f) && f.dataset ? f.dataset.job : null;
        $('jobs-view').innerHTML = view === 'wall' ? M.renderWall(d) : M.renderTimeline(d);
        if (fKey) { const el = $('jobs-view').querySelector(`[data-job="${CSS.escape(fKey)}"]`); if (el) el.focus({ preventScroll: true }); }
        M.updateDetail(d);
    }

    function setView(v, save) {
        if (!VIEWS.includes(v) || v === state.view) return;
        state.view = v;
        if (save) {
            try { localStorage.setItem(VIEW_KEY, v); } catch { /* приватный режим */ }
            const u = new URL(location.href);
            u.searchParams.set('view', v);
            history.replaceState(null, '', u.pathname + u.search + u.hash);
        }
        renderView();
    }

    function showError(msg) {
        const el = $('jobs-error');
        if (!msg) { el.hidden = true; el.innerHTML = ''; return; }
        el.hidden = false;
        el.innerHTML = `${M.icon('warn', 'size-4')}<span>Не удалось обновить в ${esc(M.hms(Date.now()))}: ${esc(msg)}.
            ${state.okAt ? `Показаны данные от ${esc(M.hms(state.okAt))}; следующая попытка — через 30 с или по «Обновить».` : 'Данных пока нет — попробуйте «Обновить».'}</span>`;
    }

    async function load() {
        if (state.loading || state.stopped) return;
        state.loading = true;
        const btn = $('jobs-refresh');
        btn.setAttribute('aria-busy', 'true');
        try {
            const d = await api(URL_MONITOR);
            if (state.stopped) return;
            state.data = d;
            state.okAt = Date.now();
            showError(null);
            renderHeader(d);
            renderView();
            $('jobs-updated').textContent = `обновлено ${M.hms(state.okAt)} · авто 30 с`;
        } catch (e) {
            showError(e.message || String(e));
            if (!state.data) $('jobs-view').innerHTML = '';
        } finally {
            state.loading = false;
            btn.removeAttribute('aria-busy');
        }
    }

    function openFromHash() {
        const m = /^#job-(.+)$/.exec(location.hash);
        if (m && state.data && state.data.jobs.some(j => j.key === m[1])) {
            const el = $('jobs-view').querySelector(`[data-job="${CSS.escape(m[1])}"]`);
            if (el) el.scrollIntoView({ block: 'center' });
            M.openDetail(m[1], el, state.data);
        }
    }

    // Стрелки по плиткам/дорожкам текущего вида (Tab тоже работает — это кнопки).
    function arrowNav(e) {
        if (!['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight'].includes(e.key)) return;
        const cur = e.target.closest && e.target.closest('#jobs-view [data-job]');
        if (!cur) return;
        const all = [...$('jobs-view').querySelectorAll('[data-job]')];
        const i = all.indexOf(cur);
        const next = all[(e.key === 'ArrowDown' || e.key === 'ArrowRight') ? i + 1 : i - 1];
        if (next) { e.preventDefault(); next.focus(); }
    }

    document.addEventListener('DOMContentLoaded', async () => {
        state.view = initialView();
        document.querySelectorAll('[data-view]').forEach(b => {
            b.setAttribute('aria-pressed', String(b.dataset.view === state.view));
            b.addEventListener('click', () => setView(b.dataset.view, true));
        });
        $('jobs-refresh').addEventListener('click', load);
        document.addEventListener('click', e => {
            const t = e.target.closest('[data-job]');
            if (t && state.data && !t.closest('#job-detail')) {
                M.openDetail(t.dataset.job, t, state.data);
                // Клик по пометке «ⓘ» на дорожке/плитке — панель и сразу справка у значка в её шапке.
                const mark = e.target.closest('[data-note-mark]');
                if (mark && window.AdminNotes) window.AdminNotes.open(mark.dataset.noteMark);
            }
        });
        $('jobs-view').addEventListener('keydown', arrowNav);
        M.setDetailActionHandler(load);

        setInterval(() => { if (!document.hidden) load(); }, POLL_MS);
        document.addEventListener('visibilitychange', () => {
            if (!document.hidden && Date.now() - state.okAt >= POLL_MS) load();
        });
        window.addEventListener('hashchange', openFromHash);

        await load();
        openFromHash();
    });

    // Для проверки раскладок на подставных данных (двухзонный вид ProdAdmin в Development):
    // JobsMon.page.stop(); JobsMon.page.show(fixture) — опрос остановлен, рисуется фикстура.
    M.page = {
        stop() { state.stopped = true; },
        show(d) { state.data = d; renderHeader(d); renderView(); },
    };
})();
