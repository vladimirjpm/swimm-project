// /Admin/Jobs — общие кирпичики мониторинга (docs/admin-pages/jobs.md, «Как устроена страница»).
// Классический скрипт без сборщика: всё кладётся в window.JobsMon, остальные файлы папки берут
// отсюда формат времени, статусы, зоны и иконки. Tailwind-классы пишутся строками целиком —
// их собирает @source в Styles/admin.css (динамически склеенный класс в бандл не попадёт).
(function () {
    const M = window.JobsMon = window.JobsMon || {};

    // ── Экранирование ─────────────────────────────────────────────────────────────────────
    // esc() из _Layout не трогает числа и одинарные кавычки — здесь своя, на любой тип.
    M.esc = function (v) {
        if (v === null || v === undefined) return '';
        return String(v).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    };
    const esc = M.esc;

    // Экранирует и изолирует ивритские куски в русской строке («Старт «ליגה …» #16925 не взят»):
    // без <bdi> соседние «#16925» и кавычки алгоритм bidi приклеивает к RTL-куску и переставляет.
    const HEBREW_RUN = /[֐-׿](?:[֐-׿0-9\s"'״׳.,\-–/()]*[֐-׿])?/g;
    M.bidi = v => esc(v).replace(HEBREW_RUN, m => `<bdi>${m}</bdi>`);

    // ── Время ────────────────────────────────────────────────────────────────────────────
    // Сервер отдаёт UTC; показываем в местном времени браузера — как нынешняя страница
    // показывала время сервера (у Влада это одно и то же: Израиль). «Назад/через» считаем от
    // серверного now ответа, а не от часов браузера.
    M.ms = iso => (iso ? Date.parse(iso) : NaN);

    // То же правило, что JobTimeText.Human на сервере: «45 с», «20 мин», «1 ч 18 мин», «17 ч», «3 дн.».
    M.human = function (sec) {
        sec = Math.max(0, sec);
        if (sec < 60) return `${Math.floor(sec)} с`;
        const min = Math.floor(sec / 60);
        if (min < 60) return `${min} мин`;
        const hours = Math.floor(sec / 3600);
        if (sec < 2 * 86400) {
            const rest = min % 60;
            return rest === 0 || hours >= 10 ? `${hours} ч` : `${hours} ч ${rest} мин`;
        }
        return `${Math.floor(sec / 86400)} дн.`;
    };
    M.ago = function (iso, nowMs) {
        const d = (nowMs - M.ms(iso)) / 1000;
        return d < 0 ? 'только что' : `${M.human(d)} назад`;
    };
    M.until = (iso, nowMs) => `через ${M.human((M.ms(iso) - nowMs) / 1000)}`;
    M.dur = sec => (sec == null ? '—' : sec < 1 ? '<1 с' : sec < 60 ? `${Math.floor(sec)} с` : M.human(sec));

    const pad = n => String(n).padStart(2, '0');
    M.hm = iso => { const d = new Date(M.ms(iso)); return `${pad(d.getHours())}:${pad(d.getMinutes())}`; };
    M.hms = ms => { const d = new Date(ms); return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`; };
    M.full = iso => { const d = new Date(M.ms(iso)); return `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()} ${M.hm(iso)}`; };
    const dayKey = ms => { const d = new Date(ms); return d.getFullYear() * 400 + d.getMonth() * 32 + d.getDate(); };
    // «14:20», «вчера 13:02», «завтра 08:02», «17.11 07:30» — относительно серверного now.
    M.dayHm = function (iso, nowMs) {
        const t = M.ms(iso);
        const day = 86400000;
        if (dayKey(t) === dayKey(nowMs)) return M.hm(iso);
        if (dayKey(t) === dayKey(nowMs - day)) return `вчера ${M.hm(iso)}`;
        if (dayKey(t) === dayKey(nowMs + day)) return `завтра ${M.hm(iso)}`;
        const d = new Date(t);
        return `${pad(d.getDate())}.${pad(d.getMonth() + 1)} ${M.hm(iso)}`;
    };
    const WEEKDAYS = ['вс', 'пн', 'вт', 'ср', 'чт', 'пт', 'сб'];
    M.dayLabel = ms => { const d = new Date(ms); return `${WEEKDAYS[d.getDay()]} ${pad(d.getDate())}.${pad(d.getMonth() + 1)}`; };

    // ── Иконки (пути — из макетов дизайнера) ──────────────────────────────────────────────
    const PATHS = {
        x: 'M6 6l12 12M18 6L6 18',
        clock: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM12 7v5l3 2',
        play: 'M8 5l11 7-11 7z',
        check: 'M5 12.5l4.5 4.5L19 7.5',
        dash: 'M5 12h14',
        power: 'M12 3v8M6.3 6.3a8 8 0 1 0 11.4 0',
        person: 'M12 4a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM4 21a8 8 0 0 1 16 0',
        cloud: 'M7 18a4 4 0 0 1-.5-7.97A6 6 0 0 1 18 9.5a4.25 4.25 0 0 1 .5 8.5z',
        monitor: 'M3 5h18v11H3zM8 20h8M12 16v4',
        server: 'M4 4h16v6H4zM4 14h16v6H4zM8 7h.01M8 17h.01',
        refresh: 'M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7',
        warn: 'M12 4l9 16H3zM12 10v4M12 17v.5',
        lock: 'M6 11h12v9H6zM9 11V8a3 3 0 0 1 6 0v3',
    };
    // Иконка всегда декоративная: смысл несёт подпись рядом (статус — цветом И словом).
    M.icon = (name, cls = 'size-3.5') =>
        `<svg class="${cls} shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${PATHS[name] || PATHS.dash}"/></svg>`;

    // ── Статусы ─────────────────────────────────────────────────────────────────────────
    // Шесть статусов контракта + «ждёт тебя» (не статус службы — счётчик решений человека).
    M.STATUS = {
        failed: { word: 'упала', icon: 'x', text: 'text-admin-danger', bg: 'bg-admin-danger', tile: 'border-admin-danger/60 bg-admin-danger/10', kindWord: 'Упала' },
        overdue: { word: 'просрочена', icon: 'clock', text: 'text-admin-warning', bg: 'bg-admin-warning', tile: 'border-admin-warning/60 bg-admin-warning/10', kindWord: 'Просрочена' },
        running: { word: 'идёт', icon: 'play', text: 'text-admin-accent', bg: 'bg-admin-accent', tile: 'border-admin-accent/60 bg-admin-accent/10', kindWord: 'Идёт' },
        ok: { word: 'в норме', icon: 'check', text: 'text-admin-success', bg: 'bg-admin-success', tile: 'border-admin-border bg-admin-surface', kindWord: 'В норме' },
        idle: { word: 'ждёт задания', icon: 'dash', text: 'text-admin-success', bg: 'bg-admin-success', tile: 'border-admin-border bg-admin-surface', kindWord: 'Ждёт задания' },
        off: { word: 'выключена', icon: 'power', text: 'text-admin-text-muted', bg: 'bg-admin-text-muted', tile: 'border-dashed border-admin-border bg-transparent', kindWord: 'Выключена' },
        waiting: { word: 'ждёт тебя', icon: 'person', text: 'text-white', bg: 'bg-admin-text', tile: 'border-admin-border bg-admin-surface', kindWord: 'Ждёт тебя' },
    };
    M.st = s => M.STATUS[s] || M.STATUS.off;

    // Иконка статуса в цветном кружке (плитки, панели): тёмная иконка на заливке цвета статуса.
    M.statusDot = (s, size = 'size-[22px]') => {
        const m = M.st(s);
        return `<span class="${size} ${m.bg} inline-flex shrink-0 items-center justify-center rounded-full text-admin-bg">${M.icon(m.icon, 'size-3')}</span>`;
    };
    // Иконка статуса без кружка цветом статуса + скрытая подпись для читалок.
    M.statusIcon = (s, withSr = true) => {
        const m = M.st(s);
        return `<span class="inline-flex ${m.text}">${M.icon(m.icon)}</span>${withSr ? `<span class="sr-only">${m.word}</span>` : ''}`;
    };

    // ── Зоны ────────────────────────────────────────────────────────────────────────────
    // Состав зон приходит в JSON (zones[]); здесь только как каждую рисовать. Неизвестная
    // третья зона получает нейтральную рамку цвета акцента — схема её выдерживает.
    M.zone = function (key, env) {
        if (key === 'prod') return {
            key, title: 'ПРОД · Azure', short: 'прод', icon: 'cloud',
            sub: 'круглосуточно · отсюда только наблюдаю',
            text: 'text-admin-env-prod', subText: 'text-admin-env-prod/80',
            frame: 'border-admin-env-prod/45 bg-admin-env-prod/6', badge: 'bg-admin-env-prod text-admin-bg',
        };
        if (key === 'local') return {
            key, title: `LOCAL · ${env || 'этот процесс'}`, short: 'Local', icon: 'monitor',
            sub: env === 'ProdAdmin' ? 'этот компьютер · работает, пока ProdAdmin открыт' : 'этот компьютер · локальная база',
            text: 'text-admin-env-local', subText: 'text-admin-env-local/80',
            frame: 'border-admin-env-local/45 bg-admin-env-local/6', badge: 'bg-admin-env-local text-admin-bg',
        };
        return {
            key, title: String(key).toUpperCase(), short: key, icon: 'server', sub: '',
            text: 'text-admin-accent', subText: 'text-admin-accent/80',
            frame: 'border-admin-accent/45 bg-admin-accent/6', badge: 'bg-admin-accent text-admin-bg',
        };
    };
    // Маленькая метка зоны «☁ прод» / «🖥 Local» — в панелях, журнале, ближайших запусках.
    M.zoneTag = (key, env, withText = true) => {
        const z = M.zone(key, env);
        return `<span class="inline-flex items-center gap-1 text-[11px] font-semibold ${z.text}" title="${esc(z.title)}">${M.icon(z.icon, 'size-3')}${withText ? esc(z.short) : `<span class="sr-only">${esc(z.short)}</span>`}</span>`;
    };

    // Подгруппы внутри зоны — по виду службы (JobKinds).
    M.KINDS = [
        { key: 'schedule', title: 'Расписания' },
        { key: 'queue', title: 'Очереди · по кнопке' },
        { key: 'service', title: 'Служебные' },
    ];

    // Пульс зоны: у прода — подпись процесса и «жив, тик N мин назад» (последний тик
    // напоминаний), у Local — подпись и «запущен в HH:MM».
    M.pulse = function (z, nowMs) {
        if (!z) return '';
        let alive = true, text;
        if (z.pulseKind === 'tick') {
            alive = !!z.pulseAt && nowMs - M.ms(z.pulseAt) < 35 * 60000;
            text = z.pulseAt ? `${alive ? 'жив' : 'молчит'}, тик ${M.ago(z.pulseAt, nowMs)}` : 'тиков нет';
        } else {
            text = z.pulseAt ? `запущен в ${M.dayHm(z.pulseAt, nowMs)}` : 'время запуска неизвестно';
        }
        const proc = z.process ? `<span class="hidden md:inline">${esc(z.process)} · </span>` : '';
        return `<span class="inline-flex min-w-0 items-center gap-1.5 font-mono text-[11px] text-admin-text-muted">
            <span class="size-2 shrink-0 rounded-full ${alive ? 'bg-admin-success' : 'bg-admin-warning'}" aria-hidden="true"></span>
            <span class="truncate">${proc}${esc(text)}</span></span>`;
    };

    // ── Тексты о службе: общие для плиток, дорожек и панели деталей ──────────────────────
    const firstLine = s => (s || '').split(/\r?\n/)[0];
    // Причина «выключена» — первая часть короткого статуса: «выключена настройкой (X)».
    M.offReason = j => (j.statusText || '').split(';')[0];
    M.runningSince = function (j) {
        if (j.claim && j.claim.live) return j.claim.since;
        if (j.stateSince) return j.stateSince;
        const open = (j.windowRuns || []).find(r => !r.finishedAt);
        return open ? open.startedAt : null;
    };

    // Главная строка плитки: «6 ч назад» / «идёт 3 мин» / «упала 49 мин назад» / «просрочена 1 ч 18 мин».
    M.mainLine = function (j, nowMs) {
        switch (j.status) {
            case 'running': {
                const since = M.runningSince(j);
                return since ? `идёт ${M.human((nowMs - M.ms(since)) / 1000)}` : 'идёт';
            }
            case 'failed': return j.lastRunAt ? `упала ${M.ago(j.lastRunAt, nowMs)}` : j.badge.text;
            case 'overdue': return j.overdueSeconds != null ? `просрочена ${M.human(j.overdueSeconds)}` : j.badge.text;
            case 'off': return 'выключена';
            case 'idle': return 'ждёт задания';
            default:
                if (j.kind === 'service' && j.stateSince) return `${j.badge.text} с ${M.dayHm(j.stateSince, nowMs)}`;
                return j.lastRunAt ? M.ago(j.lastRunAt, nowMs) : j.badge.text;
        }
    };

    // Итог одной строкой: ошибка, итог последнего прогона, у выключенной — почему выключена.
    M.summaryLine = function (j) {
        if (j.status === 'off') return M.offReason(j);
        if (j.kind === 'queue' && j.progress) return M.progressText(j.progress);
        if (j.lastError && j.status === 'failed') return `ошибка: ${firstLine(j.lastError)}`;
        if (j.kind === 'queue' && j.queue && !j.lastSummary) return `выполнено ${j.queue.completed} · упало ${j.queue.failed}`;
        return j.lastSummary || '';
    };

    M.progressText = p => p ? [`${fmtN(p.done)} из ${fmtN(p.total)} ${p.unit}`, p.current ? `сейчас ${p.current}` : null, p.note]
        .filter(Boolean).join(' · ') : '';
    M.progressPct = p => (p && p.total > 0 ? Math.min(100, Math.round(p.done * 100 / p.total)) : null);

    // Колонка «следующий запуск» дорожки: текст и цвет.
    M.nextText = function (j, nowMs) {
        if (j.status === 'running')
            return j.progress ? { text: `${fmtN(j.progress.done)} из ${fmtN(j.progress.total)} ${j.progress.unit}`, cls: 'text-admin-accent' }
                : { text: 'идёт сейчас', cls: 'text-admin-accent' };
        if (j.status === 'off') return { text: M.offReason(j), cls: 'text-admin-text-muted' };
        if (j.kind === 'queue') return { text: `в очереди ${j.queue ? j.queue.queued : 0}`, cls: 'text-admin-text' };
        if (j.status === 'overdue')
            return { text: j.overdueSeconds != null ? `просрочена на ${M.human(j.overdueSeconds)}` : j.badge.text, cls: 'text-admin-warning' };
        if (j.nextAt && j.nextIsRetry)
            return { text: M.ms(j.nextAt) > nowMs ? `повтор ${M.until(j.nextAt, nowMs)}` : 'повтор — при ближайшей проверке', cls: 'text-admin-danger' };
        if (j.dueNow) return { text: 'пора — при ближайшей проверке', cls: 'text-admin-text' };
        if (j.nextAt && M.ms(j.nextAt) > nowMs) return { text: `${j.nextApprox ? '≈ ' : ''}${M.until(j.nextAt, nowMs)}`, cls: 'text-admin-text' };
        return { text: '—', cls: 'text-admin-text-muted' };
    };
    // Вторая строка той же колонки (широкий экран): итог прошлого прогона.
    M.nextSub = function (j) {
        if (j.kind === 'queue') {
            const q = j.queue ? `выполнено ${j.queue.completed} · упало ${j.queue.failed}` : '';
            return j.progress && j.progress.note ? [j.progress.current ? `сейчас ${j.progress.current}` : null, j.progress.note].filter(Boolean).join(' · ') : q;
        }
        if (j.status === 'running' && j.lastSummary) return `прошлый: ${j.lastSummary}`;
        if (j.status === 'off') return j.lastSummary || '';
        return M.summaryLine(j);
    };

    // Низ плитки: «был HH:MM» / «след. HH:MM».
    M.wasText = (j, nowMs) => (j.lastRunAt ? M.dayHm(j.lastRunAt, nowMs) : 'не было');
    M.nextShort = function (j, nowMs) {
        if (j.status === 'running') return 'после прогона';
        if (j.status === 'off') return 'не запустится';
        if (j.status === 'overdue' || j.dueNow) {
            const at = j.overdueSince || j.nextAt;
            return at ? `пора с ${M.dayHm(at, nowMs)}` : 'пора';
        }
        if (j.nextAt) return `${j.nextApprox ? '≈' : ''}${M.dayHm(j.nextAt, nowMs)}`;
        return '—';
    };
    M.intervalText = j => (j.intervalSeconds ? `раз в ${M.human(j.intervalSeconds)}` : '');

    // Подпись для aria-label плитки/дорожки: «Входящие isr.org.il — в норме, 6 ч назад».
    M.ariaJob = (j, nowMs) => `${j.title} — ${M.st(j.status).word}, ${M.mainLine(j, nowMs)}. Открыть подробности`;

    // Пустое состояние одной строкой — без рамок с нулями.
    M.empty = text => `<p class="m-0 py-1 text-xs text-admin-text-muted">${esc(text)}</p>`;
    // Заголовок панели: «ТРЕБУЕТ ВНИМАНИЯ · 3» (число только если есть).
    M.panelTitle = (title, n, cls) =>
        `<h2 class="m-0 mb-2.5 text-[11px] font-bold uppercase tracking-[.08em] ${cls}">${esc(title)}${n ? ` · ${n}` : ''}</h2>`;
})();
