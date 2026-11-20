// /Admin/Jobs — вид «Таймлайн» (по умолчанию): ось времени 36 ч, зоны рамками, по дорожке на
// службу, служебные — строкой плашек, снизу три панели. Окно (windowFrom..windowTo) и «сейчас»
// приходят с сервера; подписи оси — в местном времени браузера каждые 3 часа.
(function () {
    const M = window.JobsMon;
    const esc = M.esc;

    // Колонки дорожки: служба | полоса | следующий запуск. На узком экране полоса уходит второй
    // строкой на всю ширину (order-last + col-span-2), шапка оси прячет подписи колонок.
    const LANE_GRID = 'grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 lg:grid-cols-[220px_minmax(0,1fr)_190px] 2xl:grid-cols-[300px_minmax(0,1fr)_320px]';
    const BAR_CELL = 'order-last col-span-2 lg:order-none lg:col-span-1';

    function windowOf(d) {
        const from = M.ms(d.windowFrom), to = M.ms(d.windowTo), now = M.ms(d.now);
        const pct = t => Math.max(0, Math.min(100, (t - from) * 100 / (to - from)));
        return { from, to, now, pct, nowPct: pct(now) };
    }

    // Местные полуночи и часы, кратные трём, внутри окна.
    function axisTicks(w) {
        const ticks = [];
        const t = new Date(w.from);
        t.setMinutes(0, 0, 0);
        if (t.getTime() < w.from) t.setHours(t.getHours() + 1);
        for (; t.getTime() <= w.to; t.setHours(t.getHours() + 1)) {
            const h = t.getHours();
            if (h % 3 === 0) ticks.push({ ms: t.getTime(), midnight: h === 0, h });
        }
        return ticks;
    }

    function axis(d, w) {
        const ticks = axisTicks(w);
        const labels = ticks.map((k, i) => {
            const text = k.midnight ? M.dayLabel(k.ms) : `${String(k.h).padStart(2, '0')}:00`;
            // Каждые 3 ч — только на широком экране; уже — каждые 6 ч (иначе подписи наезжают). Полночь всегда.
            const hide = !k.midnight && k.h % 6 !== 0 ? 'hidden 2xl:block' : 'block';
            return `<span class="${hide} absolute bottom-0.5 whitespace-nowrap border-l border-[#4a5060] pl-1 font-mono text-[10px] ${k.midnight ? 'text-white' : 'text-admin-text-muted'}" style="left:${w.pct(k.ms).toFixed(3)}%">${esc(text)}</span>`;
        }).join('');
        // Отступ = рамка зоны (1) + её поле (8/12) + поле дорожки (4): шкала ровно над полосами.
        return `<div class="${LANE_GRID} px-[13px] sm:px-[17px]" aria-hidden="true">
            <span class="hidden self-end pb-1 text-[11px] font-bold uppercase tracking-[.08em] text-admin-text-muted lg:block">Служба</span>
            <div class="${BAR_CELL} relative h-9 overflow-hidden">
                <span class="absolute top-0 -translate-x-1/2 whitespace-nowrap text-[11px] font-bold text-white" style="left:${w.nowPct.toFixed(3)}%">сейчас ${esc(M.hm(d.now))}</span>
                ${labels}
            </div>
            <span class="hidden self-end pb-1 text-[11px] font-bold uppercase tracking-[.08em] text-admin-text-muted lg:block">Следующий запуск</span>
        </div>`;
    }

    // Полоса одной службы: фон прошлое/будущее, полночи, метки прогонов, идущий, просрочка, план, «сейчас».
    function bar(d, w, j, ticks) {
        const nowMs = w.now;
        const parts = [];
        parts.push(`<span class="absolute inset-y-0 left-0 bg-admin-surface-hover" style="width:${w.nowPct.toFixed(3)}%"></span>`);
        parts.push(`<span class="absolute inset-y-0 right-0 bg-[#181b21]" style="left:${w.nowPct.toFixed(3)}%"></span>`);
        for (const k of ticks) if (k.midnight)
            parts.push(`<span class="absolute inset-y-0 w-px bg-[#4a5060]" style="left:${w.pct(k.ms).toFixed(3)}%"></span>`);

        if (j.timelineMode === 'ticker') {
            // Напоминания: пустые тики в историю не пишутся, поэтому ленту «тикает» за всё окно
            // не рисуем — только от последнего известного тика до «сейчас» и подпись об этом.
            if (j.active && j.lastRunAt && M.ms(j.lastRunAt) >= w.from) {
                const l = w.pct(M.ms(j.lastRunAt));
                parts.push(`<span class="absolute inset-y-0 min-w-[6px] bg-admin-success/30" style="left:${l.toFixed(3)}%;width:${(w.nowPct - l).toFixed(3)}%" title="последний тик ${esc(M.ago(j.lastRunAt, nowMs))}"></span>`);
            }
            const note = j.active
                ? `тикает раз в ${M.human(j.intervalSeconds || 300)} · пустые тики не хранятся${j.lastRunAt ? ` · последний ${M.ago(j.lastRunAt, nowMs)}` : ''}`
                : `${M.offReason(j)}${j.lastRunAt ? ` · последний тик ${M.ago(j.lastRunAt, nowMs)}` : ''}`;
            parts.push(`<span class="pointer-events-none absolute inset-y-0 left-2 flex items-center truncate text-[10px] text-admin-text-muted" style="max-width:calc(${w.nowPct.toFixed(3)}% - 1rem)">${esc(note)}</span>`);
        }

        // Просрочка: от «пора» до «сейчас».
        if (j.status === 'overdue' && j.overdueSince) {
            const l = w.pct(M.ms(j.overdueSince));
            parts.push(`<span class="absolute inset-y-0 bg-admin-warning/45" style="left:${l.toFixed(3)}%;width:${(w.nowPct - l).toFixed(3)}%" title="пора с ${esc(M.dayHm(j.overdueSince, nowMs))}"></span>`);
        }
        // Прогоны в окне.
        for (const r of j.windowRuns || []) {
            const start = M.ms(r.startedAt);
            if (!r.finishedAt) {
                const l = w.pct(start);
                parts.push(`<span class="absolute inset-y-0 min-w-[4px] bg-admin-accent" style="left:${l.toFixed(3)}%;width:${(w.nowPct - l).toFixed(3)}%" title="идёт с ${esc(M.hm(r.startedAt))}"></span>`);
                continue;
            }
            const cls = r.outcome === 'failed' ? 'bg-admin-danger' : 'bg-admin-success';
            parts.push(`<span class="absolute inset-y-0 w-[3px] -ml-px ${cls}" style="left:${w.pct(start).toFixed(3)}%" title="${esc(M.dayHm(r.startedAt, nowMs))} — ${r.outcome === 'failed' ? 'ошибка' : 'успех'}"></span>`);
        }
        // План (у start-list — ориентир: короче и бледнее).
        for (const p of j.planned || []) {
            const cls = j.nextApprox ? 'inset-y-[30%] bg-admin-text-muted/40' : 'inset-y-1 bg-admin-text-muted/55';
            parts.push(`<span class="absolute w-[2px] ${cls}" style="left:${w.pct(M.ms(p)).toFixed(3)}%" title="${j.nextApprox ? 'ориентир ' : 'план '}${esc(M.dayHm(p, nowMs))}"></span>`);
        }
        parts.push(`<span class="absolute inset-y-0 w-[2px] -ml-px bg-white" style="left:${w.nowPct.toFixed(3)}%"></span>`);
        return `<span class="${BAR_CELL} relative block h-[18px] overflow-hidden rounded 2xl:h-[26px]" aria-hidden="true">${parts.join('')}</span>`;
    }

    function lane(d, w, j, ticks) {
        const nowMs = w.now;
        const m = M.st(j.status);
        const next = M.nextText(j, nowMs);
        const sub = M.nextSub(j);
        return `<button type="button" data-job="${esc(j.key)}" aria-label="${esc(M.ariaJob(j, nowMs))}"
                class="${LANE_GRID} w-full cursor-pointer rounded-md border-0 bg-transparent px-1 py-1 text-left text-admin-text hover:bg-white/[.04] focus-visible:outline-2 focus-visible:outline-admin-accent 2xl:min-h-10">
            <span class="flex min-w-0 items-center gap-2" title="${esc(j.badge.text)}">
                ${M.statusIcon(j.status, false)}
                <span class="min-w-0 flex-1 truncate font-semibold text-white">${esc(j.title)}</span>
                <span class="hidden whitespace-nowrap text-[11px] font-semibold 2xl:inline ${m.text}">${esc(m.word)}</span>
            </span>
            ${bar(d, w, j, ticks)}
            <span class="flex min-w-0 max-w-[11rem] flex-col text-right lg:max-w-none lg:text-left">
                <span class="truncate font-mono text-xs ${next.cls}" title="${esc(next.text)}">${esc(next.text)}</span>
                ${sub ? `<span class="hidden truncate text-[11px] text-admin-text-muted 2xl:block" dir="auto" title="${esc(sub)}">${M.bidi(sub)}</span>` : ''}
            </span>
        </button>`;
    }

    // Служебные — плашки одной строкой (прогонов во времени у них нет).
    function servicePlaque(d, j) {
        const nowMs = M.ms(d.now);
        const m = M.st(j.status);
        const sum = M.summaryLine(j);
        return `<button type="button" data-job="${esc(j.key)}" aria-label="${esc(M.ariaJob(j, nowMs))}"
                class="flex min-w-0 cursor-pointer items-center gap-2 rounded-md border px-3 py-2 text-left text-admin-text hover:bg-admin-surface-hover focus-visible:outline-2 focus-visible:outline-admin-accent ${j.status === 'off' ? 'border-dashed border-admin-border bg-transparent' : 'border-admin-border bg-admin-surface'}">
            ${M.statusIcon(j.status, false)}
            <span class="min-w-0 truncate font-semibold text-white sm:shrink-0 sm:max-w-[60%]">${esc(j.title)}</span>
            <span class="hidden min-w-0 flex-1 truncate text-xs text-admin-text-muted sm:block" dir="auto">${M.bidi(j.status === "off" ? M.offReason(j) : sum)}</span>
            <span class="ml-auto whitespace-nowrap font-mono text-[11px] ${m.text}">${esc(j.status === 'off' ? 'выключена' : M.mainLine(j, nowMs))}</span>
        </button>`;
    }

    function zoneBlock(d, w, zone, ticks) {
        const z = M.zone(zone.key, d.environment);
        const byKey = Object.fromEntries(d.jobs.map(j => [j.key, j]));
        const jobs = zone.jobKeys.map(k => byKey[k]).filter(Boolean);
        const kinds = M.KINDS.filter(k => jobs.some(j => j.kind === k.key));
        const showSub = kinds.length > 1;
        const groups = kinds.map(k => {
            const list = jobs.filter(j => j.kind === k.key);
            const label = showSub ? `<h3 class="m-0 mt-2 px-1 text-[11px] font-bold uppercase tracking-[.08em] text-admin-text-muted">${esc(k.title)}</h3>` : '';
            if (k.key === 'service' || list.every(j => j.timelineMode === 'none'))
                return `${label}<div class="mt-1 grid gap-2 pb-1 md:grid-cols-2 xl:grid-cols-3">${list.map(j => servicePlaque(d, j)).join('')}</div>`;
            return `${label}<div class="flex flex-col">${list.map(j => lane(d, w, j, ticks)).join('')}</div>`;
        }).join('');
        return `<section class="rounded-xl border ${z.frame} px-2 pb-3 pt-2 sm:px-3" aria-label="${esc(z.title)}">
            <div class="flex flex-wrap items-center gap-x-2.5 gap-y-1 px-1 pb-1">
                <span class="inline-flex size-[22px] shrink-0 items-center justify-center rounded-md ${z.badge}">${M.icon(z.icon)}</span>
                <h2 class="m-0 whitespace-nowrap text-[15px] font-bold text-white">${esc(z.title)}</h2>
                <span class="min-w-0 truncate text-xs ${z.subText}">${esc(z.sub)}</span>
                <span class="ml-auto min-w-0">${M.pulse(zone, w.now)}</span>
            </div>
            ${groups}
        </section>`;
    }

    M.renderTimeline = function (d) {
        const w = windowOf(d);
        const ticks = axisTicks(w);
        const zones = d.zones.map(z => zoneBlock(d, w, z, ticks)).join('');
        return `<div class="flex flex-col gap-3">
            <div class="flex flex-col gap-2">
                ${axis(d, w)}
                ${zones}
            </div>
            <div class="grid gap-3 lg:grid-cols-3">
                ${M.renderRunning(d)}
                ${M.renderAttention(d)}
                ${M.renderUpcoming(d)}
            </div>
        </div>`;
    };

    // Легенда меток таймлайна — справа в строке счётчиков.
    M.timelineLegend = function () {
        const item = (sw, text) => `<span class="inline-flex items-center gap-1.5">${sw}${text}</span>`;
        return `<span class="flex flex-wrap items-center gap-x-3.5 gap-y-1 text-[11px] text-admin-text-muted" aria-label="Легенда меток">
            ${item('<span class="h-3 w-1 bg-admin-success"></span>', 'успех')}
            ${item('<span class="h-3 w-1 bg-admin-danger"></span>', 'ошибка')}
            ${item('<span class="size-3 bg-admin-accent"></span>', 'идёт')}
            ${item('<span class="size-3 bg-admin-warning/45"></span>', 'просрочка')}
            ${item('<span class="h-3 w-1 bg-admin-text-muted/55"></span>', 'план')}
        </span>`;
    };
})();
