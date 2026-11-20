// Блок «Jobs» дашборда /Admin (docs/admin-pages/index.md, раздел «Блок Jobs»).
// Рисует строку блока (пилюли + «карта служб») и панель деталей (все службы по зонам) из ОДНОГО ответа
// GET /api/admin/jobs/monitor (контракт — docs/admin-pages/jobs.md). Макет — !design_handoff/
// design_handoff_admin_jobs_monitor/dashboard-jobs-block.html.
// Классический скрипт: грузится ДО dashboard.js, отдаёт window.AdminDashJobs.build(monitor, highlightKey).
// esc() и fmtN() — глобальные, из _Layout.cshtml. Классы Tailwind собирает @source по wwwroot/js/admin/**.
(function () {
    // Иконки статуса (пути из макета). stroke-width задаёт вызывающий.
    const IC = {
        failed:  'M6 6l12 12M18 6L6 18',
        overdue: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 7v5l3 2',
        running: 'M8 5l11 7-11 7z',
        ok:      'M5 12.5l4.5 4.5L19 7.5',
        idle:    'M5 12h14',
        off:     'M12 3v8M6.3 6.3a8 8 0 1 0 11.4 0',
    };
    const ZONE_IC = {
        prod:  'M7 18a4 4 0 0 1-.5-7.97A6 6 0 0 1 18 9.5a4.25 4.25 0 0 1 .5 8.5z',   // облако
        local: 'M3 5h18v11H3zM8 20h8M12 16v4',                                       // монитор
    };
    const ZONE_ICON_CLS = { prod: 'text-admin-env-prod', local: 'text-admin-env-local' };
    const ZONE_BOX_CLS  = { prod: 'bg-admin-env-prod', local: 'bg-admin-env-local' };

    // Полные литералы классов — иначе Tailwind их не увидит.
    const TEXT_CLS = {
        failed: 'text-admin-danger', overdue: 'text-admin-warning', running: 'text-admin-accent',
        ok: 'text-admin-success', idle: 'text-admin-success', off: 'text-admin-text-muted',
    };
    const TILE_CLS = {
        failed:  'border-admin-danger bg-admin-danger text-admin-bg',
        overdue: 'border-admin-warning bg-admin-warning text-admin-bg',
        running: 'border-admin-accent bg-admin-accent text-admin-bg',
        ok:      'border-admin-success/50 bg-admin-success/20 text-admin-success',
        idle:    'border-admin-success/50 bg-admin-success/20 text-admin-success',
        off:     'border-dashed border-[#6b7080] bg-transparent text-admin-text-muted',
    };
    const KIND_TITLE = { schedule: 'Расписания', queue: 'Очереди · по кнопке', service: 'Служебные' };

    function svg(path, cls, sw) {
        return `<svg class="${cls}" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="${sw}" stroke-linecap="round" stroke-linejoin="round"><path d="${path}"></path></svg>`;
    }
    const stIcon = (st, cls, sw) => svg(IC[st] || IC.off, cls, sw);

    // --- Время: «сейчас» — серверное (monitor.now), не часы браузера ---
    function dur(sec, coarse) {
        sec = Math.max(0, Math.round(sec));
        if (sec < 60) return `${sec} с`;
        const min = Math.floor(sec / 60);
        if (min < 60) return `${min} мин`;
        const h = Math.floor(min / 60), m = min % 60;
        if (h < 24) return (m && !(coarse && h >= 3)) ? `${h} ч ${m} мин` : `${h} ч`;
        const d = Math.floor(h / 24), hh = h % 24;
        return (hh && !coarse) ? `${d} д ${hh} ч` : `${d} д`;
    }
    const hhmm = iso => new Date(iso).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' });

    function stateText(j, now) {
        const ago = iso => `${dur((now - Date.parse(iso)) / 1000, true)} назад`;
        switch (j.status) {
            case 'running': {
                const since = j.claim?.since || j.stateSince;
                return since ? `идёт ${dur((now - Date.parse(since)) / 1000)}` : (j.statusText || 'идёт');
            }
            case 'failed':  return j.lastRunAt ? `упала ${ago(j.lastRunAt)}` : (j.statusText || 'упала');
            case 'overdue': return j.overdueSeconds != null ? `просрочена ${dur(j.overdueSeconds)}` : (j.statusText || 'просрочена');
            case 'off':     return 'выключена';
            case 'ok':      return (j.kind === 'schedule' && j.lastRunAt) ? ago(j.lastRunAt) : (j.statusText || 'в норме');
            default:        return j.statusText || 'ждёт задания';
        }
    }
    // Правая колонка панели — следующий запуск (у очередей — прогресс/очередь).
    function nextText(j, now) {
        if (j.status === 'running') {
            if (j.progress && j.progress.total) return `${fmtN(j.progress.done)} из ${fmtN(j.progress.total)}${j.progress.unit ? ' ' + j.progress.unit : ''}`;
            return 'идёт сейчас';
        }
        if (j.status === 'off') return '';
        if (j.kind === 'queue') return j.queue ? `в очереди ${fmtN(j.queue.queued)}` : '';
        if (j.nextAt) {
            const left = (Date.parse(j.nextAt) - now) / 1000;
            if (left > 0) return `${j.nextIsRetry ? 'повтор ' : ''}${j.nextApprox ? '~' : ''}через ${dur(left, true)}`;
            return 'пора сейчас';
        }
        return '';
    }

    // --- Квадратик «карты служб» ---
    function tile(j, now, hl, firstOfGroup) {
        const label = `${j.title} — ${stateText(j, now)}`;
        const sel = hl === j.key ? ' ring-2 ring-admin-text ring-offset-1 ring-offset-admin-surface' : '';
        const gap = firstOfGroup ? ' ml-[7px]' : '';
        return `<button type="button" data-job-key="${esc(j.key)}" title="${esc(label)}" aria-label="${esc(label)}" aria-pressed="${hl === j.key}"
            class="w-[18px] h-[18px] p-0 box-border flex items-center justify-center rounded border cursor-pointer${gap}${sel} ${TILE_CLS[j.status] || TILE_CLS.off}">${stIcon(j.status, 'w-[11px] h-[11px]', 3)}</button>`;
    }

    // Группы одной зоны по kind, в порядке появления в zone.jobKeys.
    function groupByKind(jobs) {
        const groups = [];
        for (const j of jobs) {
            let g = groups.find(x => x.kind === j.kind);
            if (!g) groups.push(g = { kind: j.kind, jobs: [] });
            g.jobs.push(j);
        }
        return groups;
    }

    function zoneJobs(z, byKey) { return (z.jobKeys || []).map(k => byKey[k]).filter(Boolean); }
    function zoneTitle(z, env) {
        if (z.key === 'prod') return 'ПРОД · Azure';
        if (z.key === 'local') return `LOCAL · ${env}`;
        return String(z.key).toUpperCase();
    }
    function zonePulse(z, now) {
        if (!z.pulseAt) return '';
        if (z.pulseKind === 'tick') return `жив, тик ${dur((now - Date.parse(z.pulseAt)) / 1000, true)} назад`;
        if (z.pulseKind === 'started') return `запущен в ${hhmm(z.pulseAt)}`;
        return '';
    }

    // --- Строка блока ---
    function pill(v, label, numCls) {
        return `<span class="inline-flex items-center gap-1 bg-admin-bg border border-admin-border rounded-md px-1.5 py-[3px] text-[11px] whitespace-nowrap"><span class="font-bold text-xs ${numCls}">${fmtN(v)}</span><span class="text-admin-text-muted">${esc(label)}</span></span>`;
    }
    function zeroChip(label) {
        return `<span class="inline-flex items-center rounded-full border border-admin-success/35 px-2.5 py-0.5 text-[11px] font-semibold text-admin-success">✓ ${esc(label)} · 0</span>`;
    }
    function buildRowHtml(m, byKey, now, hl) {
        const c = m.counters || {};
        // «Упала» и «Просрочена» при нуле сворачиваются в зелёные чипы, «Ждёт тебя» и «Идут» — скрываются (решение Влада 20.11.2026).
        const pills = [
            c.failed  ? pill(c.failed, 'Упала', 'text-admin-danger')      : zeroChip('Упала'),
            c.overdue ? pill(c.overdue, 'Просрочена', 'text-admin-warning') : zeroChip('Просрочена'),
            c.waiting ? pill(c.waiting, 'Ждёт тебя', 'text-[#e8eaf0]')     : '',
            c.running ? pill(c.running, 'Идут', 'text-admin-accent')       : '',
        ].join('');

        const rows = (m.zones || []).map(z => {
            const jobs = zoneJobs(z, byKey);
            const tiles = groupByKind(jobs).map((g, gi) =>
                g.jobs.map((j, ji) => tile(j, now, hl, gi > 0 && ji === 0)).join('')).join('');
            const t = zoneTitle(z, m.environment);
            const active = jobs.filter(j => j.active).length;
            // Квадратики переносятся на следующую строку, если зона не помещается (Development: 14 служб в одной зоне; узкий экран).
            // Подпись зоны уходит на свою строку, если не помещается рядом с квадратиками.
            return `<div class="flex flex-wrap items-start justify-between gap-x-1.5 gap-y-0.5">
                <div class="flex items-start gap-1.5 min-w-0">
                    <span title="${esc(t)}" class="flex h-[18px] items-center ${ZONE_ICON_CLS[z.key] || 'text-admin-text-muted'}">${svg(ZONE_IC[z.key] || ZONE_IC.local, 'w-3.5 h-3.5 shrink-0', 2.2).replace('aria-hidden="true"', `role="img" aria-label="${esc(t)}"`)}</span>
                    <div class="flex flex-wrap gap-[3px] min-w-0">${tiles}</div>
                </div>
                ${z.key === 'prod' ? `<span class="ml-auto text-[11px] leading-[18px] text-admin-text-muted whitespace-nowrap">работают ${fmtN(active)} из ${fmtN(jobs.length)}</span>` : ''}
            </div>`;
        }).join('');

        return `<div class="flex flex-wrap gap-1 items-center">${pills}</div>${rows}`;
    }

    // --- Панель деталей ---
    function jobRow(j, now, hl) {
        const state = stateText(j, now);
        const problem = j.status === 'failed' || j.status === 'overdue' || j.status === 'running';
        const sel = hl === j.key ? ' bg-admin-surface-hover ring-1 ring-admin-accent/60' : '';
        return `<a href="/Admin/Jobs?job=${encodeURIComponent(j.key)}" data-job-row="${esc(j.key)}" title="${esc(`${j.title} — ${state}`)}"
            class="grid grid-cols-[16px_minmax(0,1fr)_auto_132px] gap-x-2 items-center h-[26px] shrink-0 px-2 rounded-md text-[13px] text-admin-text no-underline hover:bg-admin-surface-hover${sel}">
            <span class="flex ${TEXT_CLS[j.status] || TEXT_CLS.off}">${stIcon(j.status, 'w-3.5 h-3.5 shrink-0', 2.2)}</span>
            <span class="truncate text-white" dir="auto">${esc(j.title)}</span>
            <span class="text-xs whitespace-nowrap ${problem ? TEXT_CLS[j.status] : 'text-admin-text-muted'}">${esc(state)}</span>
            <span class="font-mono text-[11px] text-admin-text-muted truncate text-right">${esc(nextText(j, now))}</span>
        </a>`;
    }
    function buildDetailHtml(m, byKey, now, hl) {
        return (m.zones || []).map(z => {
            const jobs = zoneJobs(z, byKey);
            const groups = groupByKind(jobs);
            const body = groups.map(g =>
                (groups.length > 1 ? `<h4 class="mt-1.5 mb-0.5 px-2 text-[10px] font-normal tracking-[.04em] uppercase text-admin-text-muted">${esc(KIND_TITLE[g.kind] || g.kind)}</h4>` : '')
                + g.jobs.map(j => jobRow(j, now, hl)).join('')).join('');
            const pulse = zonePulse(z, now);
            return `<div class="flex items-center gap-2 mt-3 px-2 h-6">
                    <span class="w-5 h-5 rounded-[5px] flex items-center justify-center shrink-0 text-admin-bg ${ZONE_BOX_CLS[z.key] || 'bg-admin-text-muted'}">${svg(ZONE_IC[z.key] || ZONE_IC.local, 'w-3.5 h-3.5 shrink-0', 2.2)}</span>
                    <h3 class="m-0 flex-1 text-xs font-bold text-white">${esc(zoneTitle(z, m.environment))}</h3>
                    ${pulse ? `<span class="font-mono text-[11px] text-admin-text-muted"${z.process ? ` title="${esc(z.process)}"` : ''}>${esc(pulse)}</span>` : ''}
                </div>${body}`;
        }).join('');
    }

    // Блок для дашборда. m — ответ monitor (или null). hl — ключ подсвеченной службы.
    function build(m, hl) {
        if (!m) return { key: 'jobs', title: 'Jobs', href: '/Admin/Jobs', heroValue: '—',
                         heroSubs: 'сводка фоновых служб не загрузилась', metrics: [] };
        const now = Date.parse(m.now);
        const byKey = Object.fromEntries((m.jobs || []).map(j => [j.key, j]));
        const s = m.summary || {};
        const problems = (s.problems || []).length;
        const tone = s.tone === 'danger' ? 'text-admin-danger' : 'text-admin-warning';
        const failed24h = s.stats ? s.stats.failed24h : 0;
        return {
            key: 'jobs', title: 'Jobs', href: '/Admin/Jobs',
            heroValue: problems ? `<span class="${tone}">${fmtN(problems)}</span>` : '<span class="text-admin-success">✓</span>',
            heroSubs: `${problems ? `служб с проблемой: ${fmtN(problems)}` : 'все в порядке'} · работают ${fmtN(s.active)} из ${fmtN(s.total)} · ошибок за 24 ч: ${fmtN(failed24h)}`,
            metrics: [],
            rowHtml: buildRowHtml(m, byKey, now, hl),
            detailHtml: buildDetailHtml(m, byKey, now, hl),
        };
    }

    window.AdminDashJobs = { build };
})();
