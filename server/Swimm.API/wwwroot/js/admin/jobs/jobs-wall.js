// /Admin/Jobs — вид «Стена статусов»: зоны с плитками служб, на очень широком экране — журнал под
// зонами и колонка панелей справа; уже — панели рядом под зонами (как макет 1280).
(function () {
    const M = window.JobsMon;
    const esc = M.esc;

    // Полоска 15 последних прогонов: слева старые, справа свежие (recentOutcomes — свежие первыми).
    function strip(j) {
        const out = (j.recentOutcomes || []).slice(0, 15).reverse();
        const cells = [];
        for (let i = 0; i < 15 - out.length; i++) cells.push('<span class="h-1 flex-1 rounded-sm bg-admin-border/70"></span>');
        for (const o of out) {
            const cls = o === 'failed' ? 'bg-admin-danger' : o === 'running' ? 'bg-admin-accent' : 'bg-admin-success';
            cells.push(`<span class="h-1 flex-1 rounded-sm ${cls}"></span>`);
        }
        const fails = out.filter(o => o === 'failed').length;
        const label = out.length ? `последние ${out.length}: ошибок ${fails}` : 'прогонов ещё не было';
        return `<span class="mt-auto flex gap-1 pt-3" role="img" aria-label="${esc(label)}" title="${esc(label)}">${cells.join('')}</span>`;
    }

    function tile(d, j, compact) {
        const nowMs = M.ms(d.now);
        const m = M.st(j.status);
        const sum = M.summaryLine(j);
        const interval = M.intervalText(j);
        return `<button type="button" data-job="${esc(j.key)}" aria-label="${esc(M.ariaJob(j, nowMs))}"
                class="flex min-w-0 cursor-pointer flex-col rounded-lg border p-3 text-left text-admin-text transition-colors hover:brightness-110 focus-visible:outline-2 focus-visible:outline-admin-accent ${m.tile} ${compact ? '' : '2xl:p-4'}">
            <span class="flex min-w-0 items-center gap-2" title="${esc(j.badge.text)}">
                ${M.statusDot(j.status)}<span class="sr-only">${esc(m.word)}:</span>
                <span class="min-w-0 flex-1 truncate font-semibold text-white">${esc(j.title)}</span>
                ${!compact && interval ? `<span class="hidden whitespace-nowrap font-mono 2xl:inline text-[11px] text-admin-text-muted">${esc(interval)}</span>` : ''}
            </span>
            <span class="mt-1.5 block truncate font-bold leading-tight ${m.text} ${compact ? 'text-base' : 'text-lg 2xl:text-[22px]'}" title="${esc(M.mainLine(j, nowMs))}">${esc(M.mainLine(j, nowMs))}</span>
            <span class="mt-1 block truncate text-xs text-admin-text" dir="auto" title="${esc(sum)}">${M.bidi(sum) || "&nbsp;"}</span>
            ${compact ? '' : `${strip(j)}
            <span class="mt-2 flex justify-between gap-2 font-mono text-[11px] text-admin-text-muted">
                <span class="truncate">был ${esc(M.wasText(j, nowMs))}</span>
                <span class="truncate text-right">след. ${esc(M.nextShort(j, nowMs))}</span>
            </span>`}
        </button>`;
    }

    function group(d, title, list, compact, gridCls) {
        return `<div class="min-w-0">
            ${title ? `<h3 class="m-0 mb-2 text-[11px] font-bold uppercase tracking-[.08em] text-admin-text-muted">${esc(title)} · ${list.length}</h3>` : ''}
            <div class="grid gap-2.5 ${gridCls}">${list.map(j => tile(d, j, compact)).join('')}</div>
        </div>`;
    }

    function zoneBlock(d, zone) {
        const nowMs = M.ms(d.now);
        const z = M.zone(zone.key, d.environment);
        const byKey = Object.fromEntries(d.jobs.map(j => [j.key, j]));
        const jobs = zone.jobKeys.map(k => byKey[k]).filter(Boolean);
        const of = kind => jobs.filter(j => j.kind === kind);
        const kinds = M.KINDS.filter(k => of(k.key).length);
        const showSub = kinds.length > 1;
        const sched = of('schedule'), queues = of('queue'), services = of('service');
        let body = '';
        if (sched.length)
            body += group(d, showSub ? 'Расписания' : '', sched, false, 'sm:grid-cols-2 xl:grid-cols-4');
        // Очереди и служебные — короче и рядом, если есть обе группы.
        const small = [];
        if (queues.length) small.push(group(d, 'Очереди · по кнопке', queues, true, 'sm:grid-cols-3'));
        if (services.length) small.push(group(d, showSub ? 'Служебные' : '', services, true, 'sm:grid-cols-3'));
        if (small.length) body += `<div class="grid gap-3 ${small.length > 1 ? 'min-[1700px]:grid-cols-2' : ''}">${small.join('')}</div>`;
        return `<section class="flex flex-col gap-3 rounded-xl border ${z.frame} p-3" aria-label="${esc(z.title)}">
            <div class="flex flex-wrap items-center gap-x-2.5 gap-y-1">
                <span class="inline-flex size-[22px] shrink-0 items-center justify-center rounded-md ${z.badge}">${M.icon(z.icon)}</span>
                <h2 class="m-0 whitespace-nowrap text-[15px] font-bold text-white">${esc(z.title)}</h2>
                <span class="min-w-0 truncate text-xs ${z.subText}">${esc(z.sub)}</span>
                <span class="ml-auto min-w-0">${M.pulse(zone, nowMs)}</span>
            </div>
            ${body}
        </section>`;
    }

    M.renderWall = function (d) {
        const zones = d.zones.map(z => zoneBlock(d, z)).join('');
        // ≥1700 px: панели колонкой справа и журнал под зонами; уже — панели рядом под зонами.
        return `<div class="grid gap-3 min-[1700px]:grid-cols-[minmax(0,1fr)_400px]">
            <div class="flex min-w-0 flex-col gap-3">
                ${zones}
                <div class="hidden min-[1700px]:block">${M.renderJournal(d)}</div>
            </div>
            <div class="grid content-start gap-3 lg:max-[1699px]:grid-cols-3">
                ${M.renderAttention(d)}
                ${M.renderRunning(d)}
                ${M.renderUpcoming(d)}
            </div>
        </div>`;
    };
})();
