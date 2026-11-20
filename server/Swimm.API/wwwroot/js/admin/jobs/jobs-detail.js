// /Admin/Jobs — панель деталей службы справа (по клику на плитку, дорожку, строку панели). Всё,
// что умела старая карточка: статус и пояснение, полный текст ошибки, факты, параметры и
// настройки, история прогонов таблицей, «Что изменилось» у discovery (GET runs/{id}), кнопки и
// ссылки (actions[]: «Проверить сейчас» у рекордов — POST с antiforgery), «Последние импорты» у
// очереди импорта. Закрывается Esc и ✕, фокус возвращается туда, откуда открыли.
(function () {
    const M = window.JobsMon;
    const esc = M.esc;

    let openKey = null;       // ключ открытой службы
    let opener = null;        // элемент, с которого открыли (вернуть фокус)
    let lastHtml = '';        // не перерисовывать то же самое при опросе
    let onAction = null;      // колбэк страницы: перечитать данные после POST
    const actionStatus = {};  // текст статуса кнопок, переживает перерисовку: url → строка

    const panel = () => document.getElementById('job-detail');
    const body = () => document.getElementById('job-detail-body');

    const FACT_CLS = {
        success: 'text-admin-success', danger: 'text-admin-danger', warning: 'text-admin-warning',
        info: 'text-admin-accent', neutral: 'text-admin-text-muted',
    };

    function section(title, html) {
        return `<section class="border-t border-admin-border px-5 py-4">
            <h3 class="m-0 mb-2 text-[11px] font-bold uppercase tracking-[.08em] text-admin-text-muted">${esc(title)}</h3>
            ${html}
        </section>`;
    }

    function historyTable(j, d) {
        const nowMs = M.ms(d.now);
        if (!j.history.length) {
            const why = j.kind === 'queue' ? 'У очередей своего журнала нет — постоянная история в их таблицах (ссылки ниже).'
                : j.kind === 'service' ? 'Служебные в журнал не пишут — состояние только в памяти процесса.'
                : j.timelineMode === 'ticker' ? 'Непустых тиков пока не было (пустые в историю не пишутся).'
                : 'Истории прогонов пока нет.';
            return M.empty(why);
        }
        const rows = j.history.map(h => {
            const start = h.hasDetails
                ? `<button type="button" data-run="${h.id}" aria-haspopup="dialog" title="${j.key === 'discovery' ? 'Показать, что изменилось' : 'Показать подробности прогона'}"
                        class="cursor-pointer border-0 bg-transparent p-0 font-mono text-xs text-admin-accent underline decoration-dotted underline-offset-2 hover:decoration-solid">${esc(M.full(h.startedAt))}</button>`
                : `<span class="font-mono">${esc(M.full(h.startedAt))}</span>`;
            const what = (h.success ? '' : `<span class="admin-badge mr-1 border-admin-danger/40 bg-admin-danger/15 text-admin-danger">ошибка</span><span class="text-admin-danger whitespace-pre-wrap break-words" dir="auto">${M.bidi(h.error)}</span> `)
                + (h.summary ? `<span dir="auto">${M.bidi(h.summary)}</span>` : '');
            return `<tr>
                <td class="whitespace-nowrap text-xs" title="${esc(M.ago(h.startedAt, nowMs))}">${start}</td>
                <td class="whitespace-nowrap text-xs">${esc(h.triggerText)}</td>
                <td class="whitespace-nowrap text-xs" title="${esc(h.runBy || '')}">${esc(h.originText || '—')}</td>
                <td class="whitespace-nowrap font-mono text-xs">${esc(M.dur(h.durationSeconds))}</td>
                <td class="min-w-[14rem] text-xs">${what || '—'}</td>
            </tr>`;
        }).join('');
        return `<div class="-mx-5 overflow-x-auto px-2"><table class="admin-table">
            <thead><tr><th>Начало</th><th>Кто</th><th>Откуда</th><th>Длилось</th><th>Что сделала</th></tr></thead>
            <tbody>${rows}</tbody></table></div>`;
    }

    // Прогоны очереди из памяти процесса (с её запуска).
    function queueRuns(j, d) {
        const runs = (j.windowRuns || []).slice().reverse();
        if (!runs.length) return '';
        const nowMs = M.ms(d.now);
        return section('Прогоны с запуска процесса', `<ul class="m-0 flex list-none flex-col gap-1 p-0 text-xs">${runs.map(r => {
            const s = r.outcome === 'failed' ? 'failed' : r.outcome === 'running' ? 'running' : 'ok';
            return `<li class="flex items-center gap-2">${M.statusIcon(s)}<span class="font-mono">${esc(M.dayHm(r.startedAt, nowMs))}</span>
                <span class="text-admin-text-muted">${r.finishedAt ? esc(M.dur((M.ms(r.finishedAt) - M.ms(r.startedAt)) / 1000)) : 'идёт'}</span></li>`;
        }).join('')}</ul>`);
    }

    function recentImports(d) {
        const list = d.recentImports || [];
        const nowMs = M.ms(d.now);
        if (!list.length) return section('Последние импорты', M.empty('Импортов ещё не было.'));
        return section('Последние импорты (Sys_ImportHistory)', `<div class="-mx-5 overflow-x-auto px-2"><table class="admin-table">
            <thead><tr><th>Файл</th><th>Соревнование</th><th>Когда</th></tr></thead><tbody>
            ${list.map(h => `<tr>
                <td class="break-all text-xs">${esc(h.importFileName)}</td>
                <td class="text-xs"><bdi>${esc(h.competitionName)}</bdi> <span class="text-admin-text-muted">${esc(h.competitionDate)}</span></td>
                <td class="whitespace-nowrap text-xs" title="${esc(M.full(h.importDate))}">${esc(M.dayHm(h.importDate, nowMs))}</td>
            </tr>`).join('')}</tbody></table></div>`);
    }

    function settingsBlock(j) {
        const rows = [];
        if (j.toggle) rows.push({ key: j.toggle.key, value: String(j.toggle.value), isDefault: j.toggle.isDefault });
        for (const s of j.settings || []) rows.push(s);
        if (!rows.length) return '';
        return section('Параметры и настройки (Admin/Settings)', `<dl class="m-0 grid grid-cols-[minmax(8rem,max-content)_1fr] gap-x-4 gap-y-1 text-[13px]">
            ${rows.map(s => `<dt class="font-mono text-xs text-admin-text-muted">${esc(s.key)}</dt>
                <dd class="m-0 font-mono text-xs">${s.value == null ? '<span class="text-admin-text-muted">умолчание</span>' : esc(s.value)}${s.isDefault ? ' <span class="text-admin-text-muted">(ключа нет — умолчание)</span>' : ''}</dd>`).join('')}
        </dl>`);
    }

    function actionsBlock(j) {
        if (!j.actions.length) return '';
        const html = j.actions.map(a => a.kind === 'post'
            ? `<div class="flex flex-wrap items-center gap-2">
                    <button type="button" data-post="${esc(a.url)}" class="admin-btn admin-btn-primary px-2.5 py-1">${esc(a.text)}</button>
                    ${a.hint ? `<span class="text-[11px] text-admin-text-muted">${esc(a.hint)}</span>` : ''}
                    <span role="status" aria-live="polite" data-post-status="${esc(a.url)}" class="basis-full text-[11px] text-admin-text-muted">${esc(actionStatus[a.url] || '')}</span>
               </div>`
            : `<a href="${esc(a.url)}" class="text-xs text-admin-accent no-underline hover:underline">${esc(a.text)} →</a>${a.hint ? ` <span class="text-[11px] text-admin-text-muted">${esc(a.hint)}</span>` : ''}`)
            .map(x => `<li>${x}</li>`).join('');
        return section('Действия и ссылки', `<ul class="m-0 flex list-none flex-col gap-2 p-0">${html}</ul>`);
    }

    function render(j, d) {
        const nowMs = M.ms(d.now);
        const m = M.st(j.status);
        const z = M.zone(j.zone, d.environment);
        const kind = (M.KINDS.find(k => k.key === j.kind) || {}).title || j.kind;
        const pct = M.progressPct(j.progress);

        let html = `<header class="sticky top-0 z-10 flex items-start gap-3 border-b border-admin-border bg-admin-surface px-5 py-4">
            ${M.statusDot(j.status, 'size-7')}
            <div class="min-w-0 flex-1">
                <h2 id="job-detail-title" class="m-0 flex items-center gap-1.5 text-base font-semibold text-white"><span class="min-w-0">${esc(j.title)}</span><span data-note="job.${esc(j.key)}"></span></h2>
                <div class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] text-admin-text-muted">
                    ${M.zoneTag(j.zone, d.environment)}<span>${esc(z.title)}</span><span>${esc(kind)}</span>
                    <code class="font-mono">${esc(j.service)}</code>
                </div>
            </div>
            <button type="button" data-close-detail class="admin-btn admin-btn-ghost px-2.5 py-1" aria-label="Закрыть подробности">✕</button>
        </header>`;

        // Статус и пояснение.
        html += `<section class="px-5 py-4">
            <div class="flex flex-wrap items-baseline gap-x-3 gap-y-1">
                <span class="text-xl font-bold ${m.text}">${esc(M.mainLine(j, nowMs))}</span>
                <span class="admin-badge border-admin-border text-admin-text-muted">${esc(j.badge.text)}</span>
            </div>
            <p class="m-0 mt-1.5 text-[13px] leading-snug text-admin-text" dir="auto">${M.bidi(j.statusText)}</p>
            <p class="m-0 mt-2 text-[13px] leading-relaxed text-admin-text-muted">${esc(j.what)}</p>
            ${j.claim && !j.claim.live ? `<p class="m-0 mt-2 rounded-md border border-admin-warning/50 bg-admin-warning/10 px-3 py-2 text-xs text-admin-warning">
                Брошенная заявка: взята ${esc(M.full(j.claim.since))} (${esc(M.ago(j.claim.since, nowMs))}), ${esc(j.claim.by || '?')} — процесс, вероятно, убит посреди прогона.</p>` : ''}
            ${j.claim && j.claim.live ? `<p class="m-0 mt-2 text-xs text-admin-accent">Идёт с ${esc(M.hm(j.claim.since))}, запустил <span class="font-mono">${esc(j.claim.by || '?')}</span></p>` : ''}
            ${j.progress ? `<div class="mt-3">
                <div class="h-1.5 overflow-hidden rounded-full bg-admin-surface-hover">${pct != null ? `<div class="h-full rounded-full bg-admin-accent" style="width:${pct}%"></div>` : ''}</div>
                <div class="mt-1 font-mono text-[11px] text-admin-text-muted">${esc(M.progressText(j.progress))}${pct != null ? ` · ${pct}%` : ''}</div>
            </div>` : ''}
        </section>`;

        if (j.lastError)
            html += section('Последняя ошибка', `<pre class="m-0 max-h-60 overflow-auto whitespace-pre-wrap break-words rounded-md border border-admin-danger/40 bg-admin-danger/10 p-3 font-mono text-xs text-admin-danger" dir="auto">${esc(j.lastError)}</pre>`);

        if (j.facts.length)
            html += section('Факты', `<dl class="m-0 grid grid-cols-[minmax(8rem,max-content)_1fr] gap-x-4 gap-y-1 text-[13px]">
                ${j.facts.map(f => `<dt class="text-admin-text-muted">${esc(f.label)}</dt><dd class="m-0 break-words ${FACT_CLS[f.variant] || 'text-admin-text'}" dir="auto">${M.bidi(f.value)}</dd>`).join('')}
            </dl>`);

        if (j.key === 'records-check' && d.recordsSources) {
            const rs = d.recordsSources;
            const tone = rs.tone === 'danger' ? 'text-admin-danger' : rs.tone === 'warning' ? 'text-admin-warning' : rs.tone === 'ok' ? 'text-admin-success' : 'text-admin-text';
            html += section('Рекорды · источники', `<div class="text-lg font-bold ${tone}" title="${rs.lastCheckedAt ? 'Последняя успешная проверка: ' + esc(M.full(rs.lastCheckedAt)) : 'Источники ещё не проверяли'}">${esc(rs.value)}</div>
                <div class="text-xs text-admin-text-muted">${esc(rs.sub)}</div>`);
        }

        html += settingsBlock(j);
        html += actionsBlock(j);
        if (j.kind === 'queue') html += queueRuns(j, d);
        if (j.key === 'import') html += recentImports(d);
        if (j.kind === 'schedule' || j.history.length) html += section(`История прогонов${j.history.length ? ` · последние ${j.history.length}` : ''}`, historyTable(j, d));
        return html;
    }

    function paint(d) {
        const j = d.jobs.find(x => x.key === openKey);
        if (!j) { M.closeDetail(); return; }
        const html = render(j, d);
        if (html === lastHtml) return;
        const b = body();
        // Перерисовка при опросе: сохранить прокрутку и фокус (по data-атрибуту элемента).
        const scroll = b.scrollTop;
        const f = document.activeElement && b.contains(document.activeElement) ? document.activeElement : null;
        const fSel = f ? (f.dataset.run ? `[data-run="${f.dataset.run}"]` : f.dataset.post ? `[data-post="${f.dataset.post}"]` : f.dataset.noteBtn ? `[data-note-btn="${CSS.escape(f.dataset.noteBtn)}"]` : f.hasAttribute('data-close-detail') ? '[data-close-detail]' : null) : null;
        b.innerHTML = html;
        lastHtml = html;
        // Значок справки «ⓘ» — сразу, а не наблюдателем: чтобы фокус вернулся и на него (admin-notes.js).
        if (window.AdminNotes) window.AdminNotes.scan(b);
        b.scrollTop = scroll;
        if (fSel) { const el = b.querySelector(fSel); if (el) el.focus({ preventScroll: true }); }
    }

    M.openDetail = function (key, from, d) {
        openKey = key;
        opener = from || null;
        lastHtml = '';
        const p = panel();
        p.hidden = false;
        paint(d);
        p.setAttribute('aria-labelledby', 'job-detail-title');
        body().scrollTop = 0;
        const close = body().querySelector('[data-close-detail]');
        if (close) close.focus({ preventScroll: true });
        document.querySelectorAll('[data-job].jobs-selected').forEach(e => e.classList.remove('jobs-selected'));
        document.querySelectorAll(`[data-job="${CSS.escape(key)}"]`).forEach(e => e.classList.add('jobs-selected'));
        if (location.hash !== `#job-${key}`) history.replaceState(null, '', `${location.pathname}${location.search}#job-${key}`);
    };

    M.closeDetail = function () {
        if (!openKey) return;
        const key = openKey;
        openKey = null;
        lastHtml = '';
        panel().hidden = true;
        document.querySelectorAll('[data-job].jobs-selected').forEach(e => e.classList.remove('jobs-selected'));
        if (location.hash) history.replaceState(null, '', `${location.pathname}${location.search}`);
        // Вернуть фокус: на тот же элемент, а если его перерисовали опросом — на такой же по data-job.
        let target = opener && document.contains(opener) ? opener : document.querySelector(`#jobs-view [data-job="${CSS.escape(key)}"]`);
        if (target) target.focus({ preventScroll: false });
        opener = null;
    };

    M.detailKey = () => openKey;
    M.updateDetail = d => { if (openKey) { paint(d); document.querySelectorAll(`[data-job="${CSS.escape(openKey)}"]`).forEach(e => e.classList.add('jobs-selected')); } };
    M.setDetailActionHandler = fn => { onAction = fn; };

    // ── «Что изменилось» / подробности прогона: GET runs/{id} в <dialog> ─────────────────
    function changesHtml(v) {
        const c = v.discoveryChanges;
        if (!c) {
            return `<div class="px-5 py-4 text-[13px]">
                ${v.summary ? `<p class="m-0 mb-2" dir="auto">${M.bidi(v.summary)}</p>` : ''}
                ${v.error ? `<pre class="m-0 mb-2 whitespace-pre-wrap break-words font-mono text-xs text-admin-danger">${esc(v.error)}</pre>` : ''}
                ${v.details != null ? `<pre class="m-0 max-h-[60vh] overflow-auto whitespace-pre-wrap break-words rounded-md bg-admin-bg p-3 font-mono text-[11px] text-admin-text" dir="auto">${esc(JSON.stringify(v.details, null, 2))}</pre>` : M.empty('Подробностей у прогона нет.')}
            </div>`;
        }
        const tone = { new: 'text-admin-success', postponed: 'text-admin-warning', missing: 'text-admin-danger' };
        let html = c.truncated != null
            ? `<div class="border-b border-admin-border px-5 py-2 text-xs text-admin-warning">Показаны первые ${c.total} из ${c.truncated} — список за прогон обрезан (JobRunDetails.MaxChanges).</div>` : '';
        if (!c.groups.length) html += `<div class="px-5 py-4">${M.empty('Изменений за прогон нет.')}</div>`;
        for (const g of c.groups) {
            html += `<section class="border-b border-admin-border/70 last:border-b-0">
                <h3 class="m-0 px-5 pb-1 pt-3 text-[11px] font-semibold uppercase tracking-wide ${tone[g.kind] || 'text-admin-accent'}">${esc(g.title)} · ${g.lines.length}
                    <span class="ml-1 font-normal normal-case tracking-normal text-admin-text-muted">— ${esc(g.hint)}</span></h3>
                <ul class="m-0 list-none p-0 pb-2">${g.lines.map(l => `<li class="flex flex-wrap items-baseline gap-x-3 gap-y-0.5 px-5 py-1.5 text-[13px] hover:bg-admin-surface-hover">
                    <span class="min-w-0 flex-1 basis-[16rem] font-semibold text-admin-text" dir="auto">${esc(l.name)}</span>
                    <span class="whitespace-nowrap text-xs text-admin-text-muted">${l.oldDates ? `<span class="line-through">${esc(l.oldDates)}</span> → <span class="text-admin-text">${esc(l.dates)}</span>` : esc(l.dates)}</span>
                    <span class="whitespace-nowrap text-xs"><a href="${esc(l.siteUrl)}" target="_blank" rel="noopener" class="text-admin-accent hover:underline">compID ${esc(l.orgCompId)} ↗</a>
                        <span class="text-admin-text-muted"> · </span><a href="${esc(l.adminUrl)}" class="text-admin-accent hover:underline">${esc(l.adminText)} →</a></span>
                    ${l.oldName ? `<span class="basis-full text-xs text-admin-text-muted">было: <span dir="auto">${esc(l.oldName)}</span></span>` : ''}
                    ${l.note ? `<span class="basis-full text-xs text-admin-text-muted">${esc(l.note)}</span>` : ''}
                </li>`).join('')}</ul>
            </section>`;
        }
        return html;
    }

    async function openRun(id, btn) {
        const dlg = document.getElementById('run-changes');
        const title = document.getElementById('run-changes-title');
        const content = document.getElementById('run-changes-body');
        title.innerHTML = 'Загрузка…';
        content.innerHTML = '';
        dlg.showModal();
        dlg.querySelector('[data-close-run]').focus();
        dlg.onclose = () => { if (btn && document.contains(btn)) btn.focus({ preventScroll: true }); };
        try {
            const v = await api(`/api/admin/jobs/runs/${encodeURIComponent(id)}`);
            const c = v.discoveryChanges;
            title.innerHTML = c
                ? `Что изменилось <span class="text-sm font-normal text-admin-text-muted">— прогон ${esc(M.full(v.startedAt))}, изменений ${c.total}</span>`
                : `Прогон <span class="text-sm font-normal text-admin-text-muted">— ${esc(M.full(v.startedAt))}, ${v.success ? 'успех' : 'ошибка'}</span>`;
            content.innerHTML = changesHtml(v);
        } catch (e) {
            title.textContent = 'Прогон не загрузился';
            content.innerHTML = `<p class="m-0 px-5 py-4 text-[13px] text-admin-danger">${esc(e.message)}${String(e.message).startsWith('404') ? ' — строки журнала уже нет (вытеснена обрезкой).' : ''}</p>`;
        }
    }

    async function post(btn) {
        const url = btn.dataset.post;
        const st = () => body().querySelector(`[data-post-status="${CSS.escape(url)}"]`);
        adminBusy(btn, true, 'Проверяю…');
        actionStatus[url] = 'Идёт… до 5 минут';
        if (st()) st().textContent = actionStatus[url];
        try {
            const res = await fetch(url, { method: 'POST', credentials: 'same-origin', headers: { 'X-XSRF-TOKEN': _csrfToken } });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            actionStatus[url] = `Готово в ${M.hms(Date.now())} — данные обновлены.`;
            if (onAction) await onAction();
        } catch (err) {
            actionStatus[url] = `Ошибка: ${err.message}`;
        } finally {
            // Кнопку могли перерисовать — ищем живую по адресу.
            const live = body().querySelector(`[data-post="${CSS.escape(url)}"]`) || btn;
            adminBusy(live, false);
            if (st()) st().textContent = actionStatus[url];
        }
    }

    document.addEventListener('DOMContentLoaded', () => {
        const p = panel();
        p.addEventListener('click', e => {
            if (e.target.closest('[data-close-detail]')) { M.closeDetail(); return; }
            const run = e.target.closest('[data-run]');
            if (run) { openRun(run.dataset.run, run); return; }
            const pb = e.target.closest('[data-post]');
            if (pb) post(pb);
        });
        const dlg = document.getElementById('run-changes');
        dlg.addEventListener('click', e => { if (e.target === dlg || e.target.closest('[data-close-run]')) dlg.close(); });
        document.addEventListener('keydown', e => {
            // Esc закрывает панель, если сверху нет открытого окна прогона (его закрывает сам <dialog>).
            if (e.key === 'Escape' && openKey && !dlg.open) { e.preventDefault(); M.closeDetail(); }
        });
    });
})();
