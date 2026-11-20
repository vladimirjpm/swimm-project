// /Admin/Jobs — панели, общие для обоих видов: «Требует внимания», «Идёт сейчас», «Ближайшие
// запуски», и «Журнал» (последние прогоны всех задач, только «Стена» на широком экране).
// Данные — attention[] / running[] / upcoming[] / jobs[].history ответа monitor (контракт —
// docs/admin-pages/jobs.md). Клик по службе открывает панель деталей (data-job), по «ждёт
// тебя» — ссылка во «Входящие».
(function () {
    const M = window.JobsMon;
    const esc = M.esc;
    const PANEL = 'admin-card min-w-0 p-4';

    const ATTENTION_KIND = {
        failed: 'Упала',
        overdue: 'Просрочена',
        'abandoned-claim': 'Брошенная заявка',
        waiting: 'Ждёт тебя',
    };

    M.renderAttention = function (d) {
        const nowMs = M.ms(d.now);
        const items = d.attention || [];
        const rows = items.map(a => {
            // Брошенная заявка — проблема, даже если служба «в норме»: жёлтая, как просрочка.
            const s = a.kind === 'abandoned-claim' ? 'overdue' : a.kind === 'waiting' ? 'waiting' : a.status;
            const m = M.st(s);
            const head = `<span class="flex items-start gap-2.5">
                    ${M.statusDot(s)}
                    <span class="min-w-0 flex-1">
                        <span class="flex flex-wrap items-baseline gap-x-2">
                            <span class="min-w-[9rem] flex-1 font-semibold leading-snug text-white">${esc(a.title)}</span>
                            <span class="ml-auto inline-flex items-baseline gap-2 whitespace-nowrap">${M.zoneTag(a.zone, d.environment)}
                            <span class="text-[11px] ${m.text}">${esc(ATTENTION_KIND[a.kind] || m.kindWord)}</span></span>
                        </span>
                        <span class="mt-0.5 block text-xs leading-snug text-admin-text line-clamp-2" dir="auto">${M.bidi(a.text)}</span>
                        ${a.since ? `<span class="mt-0.5 block font-mono text-[10px] text-admin-text-muted">с ${esc(M.dayHm(a.since, nowMs))}</span>` : ''}
                    </span>
                </span>`;
            const cls = 'block w-full rounded-md p-1.5 text-left no-underline hover:bg-admin-surface-hover focus-visible:outline-2 focus-visible:outline-admin-accent';
            return a.href
                ? `<li><a href="${esc(a.href)}" class="${cls}">${head}</a></li>`
                : `<li><button type="button" data-job="${esc(a.key)}" class="${cls} cursor-pointer border-0 bg-transparent text-admin-text">${head}</button></li>`;
        }).join('');
        return `<section class="${PANEL}" aria-labelledby="jp-attention">
            <div id="jp-attention">${M.panelTitle('Требует внимания', items.length, items.length ? 'text-admin-warning' : 'text-admin-text-muted')}</div>
            ${items.length ? `<ul class="m-0 flex list-none flex-col gap-1.5 p-0">${rows}</ul>` : M.empty('Всё в порядке — ни упавших, ни просроченных, решений не ждёт.')}
        </section>`;
    };

    M.renderRunning = function (d) {
        const nowMs = M.ms(d.now);
        const items = d.running || [];
        const rows = items.map(r => {
            const pct = M.progressPct(r.progress);
            const bar = pct != null
                ? `<span class="mt-1.5 block h-1.5 overflow-hidden rounded-full bg-admin-surface-hover"><span class="block h-full rounded-full bg-admin-accent" style="width:${pct}%"></span></span>`
                : `<span class="jobs-stripes mt-1.5 block h-1.5 rounded-full" title="без оценки"></span>`;
            const sub = r.progress ? M.progressText(r.progress) : r.by ? `запустил ${r.by}` : 'без оценки — прогресс не сообщает';
            const since = r.since ? `с ${M.hm(r.since)} · ${M.human((nowMs - M.ms(r.since)) / 1000)}` : '';
            return `<li><button type="button" data-job="${esc(r.key)}" class="block w-full cursor-pointer rounded-md border-0 bg-transparent p-1.5 text-left text-admin-text hover:bg-admin-surface-hover focus-visible:outline-2 focus-visible:outline-admin-accent">
                <span class="flex items-center gap-2">
                    ${M.zoneTag(r.zone, d.environment, false)}
                    <span class="min-w-0 flex-1 truncate font-semibold text-white">${esc(r.title)}</span>
                    <span class="whitespace-nowrap font-mono text-[11px] text-admin-text-muted">${esc(since)}</span>
                </span>
                ${bar}
                <span class="mt-1 block truncate font-mono text-[11px] text-admin-text-muted" dir="auto">${M.bidi(sub)}</span>
            </button></li>`;
        }).join('');
        return `<section class="${PANEL}" aria-labelledby="jp-running">
            <div id="jp-running">${M.panelTitle('Идёт сейчас', items.length, items.length ? 'text-admin-accent' : 'text-admin-text-muted')}</div>
            ${items.length ? `<ul class="m-0 flex list-none flex-col gap-2 p-0">${rows}</ul>` : M.empty('Сейчас ничего не идёт.')}
        </section>`;
    };

    M.renderUpcoming = function (d) {
        const nowMs = M.ms(d.now);
        const items = d.upcoming || [];
        const rows = items.map(u => `<li><button type="button" data-job="${esc(u.key)}" class="grid w-full cursor-pointer grid-cols-[5.5rem_minmax(0,1fr)_auto] items-center gap-x-3 rounded-md border-0 bg-transparent px-1.5 py-1 text-left text-[13px] text-admin-text hover:bg-admin-surface-hover focus-visible:outline-2 focus-visible:outline-admin-accent">
                <span class="whitespace-nowrap font-mono text-xs ${u.isRetry ? 'text-admin-danger' : 'text-admin-text'}">${esc(M.until(u.at, nowMs))}</span>
                <span class="truncate">${esc(u.title)}${u.isRetry ? ' <span class="text-admin-danger">· повтор</span>' : ''}</span>
                <span class="flex items-center gap-1.5 whitespace-nowrap font-mono text-[11px] text-admin-text-muted">${M.zoneTag(u.zone, d.environment, false)}${u.approx ? '≈' : ''}${esc(M.dayHm(u.at, nowMs))}</span>
            </button></li>`).join('');
        const anyRunning = (d.jobs || []).some(j => j.kind === 'schedule' && j.active);
        return `<section class="${PANEL}" aria-labelledby="jp-upcoming">
            <div id="jp-upcoming">${M.panelTitle('Ближайшие запуски', 0, 'text-admin-text-muted')}</div>
            ${items.length ? `<ul class="m-0 flex list-none flex-col gap-0.5 p-0">${rows}</ul>`
                : M.empty(anyRunning ? 'Запланированных запусков нет — всё, чему пора, запустится при ближайшей проверке.'
                    : 'Расписания здесь не работают — ждать нечего.')}
        </section>`;
    };

    // Журнал: последние прогоны всех задач (история журнала + идущие по заявке), свежие первыми.
    M.renderJournal = function (d, limit = 8) {
        const nowMs = M.ms(d.now);
        const rows = [];
        for (const j of d.jobs || []) {
            for (const h of j.history || []) rows.push({ j, at: h.startedAt, h });
            if (j.status === 'running' && j.kind === 'schedule' && j.claim && j.claim.live)
                rows.push({ j, at: j.claim.since, running: true });
        }
        rows.sort((a, b) => M.ms(b.at) - M.ms(a.at));
        const top = rows.slice(0, limit);
        const title = d.journal === 'prod' ? 'Журнал прод-базы · общий для обеих зон' : 'Журнал локальной базы';
        const body = top.map(r => {
            const h = r.h;
            const what = r.running ? '<span class="text-admin-accent">в работе</span>'
                : h.success ? `<span dir="auto">${M.bidi(h.summary || "—")}</span>`
                : `<span class="text-admin-danger" dir="auto">ошибка: ${M.bidi((h.error || '').split(/\r?\n/)[0])}</span>`;
            return `<tr>
                <td class="whitespace-nowrap font-mono text-xs">${esc(M.dayHm(r.at, nowMs))}</td>
                <td class="whitespace-nowrap">${M.zoneTag(r.j.zone, d.environment)}</td>
                <td class="whitespace-nowrap"><button type="button" data-job="${esc(r.j.key)}" class="cursor-pointer border-0 bg-transparent p-0 text-left text-[13px] text-admin-text hover:text-admin-accent">${esc(r.j.title)}</button></td>
                <td class="whitespace-nowrap text-xs text-admin-text-muted">${esc(r.running ? 'фон' : h.triggerText)}</td>
                <td class="whitespace-nowrap font-mono text-xs text-admin-text-muted">${r.running ? 'идёт' : esc(M.dur(h.durationSeconds))}</td>
                <td class="max-w-0 truncate text-xs w-full">${what}</td>
            </tr>`;
        }).join('');
        return `<section class="admin-card min-w-0 p-4" aria-labelledby="jp-journal">
            <div id="jp-journal">${M.panelTitle(title, 0, 'text-admin-text-muted')}</div>
            ${top.length ? `<div class="overflow-x-auto"><table class="admin-table"><tbody>${body}</tbody></table></div>`
                : M.empty('В журнале пока нет прогонов.')}
        </section>`;
    };
})();
