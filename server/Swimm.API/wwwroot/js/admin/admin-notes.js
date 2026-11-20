// Справки админки — значок «ⓘ» с попапом (docs/admin-pages/admin-notes.md). Подключён в общем
// каркасе _Layout, работает на любой странице /Admin/*.
//
//   <span data-note="job.loglig-batch"></span>       → кнопка «ⓘ», по клику попап со справкой
//   <span data-note-mark="job.loglig-batch"></span>  → только пометка «ⓘ» без кнопки — для мест,
//        которые сами кнопка (дорожка «Таймлайна», плитка «Стены»): кнопка в кнопке — невалидный
//        HTML, а клик по такому месту и так открывает панель, где стоит настоящий значок.
//
// Значок появляется ТОЛЬКО если справка с таким ключом есть: список ключей (GET /api/admin/notes)
// грузится один раз на страницу. Элементы, дорисованные позже (Jobs перерисовывается опросом),
// подхватывает MutationObserver; обработчик клика висит на самой кнопке и уходит вместе с ней.
// Текст справки (GET /api/admin/notes/{key}) — по первому клику, дальше из памяти страницы.
(function () {
    const API = '/api/admin/notes';
    const STALE_DAYS = 90;

    let list = null;          // Map ключ → {key, title, checked, path}; null — ещё не загружен
    let listPromise = null;
    const bodies = new Map(); // ключ → Promise<view>

    let pop = null;           // элемент попапа (один на страницу)
    let openKey = null;
    let anchor = null;        // кнопка, у которой открыт попап

    const escHtml = v => String(v ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&#39;');

    const ICON = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="size-[1.05em]" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/></svg>';

    function getJson(url) {
        return fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } })
            .then(r => { if (!r.ok) throw new Error(`${r.status} ${r.statusText}`); return r.json(); });
    }

    function loadList() {
        if (!listPromise) {
            listPromise = getJson(API)
                .catch(() => [])
                .then(arr => { list = new Map((arr || []).map(n => [n.key, n])); return list; });
        }
        return listPromise;
    }

    // «2026-11-20» → «20.11.2026»; старше STALE_DAYS — пометка, что цифры пора перепроверить.
    function checkedText(iso) {
        if (!iso) return { text: 'дата проверки не указана', stale: true };
        const [y, m, d] = iso.split('-');
        const days = (Date.now() - Date.UTC(+y, +m - 1, +d)) / 86400000;
        return { text: `проверено ${d}.${m}.${y}`, stale: days > STALE_DAYS };
    }

    // ── Значки ───────────────────────────────────────────────────────────────────────────
    function enhance(el) {
        if (el.dataset.noteState) return;
        const isMark = el.hasAttribute('data-note-mark');
        const key = isMark ? el.dataset.noteMark : el.dataset.note;
        const info = list.get(key);
        el.dataset.noteState = info ? 'on' : 'none';
        if (!info) { el.hidden = true; return; }

        if (isMark) {
            el.innerHTML = `<span class="inline-flex shrink-0 text-admin-text-muted" title="Есть справка: ${escHtml(info.title)} — откроется в панели службы">${ICON}</span>`;
            return;
        }
        const b = document.createElement('button');
        b.type = 'button';
        b.className = 'inline-flex size-6 shrink-0 cursor-pointer items-center justify-center rounded-full border-0 bg-transparent p-0 align-middle text-admin-text-muted hover:text-admin-accent focus-visible:outline-2 focus-visible:outline-admin-accent aria-expanded:text-admin-accent';
        b.dataset.noteBtn = key;
        b.title = `Справка: ${info.title}`;
        b.setAttribute('aria-label', `Справка: ${info.title}`);
        b.setAttribute('aria-haspopup', 'dialog');
        b.setAttribute('aria-expanded', 'false');
        b.innerHTML = ICON;
        // Значок часто стоит внутри кликабельного (шапка панели, строка) — свой клик не отдаём наверх.
        b.addEventListener('click', e => {
            e.preventDefault();
            e.stopPropagation();
            if (openKey === key && anchor === b) close(true); else open(key, b);
        });
        el.appendChild(b);
    }

    function scan(root) {
        if (!list || !root) return;
        const sel = '[data-note]:not([data-note-state]),[data-note-mark]:not([data-note-state])';
        if (root.nodeType === 1 && root.matches(sel)) enhance(root);
        if (root.querySelectorAll) root.querySelectorAll(sel).forEach(enhance);
        reanchor();
    }

    // Опрос перерисовал место со значком — попап остаётся открытым у нового значка с тем же ключом.
    function reanchor() {
        if (!openKey || (anchor && anchor.isConnected)) return;
        const next = document.querySelector(`[data-note-btn="${CSS.escape(openKey)}"]`);
        if (!next) { close(false); return; }
        anchor = next;
        anchor.setAttribute('aria-expanded', 'true');
        place();
    }

    // ── Попап ────────────────────────────────────────────────────────────────────────────
    function ensurePop() {
        if (pop) return pop;
        pop = document.createElement('div');
        pop.id = 'admin-note-pop';
        pop.hidden = true;
        pop.tabIndex = -1;
        pop.setAttribute('role', 'dialog');
        pop.setAttribute('aria-modal', 'false');
        pop.setAttribute('aria-labelledby', 'admin-note-title');
        pop.className = 'fixed z-[60] flex flex-col overflow-hidden rounded-lg border border-admin-border bg-admin-surface text-admin-text shadow-2xl shadow-black/60 outline-none';
        pop.innerHTML = `
            <div class="flex shrink-0 items-start gap-3 border-b border-admin-border px-4 py-3">
                <div class="min-w-0 flex-1">
                    <h2 id="admin-note-title" class="m-0 text-sm font-semibold leading-snug text-white"></h2>
                    <div data-note-meta class="mt-1 text-[11px] text-admin-text-muted"></div>
                </div>
                <button type="button" data-note-close class="admin-btn admin-btn-ghost px-2.5 py-1" aria-label="Закрыть справку">✕</button>
            </div>
            <div data-note-body class="admin-md min-h-0 flex-1 overflow-y-auto px-4 py-3 text-[13px]"></div>
            <div class="flex shrink-0 flex-wrap items-center justify-between gap-2 border-t border-admin-border px-4 py-2 text-xs">
                <a data-note-docs class="font-semibold text-admin-accent no-underline hover:underline" href="#">Открыть в Docs ↗</a>
                <code data-note-path class="truncate text-[11px] text-admin-text-muted"></code>
            </div>`;
        pop.querySelector('[data-note-close]').addEventListener('click', () => close(true));
        document.body.appendChild(pop);
        return pop;
    }

    function fill(info, view, error) {
        const p = ensurePop();
        const c = checkedText(info.checked);
        p.querySelector('#admin-note-title').textContent = info.title;
        const meta = p.querySelector('[data-note-meta]');
        meta.textContent = c.text + (c.stale ? ' — цифры могли устареть' : '');
        meta.className = `mt-1 text-[11px] ${c.stale ? 'text-admin-warning' : 'text-admin-text-muted'}`;
        const body = p.querySelector('[data-note-body]');
        // HTML собран сервером из MD с выключенным сырым HTML (DocsRenderer) — вставляем как есть.
        if (view) body.innerHTML = view.html;
        else body.innerHTML = `<p class="${error ? 'text-admin-warning' : 'text-admin-text-muted'}">${escHtml(error || 'Загрузка…')}</p>`;
        p.querySelector('[data-note-docs]').href = `/Admin/Docs/notes?file=${encodeURIComponent(info.path)}`;
        p.querySelector('[data-note-path]').textContent = info.path;
    }

    function place() {
        if (!pop || pop.hidden || !anchor) return;
        const vw = document.documentElement.clientWidth;
        const vh = window.innerHeight;
        const gap = 8;
        const r = anchor.getBoundingClientRect();
        const width = vw < 640 ? vw - 2 * gap : Math.min(544, vw - 2 * gap);
        let left = vw < 640 ? gap : r.left + r.width / 2 - 40;
        left = Math.max(gap, Math.min(left, vw - width - gap));

        const below = vh - r.bottom - gap * 2;
        const above = r.top - gap * 2;
        const down = below >= 280 || below >= above;
        const maxH = Math.max(180, Math.min(down ? below : above, Math.round(vh * 0.8), 640));
        pop.style.width = `${width}px`;
        pop.style.left = `${left}px`;
        pop.style.maxHeight = `${maxH}px`;
        if (down) { pop.style.top = `${Math.round(r.bottom + gap)}px`; pop.style.bottom = ''; }
        else { pop.style.bottom = `${Math.round(vh - r.top + gap)}px`; pop.style.top = ''; }
    }

    function open(key, from) {
        const info = list && list.get(key);
        if (!info) return;
        if (anchor && anchor !== from) anchor.setAttribute('aria-expanded', 'false');
        openKey = key;
        anchor = from;
        anchor.setAttribute('aria-expanded', 'true');
        const p = ensurePop();
        if (!bodies.has(key)) bodies.set(key, getJson(`${API}/${encodeURIComponent(key)}`));
        fill(info, null, null);
        p.hidden = false;
        p.querySelector('[data-note-body]').scrollTop = 0;
        place();
        p.focus({ preventScroll: true });
        bodies.get(key).then(
            view => { if (openKey === key) { fill(view, view, null); place(); } },
            err => { bodies.delete(key); if (openKey === key) { fill(info, null, `Справка не загрузилась: ${err.message}`); place(); } });
    }

    function close(returnFocus) {
        if (!openKey) return;
        const a = anchor;
        openKey = null;
        anchor = null;
        if (pop) pop.hidden = true;
        if (a) {
            a.setAttribute('aria-expanded', 'false');
            if (returnFocus && a.isConnected) a.focus({ preventScroll: true });
        }
    }

    // Esc — раньше обработчиков страницы (у Jobs Esc закрывает панель деталей): сперва попап.
    window.addEventListener('keydown', e => {
        if (e.key !== 'Escape' || !openKey) return;
        e.preventDefault();
        e.stopImmediatePropagation();
        close(true);
    }, true);
    // Клик мимо попапа и значка — закрыть; фокус не перехватываем: он уходит туда, куда кликнули.
    document.addEventListener('pointerdown', e => {
        if (!openKey || !pop) return;
        if (pop.contains(e.target) || (anchor && anchor.contains(e.target))) return;
        close(false);
    }, true);
    window.addEventListener('resize', place);
    window.addEventListener('scroll', place, true); // true — прокрутка любого контейнера (панель Jobs)

    window.AdminNotes = {
        /// Синхронно превратить метки внутри root в значки (страница, перерисовавшая кусок сама,
        /// зовёт это сразу после innerHTML — чтобы вернуть фокус на значок).
        scan,
        /// Открыть справку у значка с этим ключом (если он на странице и справка есть).
        open(key) {
            return loadList().then(() => {
                scan(document.body);
                const b = document.querySelector(`[data-note-btn="${CSS.escape(key)}"]`);
                if (b) open(key, b);
                return !!b;
            });
        },
        has: key => !!(list && list.has(key)),
        close: () => close(true),
    };

    function start() {
        // Без меток на странице список не нужен — но метки может дорисовать скрипт, поэтому
        // наблюдатель ставим всегда, а список грузим при первой встреченной метке.
        const need = () => document.querySelector('[data-note],[data-note-mark]');
        const observer = new MutationObserver(records => {
            if (!list) { if (need()) loadList().then(() => scan(document.body)); return; }
            for (const r of records) r.addedNodes.forEach(n => { if (n.nodeType === 1) scan(n); });
            reanchor();
        });
        observer.observe(document.body, { childList: true, subtree: true });
        if (need()) loadList().then(() => scan(document.body));
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
