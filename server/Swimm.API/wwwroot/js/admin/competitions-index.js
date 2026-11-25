// Скрипт страницы /Admin/Competitions (Pages/Admin/Competitions/Index.cshtml). Вынесен из разметки как есть (09.10.2026):
// классический скрипт, не модуль — функции из onclick в разметке остаются глобальными.
// Tailwind-классы отсюда собирает @source в Styles/admin.css.
    // ── Регламент соревнования (תקנון) ─────────────────────────────────────────
    // Файл не сохраняется: сервер читает его и возвращает НАХОДКИ С ЦИТАТАМИ. Галочки
    // ставим по ним автоматически, но решение остаётся за админом — он видит, по каким
    // строкам мы так решили, и может снять их перед «Сохранить».
    document.addEventListener('click', async (e) => {
        const btn = e.target.closest('.regulation-check');
        if (!btn) return;

        const compId = btn.dataset.comp;
        const input = document.querySelector(`.regulation-file[data-comp="${compId}"]`);
        const out = document.querySelector(`.regulation-result[data-comp="${compId}"]`);
        const file = input?.files?.[0];

        // Файл не выбран — тянем регламент сами: он лежит на loglig (ссылка «תקנון» на
        // странице соревнования). Загрузка файла остаётся запасным путём: ссылку ставят
        // не всем, а у соревнований вне «входящих» (PDF-импорт) loglig-id вообще нет.
        out.textContent = file ? 'Читаю регламент…' : 'Ищу регламент на loglig…';
        try {
            let data;
            if (file) {
                const form = new FormData();
                form.append('file', file);
                const res = await fetch('/api/admin/competitions/analyze-regulation', {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: { 'X-XSRF-TOKEN': _csrfToken },
                    body: form
                });
                data = await res.json();
                if (!res.ok) throw new Error(data.error || `HTTP ${res.status}`);
            } else {
                const orgCompId = Number(btn.dataset.org) || null;
                if (!orgCompId) throw new Error('У соревнования нет compID сайта — приложите PDF регламента.');
                const res = await fetch('/api/admin/discovery/regulation', {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': _csrfToken },
                    body: JSON.stringify({ orgCompId })
                });
                data = await res.json();
                if (!res.ok) throw new Error(data.error || `HTTP ${res.status}`);
            }

            applyRegulation(compId, data, out);
        } catch (err) {
            out.innerHTML = `<span class="text-admin-warning">${esc(err.message)}</span>`;
        }
    });

    function applyRegulation(compId, data, out) {
        if (data.error) {
            out.innerHTML = `<span class="text-admin-warning">${esc(data.error)}</span>`;
            return;
        }

        const form = document.querySelector(`.regulation-file[data-comp="${compId}"]`).closest('form');
        const setBox = (name, checked) => {
            const box = form.querySelector(`input[type=checkbox][name="${name}"]`);
            if (box) box.checked = checked;
        };

        // Медали → Awards; клубный зачёт есть → снимаем «зачёт не ведётся»; чемпионат → 🏆.
        if (data.hasMedals) setBox('isAward', true);
        if (data.hasClubStanding) setBox('clubPointsDisabled', false);
        if (data.isChampionship) setBox('isChampionship', true);

        const labels = {
            medals: 'медали → Awards',
            clubStanding: 'клубный зачёт → галочка «не ведётся» снята',
            championship: 'чемпионат Израиля → 🏆'
        };

        const findings = (data.findings || []).map(f => `
            <div class="py-0.5">
                <span class="text-admin-accent">${esc(labels[f.flag] || f.flag)}</span>
                <span class="text-admin-text-muted"> · нашёл «${esc(f.matched)}»</span>
                <div class="text-[10px] text-admin-text-muted" dir="rtl">${esc(f.quote)}</div>
            </div>`).join('');

        const src = data.url
            ? ` <a href="${esc(data.url)}" target="_blank" rel="noopener" class="text-admin-accent hover:underline">регламент ↗</a>`
            : '';
        out.innerHTML = findings
            ? `<div class="text-admin-text">Галочки проставлены — проверьте и нажмите «Сохранить»:${src}</div>${findings}`
            : `<span class="text-admin-text-muted">В регламенте не нашлось ни медалей, ни клубного зачёта — галочки не трогал.${src}</span>`;
    }

        // Фильтры: выбор в селекте/чекбоксе сразу уходит в URL (сабмит формы) — чтобы кнопка
        // «назад» и возврат со страницы соревнования приводили ровно к тому же списку.
        // Пустые поля из query выкидываем (disabled не сериализуется) — URL остаётся читаемым.
        (function () {
            const form = document.getElementById('filters-form');
            if (!form) return;
            form.addEventListener('submit', () => {
                // Сезон и месяц уходят как fseason/fmonth: «поиск — по всем сезонам» решает сервер
                // (CompetitionListUrl.FormRedirect), чтобы работало и без JS.
                form.querySelectorAll('input[name], select[name]').forEach(el => {
                    if (el.type === 'checkbox') { if (!el.checked) el.disabled = true; }
                    else if (el.value === '') el.disabled = true;
                });
                // Смена фильтров = новая выборка: страницу пагинации сбрасываем (p не в форме).
            });
            form.querySelectorAll('select, input[type=checkbox]').forEach(el =>
                el.addEventListener('change', () => form.requestSubmit()));
        })();

        // Выбор строки списка (вариант E, этап 3): клик по строке открывает её панель справа.
        // Выбор живёт в адресе (?sel=), поэтому F5 и «назад» возвращают ту же панель; панели всех
        // строк страницы отрисованы сервером, JS только переключает видимую и пишет адрес
        // (pushState) — без перезагрузки. Без JS ссылка строки ведёт на тот же адрес.
        (function () {
            const rows = [...document.querySelectorAll('.comp-row[data-key]')];
            const box = document.getElementById('comp-detail');
            if (!rows.length || !box) return;

            // sel=s{N} находит и уже затянутую строку (ключ у неё c…/e…) — как CompetitionRowKey.Matches.
            // Без sel в адресе справа — автовыбор сервера: первая «ждёт затягивания» (data-auto-key).
            const keyFor = sel => {
                if (!sel) return box.dataset.autoKey || null;
                if (rows.some(r => r.dataset.key === sel)) return sel;
                const m = /^s(\d+)$/i.exec(sel);
                return m ? (rows.find(r => r.dataset.discId === m[1])?.dataset.key ?? null) : null;
            };

            function show(key) {
                const panel = key ? document.getElementById('panel-' + key) : null;
                box.querySelectorAll('.row-panel').forEach(p => p.classList.toggle('hidden', p !== panel));
                document.getElementById('panel-none')?.classList.toggle('hidden', !!panel);
                rows.forEach(r => r.dataset.key === key && panel
                    ? r.setAttribute('aria-current', 'true')
                    : r.removeAttribute('aria-current'));
                box.scrollTop = 0;
                // Правая колонка липкая; если страницу прокрутили выше её начала — подтянуть.
                if (box.getBoundingClientRect().top < 0) box.scrollIntoView({ block: 'start' });
                // Пачка (этап 5) слушает: спрятали её — показать строку возврата над списком.
                document.dispatchEvent(new CustomEvent('comp:panel', { detail: { key } }));
            }
            // Пачка открывается тем же show('bulk') (#panel-bulk — тоже .row-panel), «Закрыть» у неё
            // возвращает панель выбранной строки.
            window.compShow = show;
            window.compShowCurrent = () => show(keyFor(new URL(location.href).searchParams.get('sel')));

            rows.forEach(r => r.addEventListener('click', e => {
                if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
                e.preventDefault();
                if (r.getAttribute('aria-current') === 'true' && document.getElementById('panel-bulk')?.classList.contains('hidden') !== false) return;
                show(r.dataset.key);
                history.pushState({ sel: r.dataset.key }, '', r.href);
            }));

            window.addEventListener('popstate', () =>
                show(keyFor(new URL(location.href).searchParams.get('sel'))));
        })();

        // Дни многодневного события (вариант E, этап 6; хэндофф 3.5): шаги 1–4 общие, шаги 5–9 — у
        // выбранного дня (.ck-scope[data-day]). Быстрая правка, «Перезатянуть», «Качество события»,
        // ход затягивания и превью — в общей части панели (они про всё событие) и при смене дня не двигаются.
        document.addEventListener('click', (e) => {
            const btn = e.target.closest('.ck-day-btn');
            if (!btn) return;
            const panel = btn.closest('.row-panel');
            const day = btn.dataset.day;
            panel.querySelectorAll('.ck-day-btn').forEach(b => b.setAttribute('aria-pressed', String(b === btn)));
            panel.querySelectorAll('.ck-scope').forEach(sc => sc.classList.toggle('hidden', sc.dataset.day !== day));
        });

        // Выпадающие «Ещё ▾» (шапка страницы и панели) и «Фильтры» — <details>: клик мимо закрывает.
        document.addEventListener('click', (e) => {
            document.querySelectorAll('details.ck-more[open], details.page-pop[open]').forEach(d => {
                if (!d.contains(e.target)) d.removeAttribute('open');
            });
        });

        // ── Ход одиночного затягивания — в правой панели строки (вариант E, этап 4) ──
        // Затягивание идёт минуты и в несколько шагов (скачать PDF → разобрать → превью → фоновый
        // job импорта → автопроверка качества). Раньше шаги жили в плавающей панели в углу экрана,
        // теперь — в чек-листе своей строки (у всех состояний с этапа 6): статусы и строки шагов 5–7,
        // у события — в выбранном дне. Тексты строк — прежней панели «Затягивание».
        //
        // Переживает F5 и переключение строк: весь ход (фаза, строки, ответ превью, выбор в превью,
        // jobId) лежит в sessionStorage под одним ключом. Превью на сервере живёт 60 минут по
        // previewId — после F5 блок превью собирается заново из сохранённого ответа, импорт уходит
        // с тем же previewId. Разбор, прерванный перезагрузкой, запускается заново сам.
        // Статусы шагов на каждую фазу посчитаны сервером (CompetitionChecklistRules → data-phases).
        window.pullFlow = (function () {
            const FLOW_KEY = 'pullFlow';        // текущий ход (переживает F5)
            const DONE_KEY = 'pullDone';        // итог, который надо показать после reload
            const PREVIEW_TTL_MS = 60 * 60 * 1000;
            const RESTART_PARSE_MS = 10 * 60 * 1000;

            const esc = (v) => String(v ?? '').replace(/[&<>"']/g, c =>
                ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
            const store = {
                get(k) { try { return JSON.parse(sessionStorage.getItem(k) ?? 'null'); } catch { return null; } },
                set(k, v) { try { sessionStorage.setItem(k, JSON.stringify(v)); } catch { /* приватный режим */ } },
                del(k) { try { sessionStorage.removeItem(k); } catch { /* приватный режим */ } },
            };

            const ICON = {
                run: '<span class="text-admin-accent">●</span>',
                ok: '<span class="text-admin-success">✓</span>',
                fail: '<span class="text-admin-danger">✕</span>',
                wait: '<span class="text-admin-text-muted">○</span>',
            };
            // Пометка в строке списка: видно, что строка A затягивается, пока справа открыта B.
            const MARK = {
                parsing: ['разбор…', 'bg-admin-accent/15 text-admin-accent'],
                preview: ['превью', 'bg-admin-accent/15 text-admin-accent'],
                importing: ['импорт…', 'bg-admin-accent/15 text-admin-accent'],
                'parse-failed': ['сбой', 'bg-admin-danger/15 text-admin-danger'],
                'import-failed': ['сбой', 'bg-admin-danger/15 text-admin-danger'],
                'preview-expired': ['превью устарело', 'bg-admin-warning/15 text-admin-warning'],
                nothing: ['∅ нет протокола', 'bg-white/[.08] text-admin-text-muted'],
            };
            const PHASE_TEXT = {
                parsing: 'скачиваю и разбираю протокол', preview: 'превью готово, ждёт «Импортировать»',
                importing: 'импорт идёт в фоне', 'parse-failed': 'разбор не удался',
                'import-failed': 'импорт не прошёл', 'preview-expired': 'превью устарело',
                nothing: 'тянуть нечего',
            };
            // Р116: спокойные ответы разбора «тянуть нечего» — только «все строки — неявка»
            // (NoStartProtocol.Marker) и настоящий пустой PDF («No competitions found» / «0 lines extracted»).
            // Это не сбой: шаг 5 решён, строка закрыта (сервер ставит «∅», после F5 — в «Закрыто»).
            // «Парсер не распознал ни одного результата… (B4)» сюда НЕ входит: это поломка разбора —
            // красный сбой с «!» и полным текстом (ревью PR #267); сервер, пачка и автопроверка
            // считают так же (23.11.2026) — «∅» не ставится.
            const NO_START_MARKER = 'все строки — неявка';
            const EMPTY_MARKERS = ['No competitions found', '0 lines extracted'];
            const nothingToPull = (message) => {
                const m = String(message ?? '').toLowerCase();
                if (m.includes(NO_START_MARKER)) return 'тянуть нечего: в протоколе одни неявки (NS)';
                return EMPTY_MARKERS.some(x => m.includes(x.toLowerCase())) ? 'тянуть нечего: протокол пуст' : null;
            };

            // { discId, name, org, repull, phase, startedAt, previewAt, p, form, jobId, lines:[{step,text,state}], captions:{} }
            let flow = null;

            // F5 обрывает запрос разбора («Failed to fetch»), и уходящая страница записала бы «сбой»
            // поверх хода — после pagehide ход не пишем: его продолжит новая страница.
            let leaving = false;
            addEventListener('pagehide', () => { leaving = true; });
            addEventListener('beforeunload', () => { leaving = true; });
            // Возврат из bfcache («Назад»): страница снова живая — иначе save() молча ничего не писал бы.
            addEventListener('pageshow', () => { leaving = false; });
            const save = () => { if (!leaving) flow ? store.set(FLOW_KEY, flow) : store.del(FLOW_KEY); };
            const rowOf = (discId) => document.querySelector(`.comp-row[data-disc-id="${discId}"]`);
            const panelOf = (discId) => {
                const row = rowOf(discId);
                return row ? document.getElementById('panel-' + row.dataset.key) : null;
            };
            const previewBox = () => document.getElementById('preview-box');

            // Статусы — из data-phases ближайшей области: у панели (шаги 1–4) и у .ck-scope (шаги 5–9;
            // у события — своя у каждого дня: отметки «неполные» и качество у дней разные).
            function setStatuses(panel, phase) {
                const cache = new Map();
                panel.querySelectorAll('.ck-step').forEach(li => {
                    const holder = li.closest('[data-phases]');
                    if (!cache.has(holder)) {
                        let map = {};
                        try { map = JSON.parse(holder?.dataset.phases || '{}'); } catch { /* без статусов */ }
                        cache.set(holder, map[phase] ?? map.none ?? []);
                    }
                    const s = cache.get(holder)[Number(li.dataset.step) - 1];
                    if (s) li.dataset.status = s;
                });
            }

            // Шаг видимой области: у события шаги 5–9 повторены по дням, ход идёт в выбранном.
            const stepOf = (panel, n) => [...panel.querySelectorAll(`.ck-step[data-step="${n}"]`)]
                .find(li => !li.closest('.ck-scope.hidden')) ?? null;

            function linesHtml(lines) {
                return lines.map(l => `
                    <li class="flex gap-2 ${l.state === 'fail' ? 'text-admin-danger' : l.state === 'wait' ? 'text-admin-text-muted' : l.good ? 'text-admin-success font-semibold' : 'text-admin-text'}"${l.splits ? ' data-splits-line' : ''}>
                        <span class="w-3.5 shrink-0">${ICON[l.state] ?? ''}</span>
                        <span class="min-w-0">${l.text}</span>
                    </li>`).join('');
            }

            function fillLog(ol, lines) {
                if (!ol) return;
                ol.innerHTML = linesHtml(lines);
                ol.classList.toggle('hidden', !lines.length);
            }

            // Панель без хода: статусы и подписи — как нарисовал сервер.
            function resetPanel(panel) {
                if (!panel.classList.contains('checklist')) return; // пачка (#panel-bulk) — не чек-лист
                panel.querySelectorAll('.ck-log').forEach(ol => fillLog(ol, []));
                setStatuses(panel, 'none');
                panel.querySelectorAll('.ck-caption').forEach(c => { c.textContent = c.dataset.default ?? ''; });
                panel.querySelectorAll('.ck-pull-btn').forEach(b => b.classList.remove('hidden'));
                // «Нет протокола»: подсветка снимается, копия в шаге 5 прячется (у «ждём протокол» её нет в .ck-noproto-step).
                panel.querySelectorAll('.ck-noproto:not([data-keep-pulse])').forEach(b => b.classList.remove('ck-noproto-pulse'));
                panel.querySelectorAll('.ck-noproto-step').forEach(b => b.classList.add('hidden'));
                const chip = panel.querySelector('.ck-state-chip');
                if (chip?.dataset.default !== undefined) chip.textContent = chip.dataset.default;
            }

            // Кнопки «Затянуть» / «Перезатянуть»: гаснут все, пока импорт
            // ставится или идёт, и у своей строки — пока идёт её разбор.
            function lockPullButtons() {
                const busy = importBusy();
                const own = flow ? panelOf(flow.discId) : null;
                document.querySelectorAll('#comp-detail .ck-pull-btn, #comp-detail button[onclick^="discPreview("]').forEach(b => {
                    const key = b.closest('.row-panel')?.dataset.key;
                    const discId = key ? document.querySelector(`.comp-row[data-key="${key}"]`)?.dataset.discId : null;
                    const byBulk = discId ? window.bulkPull?.blocks(discId) : null;
                    const lock = busy || !!byBulk || (!!own && b.closest('.row-panel') === own && flow.phase === 'parsing');
                    if (b.dataset.title === undefined) b.dataset.title = b.title;
                    b.disabled = lock;
                    b.title = lock ? (busy ? 'Идёт импорт — дождись его конца' : byBulk ?? 'Разбор этой строки уже идёт') : b.dataset.title;
                });
                const imp = document.getElementById('btn-import');
                if (imp && flow?.queuing) imp.disabled = true;
            }

            function render() {
                document.querySelectorAll('#comp-detail .row-panel').forEach(resetPanel);
                lockPullButtons();
                window.bulkPull?.paintOffers(); // пачку нельзя ставить, пока идёт одиночное
                document.querySelectorAll('.comp-row .flow-mark').forEach(m => m.remove());
                const elsewhere = document.getElementById('pull-elsewhere');
                elsewhere?.classList.add('hidden');
                const box = previewBox();
                if (!flow) { box?.classList.add('hidden'); return; }

                const mark = MARK[flow.phase];
                const row = rowOf(flow.discId);
                if (row && mark) {
                    row.querySelector('.row-marks')?.insertAdjacentHTML('afterbegin',
                        `<span class="flow-mark inline-block whitespace-nowrap rounded px-1.5 py-0.5 text-[10px] font-bold ${mark[1]}" title="Затягивание этой строки: ${esc(PHASE_TEXT[flow.phase])}">${mark[0]}</span>`);
                }

                const panel = panelOf(flow.discId);
                if (!panel) {
                    // Строки нет на этой странице списка (другой месяц, фильтры) — ход виден над панелью.
                    box?.classList.add('hidden');
                    if (elsewhere) {
                        const href = flow.org ? `/Admin/Competitions?q=${encodeURIComponent(flow.org)}&sel=s${flow.discId}` : null;
                        elsewhere.innerHTML = `Затягивание «<bdi>${esc(flow.name)}</bdi>»: ${esc(PHASE_TEXT[flow.phase] ?? '')}. `
                            + 'Строки нет в этом списке'
                            + (href ? ` — <a class="text-admin-accent hover:underline" href="${href}">открыть её</a>` : '') + '.';
                        elsewhere.classList.remove('hidden');
                    }
                    return;
                }

                const showPreview = flow.phase === 'preview';
                setStatuses(panel, flow.phase);
                // У многодневного события тянется весь протокол (все дни) — строки хода и превью живут в
                // общей части панели (.ck-event-flow), а не в выбранном дне, и не «прыгают» при смене дня.
                // Статусы и подписи шагов 5–7 — одинаковые у всех дней.
                const eventFlow = panel.querySelector('.ck-event-flow');
                if (eventFlow) {
                    fillLog(eventFlow.querySelector('.ck-log'), flow.lines);
                } else {
                    // Строки хода — только у текущего шага и у шага со сбоем: у сделанного итог в подписи.
                    // Исключение — «тянуть нечего» (Р116): шаг 5 сделан, но текст сервера («почему») виден.
                    for (let n = 5; n <= 7; n++) {
                        const li = stepOf(panel, n);
                        const open = ['current', 'attention'].includes(li?.dataset.status)
                            || (n === 5 && flow.phase === 'nothing');
                        fillLog(li?.querySelector('.ck-log'), open ? flow.lines.filter(l => l.step === n) : []);
                    }
                }
                Object.entries(flow.captions ?? {}).forEach(([n, text]) => {
                    panel.querySelectorAll(`.ck-step[data-step="${n}"] .ck-caption`).forEach(c => { c.textContent = text; });
                });
                // «Затянуть» / «Перезатянуть» — пока хода нет или он сорвался; в разборе, превью и импорте — только PDF.
                panel.querySelectorAll('.ck-pull-btn').forEach(b => b.classList.toggle('hidden',
                    ['parsing', 'preview', 'importing'].includes(flow.phase)));
                // Р116: строка уже закрыта сервером («∅ нет протокола») — так и называем её до F5.
                const chip = panel.querySelector('.ck-state-chip');
                // Исход «тянуть нечего» (Р116/пустой PDF; НЕ сбой B4): «Нет протокола» светится — это следующий шаг.
                // Подсвечено всё до следующего render(): клик перезагружает страницу, другая строка / новое
                // «Затянуть» меняют flow. После F5 flow 'nothing' сбрасывается (restore), подсветка не переживает.
                if (flow.phase === 'nothing') {
                    panel.querySelectorAll('.ck-noproto').forEach(b => b.classList.add('ck-noproto-pulse'));
                    stepOf(panel, 5)?.querySelectorAll('.ck-noproto-step').forEach(b => b.classList.remove('hidden'));
                }
                if (chip && flow.phase === 'nothing') {
                    chip.dataset.default ??= chip.textContent;
                    chip.textContent = 'закрыто · тянуть нечего';
                }
                if (box) {
                    const slot = eventFlow?.querySelector('.ck-preview-slot') ?? stepOf(panel, 6)?.querySelector('.ck-preview-slot');
                    if (slot && box.parentElement !== slot) slot.appendChild(box);
                    box.classList.toggle('hidden', !showPreview);
                }
            }

            /// Новая строка «в работе» у шага; предыдущая незакрытая считается успешной.
            function step(stepNo, text) {
                const last = flow.lines[flow.lines.length - 1];
                if (last && last.state === 'run') last.state = 'ok';
                flow.lines.push({ step: stepNo, text, state: 'run' });
            }
            function finish(state, text, stepNo) {
                const last = flow.lines[flow.lines.length - 1];
                if (last && last.state === 'run') last.state = state === 'fail' ? 'fail' : 'ok';
                if (text) flow.lines.push({ step: stepNo ?? last?.step ?? 5, text, state });
            }
            function commit(phase) {
                if (phase) flow.phase = phase;
                save();
                render();
            }

            // F5 и «назад» должны вернуть ту же строку: без sel в адресе справа автовыбор, а после
            // импорта он ушёл бы на следующую «ждёт затягивания». Адрес — ссылки строки: в нём уже
            // подставленные сезон и месяц (голый адрес с одним sel открыл бы все сезоны).
            function pinSelection(discId) {
                const row = rowOf(discId);
                if (!row || new URL(location.href).searchParams.get('sel')) return;
                history.replaceState({ sel: row.dataset.key }, '', row.href);
            }

            // Импорт поставлен или ставится (клик «Импортировать», ответа ещё нет): новый ход
            // заменил бы этот и потерял его jobId, а повтор той же строки поставил бы второй импорт.
            const importBusy = () => flow != null && (flow.phase === 'importing' || flow.queuing === true);

            /// «Затянуть» / «Перезатянуть». false — нельзя: идёт импорт (любой строки) или разбор этой же.
            /// sibling — Р124 «Затянуть этот день»: { logligId } соседа; тот же ход, тот же шаг 6.
            function start(discId, name, repull, sibling) {
                // Пачка (этап 5): два импорта одного старта одновременно быть не должно.
                const byBulk = window.bulkPull?.blocks(discId);
                if (byBulk) {
                    alert(byBulk + '.');
                    return false;
                }
                if (importBusy()) {
                    alert(`Сначала дождись импорта «${flow.name}» — он идёт в фоне.`);
                    return false;
                }
                if (flow?.phase === 'parsing' && flow.discId === String(discId)) {
                    alert('Разбор этой строки уже идёт — дождись превью.');
                    return false;
                }
                flow = {
                    discId: String(discId), name, repull: !!repull, org: window.__DISC?.[discId]?.orgCompId ?? null,
                    phase: 'parsing', startedAt: Date.now(), lines: [], captions: {}, sibling: sibling ?? null,
                };
                step(5, sibling ? `Проверяю соседа loglig № ${esc(sibling.logligId)} и разбираю его протокол…`
                    : repull ? 'Скачиваю протокол заново и разбираю…' : 'Скачиваю PDF с isr.org.il и разбираю…');
                pinSelection(discId);
                commit();
                return true;
            }

            // token — startedAt хода: ответ разбора, пришедший после нового «Затянуть», отбрасывается.
            const isActive = (discId, token) => flow != null && flow.discId === String(discId)
                && (token === undefined || flow.startedAt === token);

            function plural(n, one, few, many) {
                const t = n % 100, u = n % 10;
                return t >= 11 && t <= 14 ? many : u === 1 ? one : u >= 2 && u <= 4 ? few : many;
            }

            function previewReady(p) {
                const days = p.competitions?.length ?? 0;
                flow.p = p;
                flow.previewAt = Date.now();
                flow.form = null;
                finish('ok', `Распознано результатов: ${p.resultCount}`, 5);
                // Подпись шага 5 — как в макете: «533 результата · 1 день в файле».
                flow.captions[5] = `${p.resultCount} ${plural(p.resultCount, 'результат', 'результата', 'результатов')}`
                    + (days ? ` · ${days} ${plural(days, 'день', 'дня', 'дней')} в файле` : '');
                step(6, 'Жду: проверь превью и нажми «Импортировать»');
                commit('preview');
            }

            function parseFailed(message) {
                flow.p = null;
                const nothing = nothingToPull(message);
                if (nothing) {
                    // Р116: не «не удалось» — спокойный исход; подробность сервера — строкой хода.
                    finish('ok', esc(nothing), 5);
                    flow.lines.push({ step: 5, state: 'wait', text: esc(message) });
                    flow.captions[5] = nothing;
                    commit('nothing');
                    return;
                }
                finish('fail', `Не удалось разобрать: ${esc(message)}`, 5);
                commit('parse-failed');
            }

            function cancel() {
                flow = null;
                save();
                render();
            }

            const setCaption = (stepNo, text) => { if (flow) { flow.captions[stepNo] = text; commit(); } };
            const saveForm = (form) => { if (flow?.phase === 'preview') { flow.form = form; save(); } };
            const current = () => flow;

            /// Клик «Импортировать»: с этого момента новый start запрещён, кнопки затягивания погашены.
            function beginImport(discId) {
                if (!isActive(discId) || flow.phase !== 'preview') return false;
                flow.queuing = true;
                commit();
                return true;
            }
            /// «Импорт не запустился»: превью остаётся, ход снова свободен.
            function importNotStarted(discId) {
                if (!isActive(discId)) return;
                flow.queuing = false;
                commit();
            }

            function importQueued(jobId, discId) {
                // Ход сменился, пока POST был в пути (не должно случаться: start запрещён) —
                // чужому ходу jobId не отдаём.
                if (!isActive(discId)) return;
                flow.queuing = false;
                flow.jobId = jobId;
                flow.p = null; flow.form = null;   // превью больше не нужно — не держим его в хранилище
                step(7, 'Импорт поставлен в очередь…');
                commit('importing');
                watch(jobId);
            }

            // Опрос job'а. Импорт идёт в фоне, поэтому единственный честный признак конца —
            // состояние job'а, а не ответ на POST /import (он лишь ставит в очередь).
            async function watch(jobId) {
                const deadline = Date.now() + 10 * 60 * 1000;
                // После F5 строка «Импортирую…» уже может быть в ходе — второй раз не пишем.
                let running = flow.lines.some(l => l.text === 'Импортирую результаты…');

                while (Date.now() < deadline) {
                    await new Promise(r => setTimeout(r, 1500));
                    if (!flow || flow.jobId !== jobId) return; // ход сменился
                    let s;
                    try {
                        s = await fetch(`/api/admin/import/status/${jobId}`, { credentials: 'same-origin' })
                            .then(r => r.ok ? r.json() : null);
                    } catch { continue; } // сеть моргнула — пробуем дальше
                    if (!s) continue;

                    if (s.state === 'running' && !running) { running = true; step(7, 'Импортирую результаты…'); commit(); }

                    if (s.state === 'completed') {
                        const r = s.result ?? {};
                        store.set(DONE_KEY, {
                            discId: flow.discId, name: flow.name, org: flow.org,
                            created: r.created ?? 0, total: r.totalRows ?? 0,
                            skipped: r.skipped ?? 0, errors: r.errors ?? 0,
                            // Сверка с файлом (Д1): главный признак того, что импорт лёг верно.
                            recon: r.reconciliation ?? '', reconBad: r.reconciliationMismatches ?? 0,
                            // Прогон реестра проверок (Д5): гоняется автоматически в конце импорта.
                            checks: r.dataChecks ?? '', checkErr: r.dataCheckErrors ?? 0,
                            checkWarn: r.dataCheckWarnings ?? 0,
                            // Галочки «сомнительный» из превью — подтверждаем, что пометка легла.
                            suspect: r.suspectFlagged ?? 0,
                            // Автодоклейка промежуточных идёт ПОСЛЕ completed — после reload ждём её
                            // по тому же job'у (watchSplits), итог — у шага 9.
                            jobId, splits: s.splits ?? null,
                            // Р124: «Затянуть этот день» — день собран в событие (или почему нет).
                            followUp: r.followUp ?? null
                        });
                        finish('ok', 'Готово. Обновляю таблицу…', 7);
                        flow = null; save(); // итог уже в DONE_KEY
                        // День собран в событие — строка теперь событие (ключ e<id>): выбираем его, иначе карточка не откроется.
                        if (r.followUpEventId) {
                            const u = new URL(location.href);
                            u.searchParams.set('sel', 'e' + r.followUpEventId);
                            setTimeout(() => { location.href = u.toString(); }, 700);
                        } else setTimeout(() => location.reload(), 700);
                        return;
                    }
                    if (s.state === 'failed') {
                        finish('fail', `Импорт не прошёл: ${esc(s.error ?? 'причина неизвестна')}`, 7);
                        flow.lines.push({ step: 7, state: 'wait',
                            text: 'Подробности — в <a class="text-admin-accent hover:underline" href="/Admin/ImportHistory">Import History</a>.' });
                        commit('import-failed');
                        return;
                    }
                }

                finish('fail', 'Жду слишком долго — статус смотри в Import History.', 7);
                commit('import-failed');
            }

            // Итог после reload: строка уже «импортировано» (закрыто) — итог пишется под шагом 7 её
            // чек-листа. Проверка качества прошла сама в импорте: её ✓ / ⚑ N — у шага 8 и в строке списка.
            // Автодоклейка промежуточных за импортом: опрос того же job'а, пока splits не done; итог —
            // подписью шага 9 (у события — у всех дней: доклейка идёт по всему событию) и строкой
            // «Итога затягивания». После следующего F5 тот же итог рисует сервер — из аудита.
            async function watchSplits(d) {
                if (!d.jobId || !d.splits) return;
                const deadline = Date.now() + 25 * 60 * 1000;
                while (Date.now() < deadline) {
                    await new Promise(r => setTimeout(r, 4000));
                    let s;
                    try {
                        s = await fetch(`/api/admin/import/status/${d.jobId}`, { credentials: 'same-origin' })
                            .then(r => r.ok ? r.json() : null);
                    } catch { continue; }
                    const sp = s?.splits;
                    if (!sp) return; // процесс перезапущен или доклейка снята — итог покажет сервер
                    if (sp.state !== 'done') continue;

                    const failed = sp.kind === 'failed' || sp.kind === 'partial';
                    // «Доклеено» — зелёным с ✓ и жирнее (Влад, 24.11.2026); то же рисует сервер после F5.
                    const attached = sp.kind === 'attached';
                    const text = `${attached ? '✓ Промежуточные' : 'промежуточные'}: ${sp.text ?? ''} · само после импорта`;
                    const panel = panelOf(d.discId);
                    panel?.querySelectorAll('.ck-step[data-step="9"] .ck-caption').forEach(c => {
                        c.textContent = text;
                        c.title = sp.message ?? '';
                        c.classList.toggle('text-admin-danger', failed);
                        c.classList.toggle('text-admin-success', attached);
                        c.classList.toggle('font-semibold', attached);
                        c.classList.toggle('text-admin-text-muted', !failed && !attached);
                    });
                    if (failed) panel?.querySelectorAll('.ck-step[data-step="9"]').forEach(li => { li.dataset.status = 'attention'; });
                    document.querySelectorAll('[data-splits-line]').forEach(li => {
                        li.outerHTML = linesHtml([{ step: 9, text: esc('Промежуточные: ' + (sp.text ?? '')), state: failed ? 'fail' : 'ok', good: attached }]);
                    });
                    return;
                }
            }

            function showDone(d) {
                watchSplits(d);
                const lines = [{ step: 7, text: `Затянуто: создано ${d.created} из ${d.total}`, state: 'ok' }];
                if (d.skipped) lines.push({ step: 7, text: `пропущено ${d.skipped}`, state: 'wait' });
                if (d.errors) lines.push({ step: 7, text: `ошибок ${d.errors}`, state: 'fail' });
                // Сверка с файлом важнее счётчиков: она отвечает на вопрос «легло ли верно».
                if (d.recon) lines.push({ step: 7, text: esc(d.recon), state: d.reconBad ? 'fail' : 'ok' });
                if (d.followUp) lines.push({ step: 7, text: esc(d.followUp), state: d.followUp.startsWith('⚠') ? 'fail' : 'ok' });
                // Пометки из превью: молча применённая пометка так же плоха, как молча потерянная.
                if (d.suspect) lines.push({ step: 8, text: `Помечено сомнительными: ${d.suspect} (в рекорды не идут)`, state: 'ok' });
                if (d.checks) lines.push({ step: 8, text: esc(d.checks), state: d.checkErr ? 'fail' : (d.checkWarn ? 'wait' : 'ok') });
                // Промежуточные доклеиваются сами за импортом (решение Влада 23.11.2026): строка
                // меняется, когда доклейка кончится (watchSplits).
                if (d.splits) lines.push({ step: 9, text: 'Промежуточные: доклеиваются сами в фоне — итог у шага 9', state: 'wait', splits: true });

                const row = rowOf(d.discId);
                if (d.reconBad) {
                    lines.push({ step: 8, state: 'wait', text: 'Данные разошлись с протоколом. Разбор — <code>docs/data-integrity.md</code> §8; '
                        + 'чаще всего лечится повторным «Перезатянуть» с галкой «удалять лишние».' });
                } else if (d.checkErr || d.checkWarn) {
                    lines.push({ step: 8, state: 'wait', text: 'Проверки данных нашли, что разобрать — '
                        + '<a class="text-admin-accent hover:underline" href="/Admin/Health">Health</a>.' });
                }

                const panel = panelOf(d.discId);
                const title = '<li class="font-semibold text-white">Итог затягивания</li>';
                if (panel) {
                    // Итог — под шагом 7 «Импорт»; у события — в общей части панели (тянулись все дни).
                    const ol = panel.querySelector('.ck-event-flow .ck-log') ?? stepOf(panel, 7)?.querySelector('.ck-log');
                    if (ol) { ol.innerHTML = title + linesHtml(lines); ol.classList.remove('hidden'); }
                    if (!row.querySelector('.pulled-badge'))
                        row.querySelector('.row-marks')?.insertAdjacentHTML('beforeend',
                            '<span class="pulled-badge inline-block rounded px-1.5 py-0.5 text-[10px] font-bold uppercase bg-admin-success/20 text-admin-success">затянуто</span>');
                    row.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
                    return;
                }
                const elsewhere = document.getElementById('pull-elsewhere');
                if (!elsewhere) return;
                const href = d.org ? `/Admin/Competitions?q=${encodeURIComponent(d.org)}&sel=s${d.discId}` : null;
                elsewhere.innerHTML = `<div class="mb-1">«<bdi>${esc(d.name)}</bdi>»</div><ol class="space-y-1 text-[12px]">${title}${linesHtml(lines)}</ol>`
                    + '<div class="mt-1 text-[12px] text-admin-text-muted">Строка не видна при текущих фильтрах — сбрось их, чтобы увидеть её со стадией «импортировано»'
                    + (href ? ` (<a class="text-admin-accent hover:underline" href="${href}">открыть</a>)` : '') + '.</div>';
                elsewhere.classList.remove('hidden');
            }

            // После reload: итог, или продолжение хода с той фазы, где он был.
            function restore() {
                const done = store.get(DONE_KEY);
                if (done) { store.del(DONE_KEY); store.del(FLOW_KEY); showDone(done); return; }

                flow = store.get(FLOW_KEY);
                if (!flow) return;
                flow.lines ??= []; flow.captions ??= {};
                // «Тянуть нечего» (Р116): после F5 сервер уже показывает строку закрытой («∅») — ход не нужен.
                if (flow.phase === 'nothing') { flow = null; save(); render(); return; }

                if (flow.phase === 'parsing') {
                    // Запрос разбора оборвала перезагрузка — запускаем его заново (это только превью).
                    if (Date.now() - (flow.startedAt ?? 0) < RESTART_PARSE_MS && flow.sibling && typeof window.siblingPullRestart === 'function') {
                        window.siblingPullRestart(flow);
                    } else if (Date.now() - (flow.startedAt ?? 0) < RESTART_PARSE_MS && typeof window.discPreview === 'function') {
                        window.discPreview(flow.discId, flow.repull, true);
                    } else { flow = null; save(); }
                    return;
                }
                if (flow.phase === 'preview' && flow.queuing) {
                    // F5 между кликом «Импортировать» и ответом: ушёл ли импорт — не знаем (jobId не
                    // дошёл). Превью одноразовое: если ушёл, повторный клик ответит «превью не найдено».
                    flow.queuing = false;
                    finish('fail', 'Перезагрузка оборвала постановку импорта — ушёл ли он, смотри в '
                        + '<a class="text-admin-accent hover:underline" href="/Admin/ImportHistory">Import History</a>.', 6);
                }
                if (flow.phase === 'preview') {
                    if (!flow.p || Date.now() - (flow.previewAt ?? 0) > PREVIEW_TTL_MS) {
                        finish('fail', 'Превью устарело (живёт 60 минут) — нажми «Затянуть» ещё раз.', 6);
                        flow.p = null;
                        commit('preview-expired');
                        return;
                    }
                    render();
                    window.discRestorePreview?.(flow);
                    return;
                }
                if (flow.phase === 'importing') {
                    step(7, 'Импорт продолжается в фоне…');
                    commit();
                    watch(flow.jobId);
                    return;
                }
                render(); // сбои — видны, пока не нажмут «Затянуть» снова
            }

            return { start, isActive, previewReady, parseFailed, cancel, setCaption, saveForm, current,
                beginImport, importNotStarted, importQueued, restore, render, step, finish, commit };
        })();

        // ── Пачки «за день» / «за месяц» (вариант E, этап 5; docs/plans/bulk-pull-plan.md) ──
        // Забор идёт минутами и живёт на сервере: панель только поллит состояние. Пачка
        // показывается в правой области ВМЕСТО чек-листа (#panel-bulk); клик по строке списка
        // возвращает чек-лист, а к пачке ведёт строка #bulk-strip над списком.
        //
        // Кто попадает в пачку, решает сервер (BulkPullSelection): у каждой кнопки готовые id для
        // обоих положений чекбокса «включая чемпионаты», JS выбирает набор и шлёт его как есть.
        //
        // Переживает F5: batchId, подпись кнопки и «была ли пачка открыта» — в sessionStorage.
        // Пачка висит, пока её не закроют («Закрыть»), в том числе готовая.
        //
        // Сосуществование с одиночным «Затянуть» (pullFlow): пока пачка тянет или импортирует —
        // одиночное запрещено целиком; пока она открыта и в ней есть что импортировать — запрещено
        // для её строк. Пачку нельзя поставить, пока идёт одиночный разбор, превью или импорт.
        window.bulkPull = (function () {
            const API = '/api/admin/discovery';
            const KEY = 'admin-bulk-pull-batch';
            const META_KEY = 'admin-bulk-pull-meta';   // { label, n, ids } — что было на кнопке
            const SHOWN_KEY = 'admin-bulk-pull-shown'; // '1' — пачка была открыта справа
            // Состояние пользователя, которое должно пережить F5 и автоперезагрузку после импорта:
            // { checks: {id: bool} — галочки, которые трогали руками; seen: {id: ms} — когда строка
            // впервые пришла затянутой (превью живёт 60 минут); importResult: ответ bulk-import }.
            const STATE_KEY = 'admin-bulk-pull-state';
            const PREVIEW_TTL_MS = 60 * 60 * 1000;
            const panel = document.getElementById('panel-bulk');
            if (!panel) return null;

            const progress = document.getElementById('bulk-progress');
            const scopeBox = document.getElementById('bulk-scope');
            const skippedBox = document.getElementById('bulk-skipped');
            const rowsBox = document.getElementById('bulk-rows');
            const importBtn = document.getElementById('bulk-import-btn');
            const importOut = document.getElementById('bulk-import-result');
            const strip = document.getElementById('bulk-strip');
            // Строка «связь потеряна» — над ходом пачки; заводится тут, разметку не трогаем.
            const netNote = document.createElement('div');
            netNote.className = 'mb-2 hidden text-[12.5px] text-admin-warning';
            progress.before(netNote);
            let restoredShown = false;
            const stripText = document.getElementById('bulk-strip-text');
            const champBox = document.getElementById('bulk-include-champ');
            const offers = [...document.querySelectorAll('.bulk-offer')];
            const ss = {
                get(k) { try { return sessionStorage.getItem(k); } catch { return null; } },
                set(k, v) { try { sessionStorage.setItem(k, v); } catch { /* приватный режим */ } },
                del(k) { try { sessionStorage.removeItem(k); } catch { /* приватный режим */ } },
            };
            let timer = null;
            let batchId = ss.get(KEY);
            let meta = null;
            try { meta = JSON.parse(ss.get(META_KEY) ?? 'null'); } catch { meta = null; }
            let last = null;          // последний статус пачки с сервера
            let sawPending = false;   // в этой загрузке видели импорт в ходе — по его концу обновить список
            // Окна без ответа сервера считаются «пачка идёт»: от клика до ответа POST (иначе второй
            // клик по другой кнопке поставил бы вторую пачку) и после F5 до ответа GET (иначе
            // запреты пусты, пока статус не пришёл).
            let starting = false;
            let restoring = !!batchId;
            let state = {};
            try { state = JSON.parse(ss.get(STATE_KEY) ?? '{}') ?? {}; } catch { state = {}; }
            state.checks ??= {}; state.seen ??= {};
            const saveState = () => ss.set(STATE_KEY, JSON.stringify(state));
            const expired = r => !!r.previewId && !r.queued && state.seen[r.discoveredId]
                && Date.now() - state.seen[r.discoveredId] > PREVIEW_TTL_MS;

            function esc(v) {
                return String(v ?? '').replace(/[&<>"']/g, c =>
                    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
            }

            async function api(url, opts = {}) {
                opts.headers = { 'X-XSRF-TOKEN': _csrfToken, ...(opts.headers ?? {}) };
                const res = await fetch(url, { credentials: 'same-origin', ...opts });
                let body = null;
                try { body = await res.json(); } catch { /* пустое тело */ }
                if (!res.ok) {
                    const err = new Error(body?.error ?? `${res.status} ${res.statusText}`);
                    err.status = res.status;
                    throw err;
                }
                return body;
            }

            // Вердикты классификатора: значок, подпись и отмечать ли по умолчанию. Отмечены
            // беспроблемные и «без регламента» — последние импортируются, но помечены.
            const VERDICTS = {
                Clean:        { icon: '🟢', label: 'беспроблемная',  checked: true,  cls: 'text-admin-success' },
                NoRegulation: { icon: '🟠', label: 'без регламента', checked: true,  cls: 'text-admin-warning' },
                NeedsReview:  { icon: '🟡', label: 'нужна проверка', checked: false, cls: 'text-admin-warning' },
                Empty:        { icon: '⚪', label: 'нечего тянуть',  checked: false, cls: 'text-admin-text-muted' },
                Failed:       { icon: '🔴', label: 'ошибка',         checked: false, cls: 'text-admin-danger' }
            };
            const QUEUED = { icon: '✓', label: 'отправлено в импорт', checked: false, cls: 'text-admin-success' };
            // Разбор строки на сервере живёт 60 минут; дальше импортировать её из пачки нечем.
            const EXPIRED = { icon: '⌛', label: 'разбор истёк — затяни заново', checked: false, cls: 'text-admin-text-muted' };
            const SPIN = '<span class="admin-spinner mr-1 size-3 align-middle"></span>';

            // Ход импорта отправленной строки (поле importState с сервера, 08.10.2026). null —
            // очередь задачу не помнит (перезапуск API): остаётся просто «отправлено».
            function importCell(r) {
                switch (r.importState) {
                    case 'queued':  return `<span class="text-admin-accent">${SPIN}в очереди импорта</span>`;
                    case 'running': return `<span class="text-admin-accent">${SPIN}импортирую…</span>`;
                    case 'completed':
                        return `<span class="text-admin-success">✓ импортировано${r.importCreated != null ? ` (${fmtN(r.importCreated)} строк)` : ''}</span>`
                            + (r.importError ? `<div class="text-[12px] text-admin-warning">${esc(r.importError)}</div>` : '');
                    case 'failed':
                        return `<span class="text-admin-danger">✗ импорт упал</span>`
                            + (r.importError ? `<div class="text-[12px] text-admin-danger">${esc(r.importError)}</div>` : '');
                    default: return `<span class="${QUEUED.cls}">${QUEUED.icon} ${esc(QUEUED.label)}</span>`;
                }
            }

            // Пометка источника длины бассейна в колонке «Из регламента» (PoolTypeSources в Application).
            const POOL_SOURCE = { venue: ' (справочник бассейнов)', default: ' (умолчание лиг)' };

            const running = b => !!b && !b.finished;
            const busy = () => starting || restoring || running(last) || !!last?.importsPending;
            // Строки пачки, которые ещё можно импортировать из неё (или ещё тянутся): одиночное
            // «Затянуть» для них запрещено — иначе один старт ушёл бы в импорт дважды.
            function heldIds() {
                if (!last) return new Set();
                const pulled = new Map((last.rows || []).map(r => [r.discoveredId, r]));
                return new Set((last.queueIds || []).filter(id => {
                    const r = pulled.get(id);
                    if (!r) return !last.finished;              // ещё не затянута
                    if (r.queued) return r.importState === 'queued' || r.importState === 'running';
                    return !!r.previewId && !expired(r);         // затянута, ждёт решения (разбор жив)
                }).map(String));
            }

            function renderRows(rows) {
                if (!rows.length) { rowsBox.innerHTML = ''; return; }
                // Импорт из пачки уже был: оставшиеся строки по умолчанию СНЯТЫ — то, что не ушло
                // с первым импортом, сознательно оставили (ревью PR #266).
                const imported = rows.some(r => r.queued);
                let stamped = false;
                rows.forEach(r => {
                    if (r.previewId && !state.seen[r.discoveredId]) { state.seen[r.discoveredId] = Date.now(); stamped = true; }
                });
                if (stamped) saveState();
                const body = rows.map(r => {
                    // Уже отправленная в импорт строка — не предложение: разбор отдан очереди,
                    // повторно импортировать нечем. Без этого после перезагрузки страницы
                    // строки рисовались отмеченными, и пачка выглядела свежей (06.10.2026).
                    const old = expired(r);
                    const v = r.queued ? QUEUED : old ? EXPIRED : (VERDICTS[r.verdict] ?? VERDICTS.Failed);
                    const canImport = !!r.previewId && !r.queued && !old;
                    // Галочку, которую админ уже трогал, перерисовка сохраняет — и при опросе раз в
                    // 2 секунды, и после F5 / автоперезагрузки (state.checks в sessionStorage).
                    const prev = rowsBox.querySelector(`input.bulk-row[value="${r.discoveredId}"]`);
                    const stored = state.checks[r.discoveredId];
                    const checked = canImport && (prev ? prev.checked
                        : stored !== undefined ? stored
                        : v.checked && !imported);
                    const reasons = (r.reasons || []).map(x => esc(x)).join('<br>');
                    const reg = r.regulationUrl
                        ? `<a href="${esc(r.regulationUrl)}" target="_blank" rel="noopener" class="text-admin-accent hover:underline">תקנון ↗</a>`
                        : '<span class="text-admin-text-muted">—</span>';
                    const flags = [
                        r.hasMedals ? 'медали' : null,
                        r.hasClubStanding ? 'клубный зачёт' : null,
                        r.isChampionship ? '🏆' : null,
                        r.pointRuleClubsId ? `правило #${r.pointRuleClubsId}` : null,
                        // Длина и откуда она (П4): справочник бассейнов / умолчание лиг; из регламента — без пометки
                        r.poolType ? r.poolType + (POOL_SOURCE[r.poolTypeSource] ?? '') : 'бассейн: вручную'
                    ].filter(Boolean).map(esc).join(' · ');
                    return `<tr data-verdict="${esc(r.verdict)}">
                        <td class="align-top"><input type="checkbox" class="bulk-row cursor-pointer" value="${r.discoveredId}"
                            ${checked ? 'checked' : ''} ${canImport ? '' : 'disabled'} /></td>
                        <td class="align-top whitespace-nowrap ${v.cls}">${r.queued ? importCell(r) : `${v.icon} ${esc(v.label)}`}</td>
                        <td class="align-top" dir="rtl" style="text-align:left">
                            <a href="https://isr.org.il/comp.asp?compID=${r.orgCompId}" target="_blank" rel="noopener"
                               class="text-admin-accent hover:underline">${esc(r.name)} ↗</a>
                        </td>
                        <td class="align-top whitespace-nowrap text-admin-text-muted">${esc(r.date)}</td>
                        <td class="align-top text-right tabular-nums">${r.resultCount || 0}</td>
                        <td class="align-top text-[12px] text-admin-text-muted">${reasons || '—'}</td>
                        <td class="align-top text-[12px]">${flags || '—'}</td>
                        <td class="align-top text-[12px]">${reg}</td>
                    </tr>`;
                }).join('');

                rowsBox.innerHTML = `<table class="admin-table text-[13px]">
                    <thead><tr>
                        <th class="w-6"></th><th>Флаг</th><th>Соревнование</th><th>Дата</th>
                        <th class="text-right">Строк</th><th>Почему</th><th>Из регламента</th><th>Источник</th>
                    </tr></thead><tbody>${body}</tbody></table>`;
                updateImportBtn();
            }

            function paintImportResult() {
                const r = state.importResult;
                if (!r) return;
                const skipped = (r.skipped || []).map(esc).join('<br>');
                importOut.innerHTML = `В очередь ушло: ${r.queued}.`
                    + (r.queued ? ' Ход — в таблице выше и в <a href="/Admin/ImportHistory" class="text-admin-accent hover:underline">истории импортов</a>.' : '')
                    + (skipped ? `<div class="mt-1 text-admin-warning">Не ушло:<br>${skipped}</div>` : '');
            }

            function updateImportBtn() {
                // Пока импорт идёт, кнопка «занята» — подпись и доступность ведёт render().
                if (importBtn.getAttribute('aria-busy') === 'true') return;
                const n = rowsBox.querySelectorAll('input.bulk-row:checked').length;
                importBtn.disabled = n === 0;
                importBtn.textContent = n ? `Импортировать отмеченные (${n})` : 'Импортировать отмеченные';
            }

            // Галочки — состояние пользователя: каждое касание пишем, чтобы F5 и автоперезагрузка
            // после импорта не вернули снятое (ревью PR #266).
            function saveChecks() {
                rowsBox.querySelectorAll('input.bulk-row:not(:disabled)').forEach(cb => { state.checks[cb.value] = cb.checked; });
                saveState();
            }
            rowsBox.addEventListener('change', () => { saveChecks(); updateImportBtn(); });

            // ── Кнопки пачек в списке ──
            const includeChamp = () => !!champBox?.checked;
            const idsOf = o => ((includeChamp() ? o.dataset.idsAll : o.dataset.ids) || '').split(',').filter(Boolean).map(Number);

            // Число на кнопке, «чемпионат — отдельно», «ещё K — следующим заходом» — по положению чекбокса.
            function paintOffers() {
                const lock = busy() ? 'Идёт пачка — дождись её конца (строка над списком → показать)'
                    : (window.pullFlow?.current?.()?.phase && ['parsing', 'preview', 'importing'].includes(window.pullFlow.current().phase))
                        ? 'Идёт одиночное затягивание — закончи или отмени его' : null;
                offers.forEach(o => {
                    const ids = idsOf(o);
                    const all = (o.dataset.idsAll || '').split(',').filter(Boolean);
                    const left = Number(includeChamp() ? o.dataset.leftAll : o.dataset.left) || 0;
                    const btn = o.querySelector('.bulk-btn');
                    o.querySelector('.bulk-n').textContent = ids.length;
                    btn.classList.toggle('hidden', ids.length === 0);
                    btn.disabled = !!lock;
                    if (btn.dataset.title === undefined) btn.dataset.title = btn.title;
                    btn.title = lock ?? btn.dataset.title;
                    o.querySelector('.bulk-champ-only')?.classList.toggle('hidden', !(ids.length === 0 && all.length > 0));
                    const l = o.querySelector('.bulk-left');
                    if (l) { l.classList.toggle('hidden', left <= 0); l.querySelector('.bulk-left-n').textContent = left; }
                });
            }
            champBox?.addEventListener('change', paintOffers);

            function offerLabel(o) {
                if (o.dataset.scope === 'month') {
                    const title = o.closest('section')?.querySelector('.text-base.font-semibold')?.textContent?.trim();
                    return `За месяц${title ? ' · ' + title : ''}`;
                }
                const head = o.closest('[data-ready-day]')?.querySelector('.text-\\[13px\\].font-semibold')?.textContent?.trim();
                return `За день${head ? ' · ' + head : ''}`;
            }

            // ── Пометки строк списка: «в пачке», «тяну…», вердикт, «импорт…» ──
            function markRows() {
                document.querySelectorAll('.comp-row .bulk-mark').forEach(m => m.remove());
                if (!last) return;
                const pulled = new Map((last.rows || []).map(r => [r.discoveredId, r]));
                (last.queueIds || []).forEach((id, i) => {
                    const row = document.querySelector(`.comp-row[data-disc-id="${id}"]`);
                    if (!row) return;
                    const r = pulled.get(id);
                    let text, cls = 'bg-admin-accent/15 text-admin-accent';
                    if (!r) {
                        if (last.finished) return;
                        text = i === (last.done ?? 0) ? 'пачка: тяну…' : 'в пачке';
                    } else if (r.queued) {
                        if (r.importState === 'queued' || r.importState === 'running') text = 'импорт…';
                        else if (r.importState === 'failed') { text = 'импорт упал'; cls = 'bg-admin-danger/15 text-admin-danger'; }
                        else { text = '✓ импорт'; cls = 'bg-admin-success/15 text-admin-success'; }
                    } else if (expired(r)) {
                        text = '⌛ разбор истёк'; cls = 'bg-admin-surface-hover text-admin-text-muted';
                    } else {
                        const v = VERDICTS[r.verdict] ?? VERDICTS.Failed;
                        text = `${v.icon} в пачке`;
                    }
                    row.querySelector('.row-marks')?.insertAdjacentHTML('afterbegin',
                        `<span class="bulk-mark inline-block whitespace-nowrap rounded px-1.5 py-0.5 text-[10px] font-bold ${cls}" title="Строка в пакетном затягивании">${esc(text)}</span>`);
                });
            }

            // ── Показ: пачка справа или строка «Идёт пачка… → показать» над списком ──
            const isShown = () => !panel.classList.contains('hidden');
            function showBulk() {
                if (window.compShow) window.compShow('bulk');
                else {
                    document.querySelectorAll('#comp-detail .row-panel').forEach(p => p.classList.toggle('hidden', p !== panel));
                    document.getElementById('panel-none')?.classList.add('hidden');
                }
                ss.set(SHOWN_KEY, '1');
                paintStrip();
            }

            function paintStrip() {
                if (restoring && !last && !isShown()) {
                    strip.classList.remove('hidden'); strip.classList.add('flex');
                    stripText.textContent = 'Пачка: проверяю состояние на сервере…';
                    return;
                }
                const visible = !!last && !isShown();
                strip.classList.toggle('hidden', !visible);
                strip.classList.toggle('flex', visible);
                if (!visible) return;
                const sent = (last.rows || []).filter(r => r.queued);
                const done = sent.filter(r => r.importState === 'completed' || r.importState === 'failed');
                stripText.textContent = running(last) ? `Идёт пачка: ${last.done} из ${last.total}`
                    : last.importsPending ? `Пачка: импорт ${done.length} из ${sent.length}`
                    : `Пачка готова: ${last.total}` + (done.length ? ` · импортировано ${done.length}` : '');
            }
            strip.addEventListener('click', showBulk);
            // Выбор строки списка спрятал пачку — показать строку возврата.
            document.addEventListener('comp:panel', e => {
                if (e.detail?.key !== 'bulk') ss.del(SHOWN_KEY);
                paintStrip();
            });

            function render(batch) {
                last = batch;
                if (batch.importsPending) sawPending = true;
                const sent = (batch.rows || []).filter(r => r.queued);
                const done = sent.filter(r => r.importState === 'completed' || r.importState === 'failed');
                const failed = sent.filter(r => r.importState === 'failed').length;
                const spin = '<span class="admin-spinner mr-1.5 align-middle text-admin-accent"></span>';

                // Подпись: что было на кнопке и сколько ушло. Ушло меньше — сервер перепроверил строки
                // тем же правилом на момент клика, и часть уже не «ждёт затягивания» (skippedNotReady).
                const gone = batch.skippedNotReady || [];
                const goneHtml = gone.length
                    ? ` · <span class="text-admin-warning" title="${esc(gone.join('\n'))}">${gone.length} уже не ждут затягивания (затянуты, скрыты, ∅, «✓ имп.», перенесены или ждут протокол) — пропущены</span>`
                    : '';
                if (meta) scopeBox.innerHTML = `${esc(meta.label)} · на кнопке ${meta.n}, в пачке ${batch.total}` + goneHtml;
                else scopeBox.innerHTML = `В пачке ${batch.total}` + goneHtml;

                if (batch.error) {
                    progress.textContent = batch.error;
                } else if (!batch.finished) {
                    progress.innerHTML = spin + `Тяну ${Math.min(batch.done + 1, batch.total)} из ${batch.total}… `
                        + '<span class="opacity-70">(можно открыть другую строку или уйти со страницы и вернуться)</span>';
                } else if (batch.importsPending) {
                    // Импорт идёт в фоновой очереди по одному — счётчик по завершённым строкам.
                    progress.innerHTML = spin + `Затянуто ${batch.done} из ${batch.total}. `
                        + `Импортирую: готово ${done.length} из ${sent.length}…`;
                } else if (done.length) {
                    // Флаг окончания: все отправленные строки доимпортировались (или упали).
                    progress.innerHTML = `Затянуто ${batch.done} из ${batch.total}. `
                        + (failed
                            ? `<span class="font-semibold text-admin-warning">Импорт завершён: ${done.length - failed} из ${sent.length}, ошибок ${failed}.</span>`
                            : `<span class="font-semibold text-admin-success">✓ Импорт завершён: ${done.length} из ${sent.length}.</span>`);
                } else {
                    progress.textContent = `Готово: затянуто ${batch.done} из ${batch.total}.`
                        + (sent.length ? ` В импорт отправлено: ${sent.length}.` : '');
                }

                adminBusy(importBtn, !!batch.importsPending, 'Импортирую…');

                const skipped = batch.skippedChampionships || [];
                skippedBox.classList.toggle('hidden', skipped.length === 0);
                if (skipped.length) {
                    skippedBox.innerHTML = `Исключено чемпионатов: ${skipped.length} — `
                        + skipped.map(esc).join(', ')
                        + '. Отметьте «включая чемпионаты», если нужны они.';
                }
                renderRows(batch.rows || []);
                paintImportResult();
                markRows();
                paintOffers();
                paintStrip();
                window.pullFlow?.render?.(); // замки одиночного «Затянуть»
            }

            async function poll() {
                if (!batchId) return;
                try {
                    const batch = await api(`${API}/bulk-pull/${batchId}`);
                    netNote.classList.add('hidden');
                    restoring = false;
                    render(batch);
                    if (ss.get(SHOWN_KEY) === '1' && !restoredShown) { restoredShown = true; showBulk(); }
                    // Опрос живёт, пока тянется пачка ИЛИ идёт импорт отправленных строк.
                    if (batch.finished && !batch.importsPending) {
                        clearInterval(timer); timer = null;
                        // Импорт отмеченных доработал в этой загрузке — обновить список: строки ушли в
                        // «закрыто» / «ждём протокол · неполный». Пачка после перезагрузки на месте.
                        if (sawPending) {
                            sawPending = false;
                            if (isShown()) ss.set(SHOWN_KEY, '1');
                            importOut.insertAdjacentHTML('beforeend', '<div class="mt-1 text-admin-text-muted">Импорт завершён — обновляю список…</div>');
                            setTimeout(() => location.reload(), 800);
                        }
                    }
                } catch (err) {
                    // Забываем пачку только когда сервер сказал «такой нет» (404: перезапуск API).
                    // Обрыв сети, 502 во время деплоя — пачка на сервере идёт: опрос продолжается,
                    // запреты остаются.
                    if (err.status === 404) {
                        clearInterval(timer); timer = null;
                        forget();
                        progress.textContent = err.message;
                    } else {
                        netNote.textContent = `Связь с сервером потеряна (${err.message}) — пробую ещё…`;
                        netNote.classList.remove('hidden');
                        if (!timer) timer = setInterval(poll, 2000);
                    }
                }
            }

            function watch() {
                if (timer) clearInterval(timer);
                poll();
                timer = setInterval(poll, 2000);
            }

            function forget() {
                [KEY, META_KEY, SHOWN_KEY, STATE_KEY].forEach(ss.del);
                batchId = null; meta = null; last = null; restoring = false; starting = false;
                state = { checks: {}, seen: {} };
                netNote.classList.add('hidden');
                markRows(); paintOffers(); paintStrip();
                window.pullFlow?.render?.();
            }

            offers.forEach(o => o.querySelector('.bulk-btn').addEventListener('click', async () => {
                const btn = o.querySelector('.bulk-btn');
                const ids = idsOf(o);
                if (!ids.length) return;
                if (busy()) { alert('Идёт другая пачка — дождись её конца: строка над списком → «показать».'); return; }
                const f = window.pullFlow?.current?.();
                if (f && (['parsing', 'preview', 'importing'].includes(f.phase) || f.queuing)) {
                    alert(`Сначала закончи одиночное затягивание «${f.name}»: импортируй его или нажми «Отмена» в превью.`);
                    return;
                }
                // Открытая пачка с неимпортированными строками заменится новой — её строки отсюда
                // больше не импортировать (их разбор на сервере доживёт свои 60 минут и пропадёт).
                const held = heldIds().size;
                if (held && !confirm(`В открытой пачке ${held} затянутых строк ещё не импортированы. Новая пачка её заменит — продолжить?`)) return;
                const includeChampionships = includeChamp();
                meta = { label: offerLabel(o), n: ids.length };
                // С клика до ответа сервера гаснут ВСЕ кнопки пачек (busy() → starting).
                starting = true;
                paintOffers();
                window.pullFlow?.render?.();
                state = { checks: {}, seen: {} };
                saveState();
                importOut.textContent = '';
                rowsBox.innerHTML = '';
                skippedBox.classList.add('hidden');
                scopeBox.textContent = `${meta.label} · на кнопке ${meta.n}`;
                progress.textContent = 'Ставлю пачку…';
                last = null;
                showBulk();
                try {
                    const batch = await api(`${API}/bulk-pull`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ ids, includeChampionships })
                    });
                    batchId = batch.batchId;
                    ss.set(KEY, batchId);
                    ss.set(META_KEY, JSON.stringify(meta));
                    starting = false;
                    render(batch);
                    watch();
                } catch (err) {
                    starting = false;
                    progress.textContent = err.message;
                    paintOffers();
                    window.pullFlow?.render?.();
                }
            }));

            document.getElementById('bulk-uncheck-noreg').addEventListener('click', () => {
                rowsBox.querySelectorAll('tr[data-verdict="NoRegulation"] input.bulk-row')
                    .forEach(cb => { cb.checked = false; });
                saveChecks();
                updateImportBtn();
            });

            document.getElementById('bulk-close').addEventListener('click', () => {
                // Закрытие только убирает пачку со страницы: забор и импорт на сервере идут дальше.
                if (busy() && !confirm('Пачка ещё идёт на сервере. Закрыть её здесь? Вернуться к ней будет нельзя, забор и импорт продолжатся.')) return;
                if (timer) { clearInterval(timer); timer = null; }
                adminBusy(importBtn, false);
                rowsBox.innerHTML = '';
                importOut.textContent = '';
                forget();
                panel.classList.add('hidden');
                if (window.compShowCurrent) window.compShowCurrent();
                else document.getElementById('panel-none')?.classList.remove('hidden');
                updateImportBtn();
            });

            importBtn.addEventListener('click', async () => {
                const ids = [...rowsBox.querySelectorAll('input.bulk-row:checked')].map(cb => Number(cb.value));
                if (!ids.length) return;
                adminBusy(importBtn, true, 'Ставлю в очередь…');
                importOut.textContent = '';
                try {
                    const r = await api(`${API}/bulk-import`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ batchId, ids })
                    });
                    // Итог (сколько ушло, что пропущено и почему) — в состоянии пачки: переживает
                    // автоперезагрузку. Ничего не ушло — перезагружать незачем, причины остаются на экране.
                    state.importResult = { queued: r.queued, skipped: r.skipped || [], at: Date.now() };
                    rowsBox.querySelectorAll('input.bulk-row:checked').forEach(cb => {
                        cb.checked = false; cb.disabled = true; state.checks[cb.value] = false;
                    });
                    saveState();
                    paintImportResult();
                    if (r.queued > 0) sawPending = true;
                    else adminBusy(importBtn, false);
                    // Опрос мог остановиться на конце забора — запускаем снова: теперь он ждёт
                    // конца импорта, а «занятость» кнопки ведёт render() по importsPending.
                    watch();
                } catch (err) {
                    importOut.innerHTML = `<span class="text-admin-danger">${esc(err.message)}</span>`;
                    adminBusy(importBtn, false);
                    updateImportBtn();
                }
            });

            paintOffers();
            // Пачка с прошлой загрузки страницы — на месте, пока её не закрыли (в том числе готовая):
            // открыта справа, если была открыта, иначе — строкой над списком.
            // До ответа сервера пачка считается идущей (restoring): кнопки пачек и одиночное
            // «Затянуть» погашены. Ответ 404 — пачки нет, забываем; обрыв — опрос пробует ещё.
            if (batchId) {
                paintStrip();
                window.pullFlow?.render?.();
                (async () => {
                    await poll();
                    if (last && (!last.finished || last.importsPending) && !timer) timer = setInterval(poll, 2000);
                })();
            }

            return {
                busy,
                /** Одиночное «Затянуть» этой строки запрещено пачкой — текст причины, иначе null. */
                blocks(discId) {
                    if (starting || restoring) return 'Проверяю пачку на сервере — подожди пару секунд';
                    if (!last) return null;
                    if (busy()) return running(last)
                        ? 'Идёт пачка — одиночное «Затянуть» после неё (строка над списком → показать)'
                        : 'Идёт импорт пачки — одиночное «Затянуть» после него';
                    return heldIds().has(String(discId))
                        ? 'Строка в открытой пачке — импортируй её оттуда или закрой пачку' : null;
                },
                paintOffers,
            };
        })();

        // ── Доклейка промежуточных (Д5, docs/plans/splits-attach-without-repull-plan.md) ──
        // Сперва СУХОЙ прогон, применяем отдельной кнопкой: отчёт здесь — единственный
        // контроль («Сверки с файлом» у этой операции нет, она не переимпорт).
        (function () {
            const API = '/api/admin/splits';

            function esc(v) { const d = document.createElement('div'); d.textContent = v ?? ''; return d.innerHTML; }

            function render(box, r) {
                const writes = (r.legWrites || 0) + (r.swimWrites || 0);
                const days = (r.days || []).map(d =>
                    `<div class="text-admin-text-muted">#${d.competitionId} ${esc(d.date)} «${esc(d.name)}»: ` +
                    `ног ${d.legWrites}, личных ${d.swimWrites}</div>`).join('');
                const head = r.ok
                    ? esc(r.message)
                    : `<span class="text-admin-danger">${esc(r.message)}</span>`;
                // Применять предлагаем только после сухого прогона и только если есть что писать:
                // повторный боевой прогон пишет 0, но дёргать loglig зря незачем.
                const apply = (r.dryRun && r.ok && writes > 0)
                    ? `<button type="button" class="admin-btn admin-btn-primary py-1 px-2.5 splits-apply">Применить</button>`
                    : '';
                const applied = (!r.dryRun && r.applied && writes > 0)
                    ? `<span class="text-admin-success">Записано. Обновить страницу, чтобы увидеть флаг ⏱.</span>`
                    : '';
                box.innerHTML =
                    `<div class="font-semibold text-admin-text">${head}</div>` +
                    (days ? `<div class="mt-1.5 space-y-0.5">${days}</div>` : '') +
                    `<div class="mt-2 flex flex-wrap items-center gap-2">` +
                    `${apply}${applied}` +
                    `<label class="text-[11px] text-admin-text-muted" ` +
                    `title="Нужен только стартам без строки «входящих» — склейкам из окружных и старому импорту из JSON">` +
                    `loglig id <input type="number" class="admin-input w-28 py-0.5 px-1.5 text-[12px] splits-loglig" ` +
                    `value="${r.logligId || ''}" /></label>` +
                    `<button type="button" class="admin-btn admin-btn-ghost py-1 px-2.5 splits-dry">Сухой прогон заново</button>` +
                    `</div>` +
                    `<div class="mt-1 text-[11px] text-admin-text-muted">Пишутся только промежуточные ` +
                    `(этапы эстафет и отрезки личных) к строкам, которые уже в базе: строки не создаются и не удаляются.</div>`;
            }

            async function run(box, apply) {
                // Прогон идёт минуты, а кнопка «⏱ Промежуточные» остаётся снаружи панели:
                // без замка второй клик запускает ВТОРУЮ качку источника (поймано на 1581).
                if (box.dataset.busy === '1') return;
                box.dataset.busy = '1';
                const compId = box.dataset.comp;
                const loglig = box.querySelector('.splits-loglig')?.value?.trim();
                box.classList.remove('hidden');
                box.textContent = apply
                    ? 'Доклеиваю промежуточные… (идёт минуты)'
                    : 'Сухой прогон: качаю промежуточные с loglig и сверяю со строками базы… (идёт минуты: источник качается по одной дисциплине)';
                const q = new URLSearchParams({ apply: apply ? 'true' : 'false' });
                if (loglig) q.set('loglig', loglig);
                try {
                    const res = await fetch(`${API}/${compId}?${q}`, {
                        method: 'POST', credentials: 'same-origin',
                        headers: { 'X-XSRF-TOKEN': _csrfToken }
                    });
                    let body = null;
                    try { body = await res.json(); } catch { /* пустое тело */ }
                    if (!res.ok) throw new Error(body?.error ?? `${res.status} ${res.statusText}`);
                    render(box, body);
                } catch (e) {
                    box.innerHTML = `<span class="text-admin-danger">Ошибка: ${esc(e.message)}</span>`;
                } finally {
                    box.dataset.busy = '';
                }
            }

            // Делегирование: панели строк рисует сервер, кнопок много и они переживают F5.
            document.addEventListener('click', (ev) => {
                const btn = ev.target.closest('.splits-btn, .splits-dry, .splits-apply');
                if (!btn) return;
                // Отчёт — под шагом 9 своей области (у события — своего дня).
                const box = btn.classList.contains('splits-btn')
                    ? btn.closest('.ck-step')?.querySelector('.splits-box')
                    : btn.closest('.splits-box');
                if (!box) return;
                run(box, btn.classList.contains('splits-apply'));
            });
        })();

        // ── Действия Discovery (перенос из /Admin/Discovery; после мутаций перезагружаем SSR) ──
        (function () {
            const API = '/api/admin/discovery';
            const info = document.getElementById('sync-info');
            let preview = null; // { previewId, id, name }

            async function apiJson(url, opts = {}) {
                opts.headers = { 'X-XSRF-TOKEN': _csrfToken, ...(opts.headers ?? {}) };
                const res = await fetch(url, { credentials: 'same-origin', ...opts });
                let body = null;
                try { body = await res.json(); } catch { /* пустое тело */ }
                if (!res.ok) throw new Error(body?.error ?? `${res.status} ${res.statusText}`);
                return body;
            }
            const discName = (id) => (window.__DISC?.[id]?.name) || ('#' + id);

            // «Проверить сайт федерации» — текущий сезон; «Ещё ▾ → Сезон на сайте → Забрать» — прошлый (cYear).
            // Один забор и одни сообщения на обе кнопки (бывшие «Проверить сейчас» + селект «Сезон на сайте»).
            document.getElementById('btn-sync').addEventListener('click', (e) => runSync(e.currentTarget, ''));
            document.getElementById('btn-sync-year')?.addEventListener('click', (e) =>
                runSync(e.currentTarget, document.getElementById('sync-year').value));
            async function runSync(btn, year) {
                btn.closest('details')?.removeAttribute('open');
                btn.disabled = true;
                info.textContent = year ? `Проверяю сезон ${year} на сайте…` : 'Проверяю сайт…';
                try {
                    const r = await apiJson(`${API}/sync${year ? `?year=${encodeURIComponent(year)}` : ''}`, { method: 'POST' });
                    // Перенос/пропажа будущих стартов (В4) — только если что-то нашлось.
                    const moved = [
                        r.postponed ? `перенесено: ${r.postponed}` : '',
                        r.missing ? `пропало: ${r.missing}` : '',
                    ].filter(Boolean).join(', ');
                    info.textContent = `На сайте: ${r.totalOnSite}, новых: ${r.added}, обновлено: ${r.updated}`
                        + (moved ? `, ${moved}` : '') + '. Обновляю…';
                    // После забора прошлого сезона сразу показываем его — иначе новые строки
                    // тонут в общем списке на 19 страницах.
                    if (year) {
                        const u = new URL(location.href);
                        u.searchParams.set('season', year);
                        u.searchParams.delete('p');
                        location.href = u.toString();
                    } else {
                        location.reload();
                    }
                } catch (e) { info.textContent = ''; toast('Ошибка: ' + e.message, 5000); btn.disabled = false; }
            }

            window.discStatus = async function (id, status) {
                try {
                    await apiJson(`${API}/${id}/status`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ status })
                    });
                    location.reload();
                } catch (e) { toast('Ошибка: ' + e.message); }
            };

            // «Нет протокола»: PDF на сайте пустой. Пометка ставится и автоматически — при
            // разборе, который не нашёл ни одного соревнования, — но человек должен уметь
            // поставить/снять её руками: файл могут выложить позже.
            window.discDiscipline = async function (id, discipline) {
                try {
                    await apiJson(`${API}/${id}/discipline`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ discipline })
                    });
                    location.reload();
                } catch (e) { toast('Ошибка: ' + e.message); }
            };

            window.discEmptySource = async function (id, empty) {
                try {
                    await apiJson(`${API}/${id}/empty-source`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ empty })
                    });
                    location.reload();
                } catch (e) { toast('Ошибка: ' + e.message); }
            };

            // Р123: «Проверить, чего не хватает» у Р115 — программа loglig ↔ дни в базе. Сеть только
            // по клику. Узлы ответа готовы в _CompChecklist.cshtml; здесь только текст и hidden.
            const PROGRAMME_API = '/api/admin/programme-check';
            const LOGLIG_COMPETITION_URL = 'https://loglig.com:2053/LeagueTable/AthleticsDisciplines/';
            window.daysCheck = async function (btn) {
                const box = btn.closest('.ck-days-check');
                const id = box.dataset.discId;
                const q = (sel) => box.querySelector(sel);
                const li = box.closest('li');
                btn.disabled = true; btn.textContent = 'Проверяю программу loglig…';
                try {
                    const v = await apiJson(`${PROGRAMME_API}/${id}/check`, { method: 'POST' });
                    box.dataset.plan = (v.lines ?? []).join('\n');
                    box.dataset.fingerprint = v.planFingerprint ?? '';
                    q('.ck-days-headline').textContent = v.headline;
                    const lines = q('.ck-days-lines');
                    lines.replaceChildren(...(v.lines ?? []).map(t => { const e = document.createElement('li'); e.textContent = t; return e; }));
                    q('.ck-days-hint').textContent = v.hint ?? '';
                    // Кандидаты в недостающий день — ссылкой на loglig; адрес — из своей константы,
                    // с сервера приходит только int-номер.
                    q('.ck-days-links').replaceChildren(...(v.candidates ?? [])
                        .filter(c => Number.isInteger(c.logligId))
                        .map(c => {
                            const a = document.createElement('a');
                            a.className = 'text-[11.5px] text-admin-accent hover:underline';
                            a.href = LOGLIG_COMPETITION_URL + c.logligId;
                            a.target = '_blank'; a.rel = 'noopener';
                            a.textContent = `loglig № ${c.logligId} «${c.title}» ↗`;
                            return a;
                        }));
                    const warn = li?.querySelector('.ck-accept-warning');
                    if (warn) { warn.textContent = v.acceptWarning ? '— ' + v.acceptWarning : ''; warn.classList.toggle('hidden', !v.acceptWarning); }
                    const blocked = q('.ck-days-blocked');
                    blocked.textContent = v.splitBlocked ?? '';
                    blocked.classList.toggle('hidden', !v.splitBlocked);
                    q('.ck-days-split').classList.toggle('hidden', !v.canSplit);
                    // Р124: «Затянуть этот день» — только у единственного кандидата (номер — int с сервера).
                    const pull = q('.ck-days-pull');
                    const cand = Number.isInteger(v.pullCandidate) ? v.pullCandidate : null;
                    box.dataset.pullCandidate = cand ?? '';
                    pull.classList.toggle('hidden', cand == null);
                    // Подпись кнопки и пояснение под ней — простыми словами с сервера (MissingDayTexts).
                    if (cand != null) pull.textContent = v.pullLabel ?? `Затянуть этот день`;
                    const note = q('.ck-days-pull-note');
                    note.textContent = cand != null ? (v.pullNote ?? '') : '';
                    note.classList.toggle('hidden', cand == null);
                    q('.ck-days-accept')?.classList.toggle('hidden', !v.acceptPrimary);
                    // (а) склеены — обычная ссылка «Принять как есть» остаётся, но с оговоркой;
                    // (б) — она же основным действием в ответе, нижняя ссылка не нужна.
                    li?.querySelector('.ck-accept-note')?.classList.toggle('hidden', v.kind !== 'Collapsed');
                    li?.querySelector('.ck-accept-row')?.classList.toggle('hidden', !!v.acceptPrimary);
                    q('.ck-days-out').classList.remove('hidden');
                    btn.textContent = 'Проверить ещё раз';
                } catch (e) {
                    btn.textContent = 'Проверить, чего не хватает';
                    toast('Ошибка проверки: ' + e.message, 5000);
                }
                btn.disabled = false;
            };

            window.daysSplit = async function (btn) {
                const box = btn.closest('.ck-days-check');
                const plan = box.dataset.plan ?? '';
                if (!confirm(`Разделить по дням?\n\n${plan}\n\nСтроки переедут в свои дни (Id те же), заведётся событие, дни пересчитаются. Одна транзакция: всё или ничего.`)) return;
                btn.disabled = true; btn.textContent = 'Делю…';
                try {
                    // Отпечаток плана, который видел админ: сервер сверит с новым dry-run (изменился — 409).
                    const r = await apiJson(`${PROGRAMME_API}/${box.dataset.discId}/split`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ planFingerprint: box.dataset.fingerprint ?? '' })
                    });
                    toast(r.message, r.recalcError ? 10000 : 4000);
                    box.querySelector('.ck-days-hint').textContent = r.message;
                    // Строка теперь — событие (ключ e<id>, был c<id>): выбираем его, иначе карточка не откроется.
                    const u = new URL(location.href);
                    if (r.eventId) u.searchParams.set('sel', 'e' + r.eventId);
                    setTimeout(() => { location.href = u.toString(); }, r.recalcError ? 6000 : 1500); // дать прочитать итог
                } catch (e) {
                    btn.disabled = false; btn.textContent = 'Разделить по дням';
                    toast(e.message, 6000);
                }
            };

            window.discSyncLangs = async function (id, btn) {
                const name = discName(id);
                if (btn) { btn.disabled = true; btn.textContent = 'Синхронизирую…'; }
                info.textContent = `Синхр. языки «${name}»: скачиваю HE+EN пару, склеиваю, обновляю имена… (может занять минуту)`;
                try {
                    const s = await apiJson(`${API}/${id}/sync-languages`, { method: 'POST' });
                    info.textContent = s.monolingual
                        ? `Синхр. языки «${name}»: ${s.message}`
                        : `Синхр. языки «${name}»: готово — EN дозаполнено ${s.enNamesFilled}, канонизировано ${s.canonized}, не найдено ${s.notFound}`;
                } catch (e) {
                    info.textContent = `Синхр. языки «${name}»: ОШИБКА — ${e.message}`;
                }
                setTimeout(() => location.reload(), 1500); // дать прочитать итог перед перезагрузкой
            };

            // repull=true («Перезатянуть») — то же превью, но с заранее взведённой перезаписью:
            // намерение уже выражено кликом, второй раз спрашивать незачем.
            // Ход и превью — в панели своей строки (pullFlow); restarted — разбор, оборванный F5,
            // pullFlow.restore запускает заново.
            window.discPreview = async function (id, repull, restarted) {
                const name = discName(id);
                if (!pullFlow.start(id, name, repull)) return;
                preview = null;
                document.getElementById('preview-error').classList.add('hidden');
                const token = pullFlow.current().startedAt;
                if (restarted) {
                    pullFlow.current().lines.unshift({ step: 5, state: 'wait', text: 'Разбор прервала перезагрузка страницы — запускаю заново.' });
                    pullFlow.commit();
                }
                try {
                    const p = await apiJson(`${API}/${id}/preview`, { method: 'POST' });
                    if (!pullFlow.isActive(id, token)) return; // пока шёл разбор, ход отменили или начали другой
                    // existingCompetitionId — для перезаписи; needsPool — правило бассейна у «Импортировать» (Р118).
                    preview = { previewId: p.previewId, id, name, existingCompetitionId: p.existingCompetitionId ?? null, needsPool: needsPool(p), collapsed: p.collapsedProtocol ?? null };
                    pullFlow.previewReady(p);
                    await showPreview(p, name, repull);
                    updateRemaining();
                    document.getElementById('preview-box').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
                } catch (e) {
                    if (pullFlow.isActive(id, token)) pullFlow.parseFailed(e.message);
                }
            };

            // Р124: «Затянуть этот день» — день старта лежит на loglig отдельным соревнованием-соседом
            // (кандидат проверки «Проверить, чего не хватает»). Тот же ход pullFlow и тот же шаг 6, что у
            // «Затянуть»; отличие — превью и импорт идут через programme-check, а блок #preview-sibling
            // говорит, куда ляжет день. Значения по умолчанию (флаги, категории, бассейн) — от лежащего дня,
            // их подставил сервер. Сервер при «Импортировать» перепроверяет всё сам.
            async function siblingPreview(id, logligId, restarted) {
                const name = discName(id);
                if (!pullFlow.start(id, name, false, { logligId })) return;
                preview = null;
                document.getElementById('preview-error').classList.add('hidden');
                const token = pullFlow.current().startedAt;
                if (restarted) {
                    pullFlow.current().lines.unshift({ step: 5, state: 'wait', text: 'Разбор прервала перезагрузка страницы — запускаю заново.' });
                    pullFlow.commit();
                }
                try {
                    const r = await apiJson(`${PROGRAMME_API}/${id}/sibling-preview`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ logligId })
                    });
                    if (!pullFlow.isActive(id, token)) return;
                    const p = r.preview;
                    p.sibling = r.sibling; // едет с ходом в sessionStorage — F5 соберёт блок заново
                    preview = { previewId: p.previewId, id, name, existingCompetitionId: null, needsPool: true, collapsed: null, sibling: r.sibling };
                    pullFlow.previewReady(p);
                    await showPreview(p, name, false);
                    updateRemaining();
                    document.getElementById('preview-box').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
                } catch (e) {
                    if (pullFlow.isActive(id, token)) pullFlow.parseFailed(e.message);
                }
            }
            window.siblingPull = (btn) => {
                const box = btn.closest('.ck-days-check');
                const logligId = Number(box.dataset.pullCandidate);
                if (!Number.isInteger(logligId) || logligId <= 0) return;
                siblingPreview(box.dataset.discId, logligId, false);
            };
            window.siblingPullRestart = (f) => siblingPreview(f.discId, f.sibling.logligId, true);

            // Блок «куда ляжет день» и правки шага 6 под соседа; у обычного превью — всё как было.
            function applySibling(p) {
                const s = p.sibling ?? null;
                const box = document.getElementById('preview-sibling');
                box.classList.toggle('hidden', !s);
                document.getElementById('event-block').classList.toggle('hidden', !!s);
                document.getElementById('preview-sibling-summary').classList.toggle('hidden', !s);
                if (!s) return;
                // Тексты — с сервера (MissingDayTexts), здесь только подстановка живого выбора.
                document.getElementById('preview-sibling-msg').textContent = s.defaultsNote ?? '';
                const ev = document.getElementById('preview-sibling-event');
                ev.classList.toggle('hidden', !!s.eventId);
                document.getElementById('sibling-event-name').value = s.eventId ? '' : (s.suggestedEventName ?? '');
                const poolBox = document.getElementById('preview-sibling-pool');
                // Бассейн как у затянутого дня при другом регламенте и «сверить заплывы нечем» — одним блоком.
                const warnings = [s.poolConflictNote, s.overlapNote].filter(Boolean);
                poolBox.classList.toggle('hidden', !warnings.length);
                poolBox.textContent = warnings.join(' ');
                if (s.pointRuleClubsId) document.getElementById('preview-rule-clubs').value = String(s.pointRuleClubsId);
                renderSiblingSummary();
            }
            // «Будет добавлен второй день: суббота 07.12.2024, 1784 строки, бассейн 25 м. …» — бассейн и имя живые.
            function renderSiblingSummary() {
                const s = preview?.sibling;
                const box = document.getElementById('preview-sibling-summary');
                if (!s?.summaryTemplate) { box.textContent = ''; return; }
                const pool = document.getElementById('flag-pool').value;
                const name = s.eventId ? (s.eventName ?? '') : document.getElementById('sibling-event-name').value.trim();
                box.textContent = s.summaryTemplate
                    .replace('{pool}', () => (pool ? pool.replace('m', '') + ' м' : 'не выбран'))
                    .replace('{name}', () => name || '…'); // функцией — «$&» в имени не подставится
            }
            document.getElementById('flag-pool').addEventListener('change', renderSiblingSummary);
            document.getElementById('sibling-event-name').addEventListener('input', renderSiblingSummary);

            // Превью после F5: ответ разбора сохранён в pullFlow, на сервере оно живёт 60 минут по
            // previewId — собираем блок заново и возвращаем выбор, сделанный в нём до перезагрузки.
            // «Импортировать» погашена, пока выбор не вернулся: иначе в окне ожидания списков событий и
            // категорий импорт ушёл бы без галочек и с бассейном/событием по умолчанию.
            window.discRestorePreview = async function (f) {
                const err = document.getElementById('preview-error');
                importHold = 'Восстанавливаю выбор в превью…';
                syncImportButton();
                preview = { previewId: f.p.previewId, id: Number(f.discId), name: f.name,
                    existingCompetitionId: f.p.existingCompetitionId ?? null, needsPool: needsPool(f.p),
                    collapsed: f.p.collapsedProtocol ?? null, sibling: f.p.sibling ?? null };
                if (preview.sibling) preview.needsPool = true;
                await showPreview(f.p, f.name, f.repull);
                const restored = applyForm(f.form);
                updateRemaining();
                if (restored.eventLost) {
                    // Сохранённого события нет в списке (список не загрузился или событие удалено):
                    // молча ушло бы eventId=null, и день не привязался бы к событию.
                    importHold = 'Проверь поле «Событие»';
                    syncImportButton();
                    err.textContent = 'Не удалось восстановить выбор события — проверь поле «Событие» (или «Отмена» и «Затянуть» заново).';
                    err.classList.remove('hidden');
                    // Снимаем только СВОЮ задержку — свёрнутый протокол решает галочка (Р119).
                    document.getElementById('event-select').addEventListener('change',
                        () => { importHold = null; syncImportButton(); err.classList.add('hidden'); }, { once: true });
                    return;
                }
                importHold = null;
                syncImportButton();
            };

            // Состояние «Импортировать» — ТОЛЬКО здесь (иначе разные пути снова разъедутся, как
            // восстановление после F5 включало кнопку у свёрнутого протокола в обход галочки).
            // importHold — временная задержка (восстановление выбора, потерянное событие);
            // свёрнутый протокол (Р119) гасит кнопку, пока не отмечена «Я проверил».
            let importHold = null;
            function collapsedOverride() {
                return !!preview?.collapsed && document.getElementById('collapsed-override-cb').checked;
            }
            /// Р119 держит импорт: протокол свёрнут, галочка «Я проверил» не отмечена. Одна проверка
            /// для кнопки, подсказки бассейна и подписи шага 6.
            function collapsedBlocked() {
                return !!preview?.collapsed && !collapsedOverride();
            }
            function syncImportButton() {
                const btn = document.getElementById('btn-import');
                const blocked = collapsedBlocked();
                btn.disabled = !!importHold || blocked;
                btn.title = importHold
                    ?? (blocked ? 'Протокол свёрнут в одну дату — импорт создал бы дубль уже затянутых дней. Обход — галочка «Я проверил» в красном блоке.' : '');
            }
            document.getElementById('collapsed-override-cb').addEventListener('change', () => {
                syncImportButton();
                updateRemaining();
            });

            // Выбор в превью (бассейн, флаги, категории, правило, событие, перезапись, «сомнительные»)
            // — в pullFlow, чтобы F5 его не терял.
            const FORM_IDS = ['flag-pool', 'flag-award', 'flag-championship', 'flag-masters', 'flag-club-off',
                'preview-rule-clubs', 'event-select', 'event-name', 'overwrite-existing-cb', 'delete-missing-cb', 'sibling-event-name'];
            function captureForm() {
                const form = { cats: [...document.querySelectorAll('.disc-cat:checked')].map(i => i.value),
                    suspect: [...document.querySelectorAll('.suspect-flag:checked')].map(i => i.value) };
                FORM_IDS.forEach(id => {
                    const el = document.getElementById(id);
                    if (el) form[id] = el.type === 'checkbox' ? el.checked : el.value;
                });
                return form;
            }
            /// { eventLost: true } — сохранённое событие поставить нельзя (его нет в списке).
            function applyForm(form) {
                if (!form) return {};
                let eventLost = false;
                FORM_IDS.forEach(id => {
                    const el = document.getElementById(id);
                    if (!el || !(id in form)) return;
                    if (id === 'event-select' && form[id] && ![...el.options].some(o => o.value === form[id])) {
                        eventLost = true;
                        el.value = '';
                    } else if (el.type === 'checkbox') el.checked = !!form[id]; else el.value = form[id];
                    el.dispatchEvent(new Event('change'));
                });
                const cats = new Set(form.cats ?? []), sus = new Set(form.suspect ?? []);
                document.querySelectorAll('.disc-cat').forEach(i => { i.checked = cats.has(i.value); });
                document.querySelectorAll('.suspect-flag').forEach(i => { i.checked = sus.has(i.value); });
                // change выше сохранял промежуточный выбор; потерянное событие не затираем пустым.
                if (!eventLost) pullFlow.saveForm(captureForm());
                return { eventLost };
            }

            // Бассейн обязателен, если хоть один день файла в базе не найден (Р118): существующие дни
            // бассейн сохраняют, а новый без выбора получил бы умолчание парсера (25 м, И-30).
            // existingCompetitionId — лишь ПЕРВЫЙ найденный день и о новых днях ничего не говорит.
            function needsPool(p) {
                const days = p.existingCompetitions || [];
                if (!days.length) return !p.existingCompetitionId;
                return days.some(m => !m.existingCompetitionId);
            }

            // Подпись шага 6: что ещё нужно до «Импортировать» (макет: «осталось: бассейн и 2 рекорда»).
            function updateRemaining() {
                const f = pullFlow.current();
                if (!f?.p) return;
                const pool = document.getElementById('flag-pool').value;
                // Правило то же, что у «Импортировать»: бассейн обязателен, если в файле есть новый день.
                const poolHint = document.getElementById('pool-hint');
                // Заблокированному свёрнутому протоколу бассейн не нужен; с галочкой обхода строки ложатся
                // в день с датой файла (у e62 — новый день), и бассейн снова обязателен.
                poolHint.classList.toggle('hidden', !!pool || !needsPool(f.p) || collapsedBlocked());
                poolHint.textContent = f.p.existingCompetitionId
                    ? 'в файле есть новый день — выбери бассейн (существующим дням он не меняется)'
                    : 'выбери — без бассейна импорт не начнётся';
                const recs = new Set((f.p.recordPreview?.rows ?? []).map(r => r.rowIndex)).size;
                const left = [];
                if (!pool && needsPool(f.p)) left.push('бассейн');
                if (recs) left.push(`${recs} ${recs % 10 === 1 && recs % 100 !== 11 ? 'рекорд' : recs % 10 >= 2 && recs % 10 <= 4 && (recs % 100 < 12 || recs % 100 > 14) ? 'рекорда' : 'рекордов'}`);
                if (collapsedBlocked()) {
                    pullFlow.setCaption(6, 'импорт заблокирован: протокол свёрнут в одну дату, эти результаты уже затянуты днями события');
                    return;
                }
                if (f.p.collapsedProtocol)
                    left.unshift('⚠ обход Р119 отмечен — строки лягут в день файла');
                const note = f.repull
                    ? (f.p.existingCompetitionId ? ' · перезапись существующих результатов уже включена'
                        : ' · совпадения с существующим соревнованием НЕ найдено — импорт создаст новое, проверь дату и название')
                    : '';
                pullFlow.setCaption(6, (left.length ? `осталось: ${left.join(' и ')}` : 'всё выбрано — нажми «Импортировать»') + note);
            }

            document.getElementById('preview-box').addEventListener('change', () => {
                pullFlow.saveForm(captureForm());
                updateRemaining();
            });
            document.getElementById('event-name').addEventListener('input', () => pullFlow.saveForm(captureForm()));

            // «Сколько рекордов побьёт файл» — считается сервером ДО «Применить» (Б2).
            // Настоящий рекорд редок: много рекордов разом почти всегда значит, что протокол
            // разобрался неверно (эталон — 00:32.59 на 100 баттерфляем). Это ПОДСКАЗКА,
            // а не запрет: импорт не блокируется, решает человек.
            // Заметка пишется САМА и хранится в БД: пометка без причины через полгода
            // неотличима от случайного клика. Пишем, что именно смутило — время заплыва
            // против конкретных действующих рекордов.
            function suspectNote(g) {
                const s = g.swim;
                const recs = g.records.map(r => `${r.kind} ${r.recordTime}${r.recordHolder ? ' ' + r.recordHolder : ''}`).join('; ');
                return `Помечено при импорте: ${s.time} на ${s.distance} ${s.styleName} быстрее рекорда — ${recs}`;
            }

            function renderRecordPreview(rp) {
                const box = document.getElementById('preview-records');
                if (!box) return;
                if (!rp || (rp.count === 0 && !rp.error)) { box.classList.add('hidden'); return; }

                if (rp.error) {
                    box.className = 'mt-3 rounded border border-admin-border bg-admin-surface px-3 py-2 text-[12px] text-admin-text-muted';
                    box.textContent = `Рекорды не проверены: ${rp.error}`;
                    box.classList.remove('hidden');
                    return;
                }

                // Порог «много» намеренно грубый: два-три рекорда на серьёзном старте — норма,
                // десяток — почти наверняка разбор поехал.
                const many = rp.count >= 5;
                box.className = many
                    ? 'mt-3 rounded border border-admin-danger/40 bg-admin-danger/10 px-3 py-2 text-[12px]'
                    : 'mt-3 rounded border border-admin-warning/40 bg-admin-warning/10 px-3 py-2 text-[12px]';
                // Один заплыв бьёт и open-, и возрастной рекорд — это ДВЕ строки превью с одним
                // rowIndex. Галочка у них общая: помечается заплыв, а не «рекорд».
                const byRow = new Map();
                (rp.rows || []).forEach(r => {
                    const key = r.rowIndex;
                    if (!byRow.has(key)) byRow.set(key, { swim: r, records: [] });
                    byRow.get(key).records.push(r);
                });

                // Каждый заплыв — отдельная карточка: заголовок (кто/что/время), под ним
                // побитые рекорды, и ОТДЕЛЁННАЯ чертой строка решения (галочка + вердикт по
                // loglig). Без этих границ четыре разных по смыслу строки читались как одна
                // простыня, и глазами не находилось, где кончается один заплыв.
                box.innerHTML =
                    `<div class="font-semibold">${many ? '⚠ ' : ''}Побьёт рекордов: ${rp.count}</div>` +
                    (many ? '<div class="text-admin-text-muted">Столько рекордов сразу — обычно признак того, что протокол разобрался неверно. Стоит посмотреть глазами до «Применить».</div>' : '') +
                    '<ul class="mt-2 space-y-2">' + [...byRow.values()].map(g => {
                        const s = g.swim;
                        const recs = g.records.map(r =>
                            `<div class="text-admin-text-muted">бьёт ${esc(r.kind)}: ${esc(r.recordTime)}${r.recordHolder ? ` — <bdi>${esc(r.recordHolder)}</bdi>` : ''}</div>`).join('');
                        // Строка LTR, имена в <bdi>: при dir="rtl" ивритское имя уезжало в конец
                        // («individual_medley · 01:29.97 200 · ליאם אלמוג») и читалось как ещё один
                        // держатель рекорда, а не как тот, кто проплыл (20.11.2026).
                        return `<li class="rounded-md border border-admin-border bg-admin-surface/60 px-2.5 py-2">`
                            + `<div class="font-semibold text-admin-text"><span class="font-normal text-admin-text-muted">проплыл:</span> <bdi>${esc(s.swimmerName)}</bdi> · ${esc(s.distance)} ${esc(s.styleName)} · <b>${esc(s.time)}</b></div>`
                            + `<div class="mt-0.5 space-y-0.5 text-[11.5px]">${recs}</div>`
                            + `<div class="mt-1.5 border-t border-admin-border pt-1.5">`
                            + `<label class="flex items-center gap-1.5 cursor-pointer text-admin-text-muted">`
                            + `<input type="checkbox" class="suspect-flag cursor-pointer" value="${s.rowIndex}"`
                            + ` data-note="${esc(suspectNote(g))}"> пометить сомнительным</label>`
                            // Сюда приезжает вердикт по карточке loglig (или поле для привязки
                            // id и пола, если пловец ещё не привязан) — см. loadRecordCheck.
                            + `<div class="record-check mt-1 text-[11.5px]" data-row="${s.rowIndex}" data-name="${esc(s.swimmerName)}"></div>`
                            + `</div>`
                            + `</li>`;
                    }).join('') +
                    '</ul>' +
                    // Штатный ложный случай: тянем протокол ТОГО САМОГО старта, на котором рекорд
                    // и был установлен. Заплыв «бьёт» запись справочника, потому что это она и есть
                    // (время равное или на сотые лучше — федерация округляет). Ничего делать не надо,
                    // но без подсказки это читается как ошибка разбора — и галочку жмут зря.
                    '<div class="mt-2 border-t border-admin-border pt-1.5 text-admin-text-muted">Если это тот самый заплыв, которым рекорд '
                    + 'и был установлен, — предупреждение ложное: в справочнике уже стоит он же '
                    + '(сверь имя и время в скобках). Помечать такую строку не нужно.</div>' +
                    (rp.count > (rp.rows || []).length ? `<div class="mt-0.5 text-admin-text-muted">…и ещё ${rp.count - rp.rows.length}</div>` : '');
                box.classList.remove('hidden');
            }

            // ── Флаги соревнования в превью ────────────────────────────────────────
            // Ставим галочки сами, но показываем ОСНОВАНИЕ каждой: регламент — чужой документ
            // произвольной формы, и молча проставленной галочке верить нельзя.
            const FLAG_LABELS = {
                isAward: 'Awards',
                isChampionship: '🏆 Чемпионат Израиля',
                isMasters: 'Masters',
                clubPointsDisabled: 'клубный зачёт не ведётся',
                poolType: 'бассейн',
                categories: 'категории'
            };

            function renderCompetitionFlags(flags) {
                const award = document.getElementById('flag-award');
                const champ = document.getElementById('flag-championship');
                const masters = document.getElementById('flag-masters');
                const clubOff = document.getElementById('flag-club-off');
                const pool = document.getElementById('flag-pool');
                const reasons = document.getElementById('flag-reasons');

                const f = flags ?? {};
                award.checked = !!f.isAward;
                champ.checked = !!f.isChampionship;
                masters.checked = !!f.isMasters;
                clubOff.checked = !!f.clubPointsDisabled;
                pool.value = f.poolType ?? '';

                const lines = Object.entries(f.reasons ?? {})
                    .map(([k, v]) => `<div>${esc(FLAG_LABELS[k] ?? k)}: ${esc(v)}</div>`);
                if (f.regulationUrl)
                    lines.push(`<div><a href="${esc(f.regulationUrl)}" target="_blank" rel="noopener"
                                   class="text-admin-accent hover:underline">регламент (תקנון) ↗</a></div>`);
                if (!lines.length)
                    lines.push('<div>Ничего не предложено: регламента нет, название и файл признаков не дали.</div>');

                reasons.innerHTML = lines.join('');
            }

            // ── Проверка подозрительных заплывов по карточке loglig ────────────────
            // Протокол мы качаем С loglig, значит время из него обязано быть и в карточке
            // пловца: совпало — рекорд настоящий, в карточке хуже — разбор поехал. Запрос
            // отдельный (карточка на пловца), чтобы превью не ждало сеть.
            const CHECK_TONE = {
                Confirms:    { cls: 'text-admin-success', icon: '✓' },
                Contradicts: { cls: 'text-admin-danger',  icon: '⚠' },
                NoData:      { cls: 'text-admin-text-muted', icon: '·' },
                NotChecked:  { cls: 'text-admin-text-muted', icon: '·' }
            };

            async function loadRecordCheck(previewId, rp) {
                if (!previewId || !rp || !(rp.rows || []).length) return;

                document.querySelectorAll('#preview-records .record-check')
                    .forEach(el => { el.textContent = 'Проверяю по loglig…'; el.className = 'record-check mt-0.5 text-[11.5px] text-admin-text-muted'; });

                let rows;
                try {
                    rows = await apiJson(`${API}/preview/${previewId}/record-check`, { method: 'POST' });
                } catch (err) {
                    document.querySelectorAll('#preview-records .record-check')
                        .forEach(el => { el.textContent = `Проверка по loglig не удалась: ${err.message}`; });
                    return;
                }

                (rows || []).forEach(renderRecordCheckRow);
            }

            function renderRecordCheckRow(r) {
                const box = document.querySelector(`#preview-records .record-check[data-row="${r.rowIndex}"]`);
                if (!box) return;

                const tone = CHECK_TONE[r.verdict] ?? CHECK_TONE.NotChecked;
                const shownId = r.logligId ?? r.suggestedLogligId;
                const link = r.logligUrl
                    ? ` <a href="${esc(r.logligUrl)}" target="_blank" rel="noopener"
                           class="text-admin-accent hover:underline">карточка loglig${box.dataset.name ? ` <bdi>${esc(box.dataset.name)}</bdi>` : ''} #${shownId} ↗</a>`
                    : '';

                // Пловец в базе есть, но не привязан — даём привязать прямо тут (и пол заодно:
                // на этом же экране он чаще всего и неизвестен). Проверять после ввода не
                // будем — Влад смотрит карточку глазами.
                //
                // Если id пришёл ИЗ ПРОТОКОЛА (на странице заплыва loglig имя — ссылка), поле
                // ввода не показываем вовсе: кнопка сразу с нужным номером. Руками вводят
                // только тогда, когда протокол id не дал.
                const gender = r.gender ? '' : `<span class="text-admin-text-muted">пол:</span>
                           <button type="button" class="admin-btn admin-btn-ghost py-0.5 px-1.5 text-[11px] rc-gender"
                                   data-swimmer="${r.swimmerId}" data-gender="male" data-row="${r.rowIndex}">♂</button>
                           <button type="button" class="admin-btn admin-btn-ghost py-0.5 px-1.5 text-[11px] rc-gender"
                                   data-swimmer="${r.swimmerId}" data-gender="female" data-row="${r.rowIndex}">♀</button>`;

                let bind = '';
                if (r.match === 'One' && !r.logligId && r.suggestedLogligId) {
                    bind = `<span class="ml-1 inline-flex flex-wrap items-center gap-1">
                           <button type="button" class="admin-btn admin-btn-ghost py-0.5 px-1.5 text-[11px] rc-loglig-bind"
                                   data-swimmer="${r.swimmerId}" data-row="${r.rowIndex}"
                                   data-loglig="${r.suggestedLogligId}">привязать #${r.suggestedLogligId}</button>
                           ${gender}
                       </span>`;
                } else if (r.match === 'One' && !r.logligId) {
                    bind = `<span class="ml-1 inline-flex flex-wrap items-center gap-1">
                           <input type="number" class="admin-input py-0.5 px-1.5 w-[7.5rem] text-[11.5px] rc-loglig-input"
                                  data-swimmer="${r.swimmerId}" placeholder="id с loglig" />
                           <button type="button" class="admin-btn admin-btn-ghost py-0.5 px-1.5 text-[11px] rc-loglig-bind"
                                   data-swimmer="${r.swimmerId}" data-row="${r.rowIndex}">привязать</button>
                           ${gender}
                       </span>`;
                }

                box.className = `record-check mt-0.5 text-[11.5px] ${tone.cls}`;
                box.innerHTML = `${tone.icon} ${esc(r.message)}${link}${bind}`;
            }

            // Привязка id и пола — ШТАТНЫМИ эндпоинтами (/api/admin/loglig/set,
            // /api/admin/swimmers/{id}/gender): вторая реализация тех же правил разошлась бы
            // с первой (сверка года и клуба, аудит, дописывание пола в строки без пола).
            document.getElementById('preview-records')?.addEventListener('click', async (e) => {
                const bindBtn = e.target.closest('.rc-loglig-bind');
                const genderBtn = e.target.closest('.rc-gender');
                if (!bindBtn && !genderBtn) return;

                const btn = bindBtn ?? genderBtn;
                const row = btn.dataset.row;
                const box = document.querySelector(`#preview-records .record-check[data-row="${row}"]`);
                btn.disabled = true;

                try {
                    if (bindBtn) {
                        // Готовый id из протокола (data-loglig) либо введённый руками.
                        const input = document.querySelector(`.rc-loglig-input[data-swimmer="${btn.dataset.swimmer}"]`);
                        const logligId = Number(btn.dataset.loglig || input?.value);
                        if (!Number.isFinite(logligId) || logligId <= 0) {
                            btn.disabled = false;
                            return alert('Введите числовой id с loglig');
                        }
                        const res = await apiJson('/api/admin/loglig/set', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ swimmerId: Number(btn.dataset.swimmer), logligId })
                        });
                        // linked=false — доменный отказ (уже привязан, карточка недоступна), не HTTP-ошибка.
                        if (res && res.linked === false) {
                            box.innerHTML = `<span class="text-admin-warning">Не привязано: ${esc(res.error ?? 'причина неизвестна')}</span>`;
                            return;
                        }
                        box.innerHTML = `<span class="text-admin-success">✓ Привязан loglig #${logligId}.</span>`
                            + ` <a href="https://loglig.com:2053/Players/Details/${logligId}?tab=results" target="_blank"
                                   rel="noopener" class="text-admin-accent hover:underline">карточка ↗</a>`;
                    } else {
                        await apiJson(`/api/admin/swimmers/${btn.dataset.swimmer}/gender`, {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ gender: btn.dataset.gender })
                        });
                        genderBtn.textContent = '✓ ' + genderBtn.textContent.replace('✓ ', '');
                        genderBtn.classList.add('text-admin-success');
                    }
                } catch (err) {
                    box.innerHTML = `<span class="text-admin-danger">${esc(err.message)}</span>`;
                } finally {
                    btn.disabled = false;
                }
            });

            // Официальный клубный зачёт: есть/нет + подстановка правила под его шкалу.
            // «Нет» — не ошибка, а факт: у лиг, мокдамот и региональных этапов зачёта не бывает,
            // и наши клубные очки на них сверять не с чем.
            function renderOfficialStanding(s) {
                const box = document.getElementById('preview-standing');
                const select = document.getElementById('preview-rule-clubs');
                select.value = '';

                if (!s) {
                    box.className = 'mb-2 rounded-md border border-admin-border px-3 py-2 text-[13px] text-admin-text-muted';
                    box.textContent = 'Официальный клубный зачёт не проверялся.';
                    return;
                }

                if (s.hasStanding && s.ruleId) {
                    // Правило опознано по шкале — подставляем его, но оставляем изменяемым.
                    select.value = String(s.ruleId);
                    box.className = 'mb-2 rounded-md border border-admin-success/40 bg-admin-success/10 px-3 py-2 text-[13px] text-admin-text';
                } else if (s.hasStanding) {
                    // Зачёт есть, а правила под его шкалу нет — это надо завести ДО импорта.
                    box.className = 'mb-2 rounded-md border border-[rgba(255,213,79,0.4)] bg-[rgba(255,213,79,0.08)] px-3 py-2 text-[13px] text-admin-text';
                } else {
                    box.className = 'mb-2 rounded-md border border-admin-border px-3 py-2 text-[13px] text-admin-text-muted';
                }

                const scale = (s.scale ?? []).slice(0, 12).map(x => x.points).join(', ');
                box.innerHTML = esc(s.message) + (scale ? `<div class="mt-1 text-[12px] text-admin-text-muted">Шкала зачёта: ${esc(scale)}…</div>` : '');

                // Предложение завести правило — только когда зачёт есть, а шкала не опознана.
                const createBox = document.getElementById('preview-rule-create');
                createBox.classList.toggle('hidden', !s.canCreateRule);
                if (s.canCreateRule) {
                    document.getElementById('preview-rule-version').value = s.suggestedVersion ?? '';
                    document.getElementById('preview-rule-scale').textContent =
                        (s.scale ?? []).map(x => `${x.place}:${x.points}`).join('  ');
                }
            }

            async function showPreview(p, name, repull) {
                document.getElementById('preview-summary').innerHTML =
                    `Распознано результатов: <b>${p.resultCount}</b>` +
                    (p.competitions?.length
                        ? '<ul class="mt-1 list-disc pl-5">' + p.competitions.map(c =>
                            `<li dir="rtl" style="text-align:left">${esc(c.competition)} — ${esc(c.date)} (${c.resultCount})</li>`).join('') + '</ul>'
                        : '');
                const warn = document.getElementById('preview-warnings');
                warn.classList.toggle('hidden', !(p.warnings?.length));
                warn.innerHTML = (p.warnings ?? []).map(esc).join('<br>');

                // Р119: протокол свёрнут в одну дату, а эти результаты уже затянуты днями события —
                // импорт дал бы дубль. «Импортировать» гаснет; сервер такой импорт тоже откатит.
                // Обход — галочка «Я проверил»; каждое новое превью начинает с неотмеченной.
                const collapsedBox = document.getElementById('preview-collapsed');
                collapsedBox.classList.toggle('hidden', !p.collapsedProtocol);
                document.getElementById('preview-collapsed-msg').textContent = p.collapsedProtocol?.message ?? '';
                document.getElementById('collapsed-override-date').textContent = p.collapsedProtocol?.fileDate ?? '';
                document.getElementById('collapsed-override-cb').checked = false;
                syncImportButton();

                renderCompetitionFlags(p.flags);
                renderRecordPreview(p.recordPreview);
                loadRecordCheck(p.previewId, p.recordPreview);
                renderOfficialStanding(p.officialClubStanding);

                // Upsert-чекбокс — только когда превью нашло существующее соревнование по ключу Name|Date|PoolType.
                const overwriteBlock = document.getElementById('overwrite-block');
                const overwriteCb = document.getElementById('overwrite-existing-cb');
                const overwriteCompName = document.getElementById('overwrite-comp-name');
                const deleteMissingBlock = document.getElementById('delete-missing-block');
                const deleteMissingCb = document.getElementById('delete-missing-cb');
                const overwriteDeleteWarning = document.getElementById('overwrite-delete-warning');
                const existingMatch = (p.existingCompetitions || []).find(m => m.existingCompetitionId);
                overwriteBlock.classList.toggle('hidden', !p.existingCompetitionId);
                overwriteCompName.textContent = existingMatch ? `«${existingMatch.existingCompetitionName}»` : '';
                overwriteCb.checked = false;
                deleteMissingCb.checked = false;
                deleteMissingBlock.classList.add('hidden');

                function updateDeleteMissingVisibility() {
                    const show = overwriteCb.checked;
                    deleteMissingBlock.classList.toggle('hidden', !show);
                    if (!show) deleteMissingCb.checked = false;
                    updateOverwriteDeleteWarning();
                }
                function updateOverwriteDeleteWarning() {
                    if (!overwriteDeleteWarning) return;
                    if (!overwriteCb.checked || !deleteMissingCb.checked) { overwriteDeleteWarning.classList.add('hidden'); return; }
                    const matches = p.existingCompetitions || [];
                    let dbTotal = 0, fileTotal = 0, anyOverage = false;
                    matches.forEach(m => {
                        if (!m.existingCompetitionId) return;
                        const existing = m.existingResultCount || 0;
                        const day = (p.competitions || []).find(c => c.competition === m.competition && c.date === m.date);
                        const fileCount = day ? (day.resultCount || 0) : 0;
                        dbTotal += existing; fileTotal += fileCount;
                        if (existing > fileCount) anyOverage = true;
                    });
                    if (!anyOverage) { overwriteDeleteWarning.classList.add('hidden'); return; }
                    const maxDeleted = Math.max(0, dbTotal - fileTotal);
                    overwriteDeleteWarning.textContent =
                        `В БД ${dbTotal} результатов, в файле ${fileTotal} — до ${maxDeleted} результатов будет удалено. ` +
                        `Результаты с привязанным медиа удалены не будут.`;
                    overwriteDeleteWarning.classList.remove('hidden');
                }
                overwriteDeleteWarning.classList.add('hidden');
                overwriteCb.onchange = updateDeleteMissingVisibility;
                deleteMissingCb.onchange = updateOverwriteDeleteWarning;

                // «Перезатянуть»: взводим перезапись сразу. «Удалять лишние» НЕ трогаем —
                // это отдельное решение, оно удаляет строки из БД.
                if (repull && p.existingCompetitionId) {
                    overwriteCb.checked = true;
                    updateDeleteMissingVisibility();
                }

                // Список существующих событий (второй день дописывается к первому).
                const eventSelect = document.getElementById('event-select');
                [...eventSelect.querySelectorAll('option[data-event]')].forEach(o => o.remove());
                try {
                    const events = await fetch('/api/admin/competition-events', { credentials: 'same-origin' })
                        .then(r => r.ok ? r.json() : []);
                    (Array.isArray(events) ? events : []).forEach(e => {
                        const opt = document.createElement('option');
                        opt.value = String(e.id); opt.dataset.event = '1';
                        opt.textContent = `${e.name} · дней: ${e.dayCount}`;
                        eventSelect.appendChild(opt);
                    });
                } catch { /* список событий опционален */ }

                const multiDay = (p.competitions?.length ?? 0) > 1;
                eventSelect.value = multiDay ? '__new__' : '';
                document.getElementById('event-name').value = name;
                document.getElementById('event-name').classList.toggle('hidden', !multiDay);

                try {
                    // cache: 'no-store' — иначе только что созданная категория не появится
                    // здесь до истечения браузерного кэша вкладки (Cache-Control: public,
                    // max-age=300 у /api/categories рассчитан на публичный сайт).
                    const cats = await fetch('/api/categories', { credentials: 'same-origin', cache: 'no-store' }).then(r => r.json());
                    // Категории, подобранные по названию соревнования, отмечаем сразу —
                    // раньше чек-лист был пустым, и про него забывали ровно так же, как про флаги.
                    const suggested = new Set(p.flags?.categoryKeys ?? []);
                    document.getElementById('preview-categories').innerHTML = cats.map(c =>
                        `<label class="flex items-center gap-1.5 cursor-pointer"><input type="checkbox" value="${esc(c.key)}" class="disc-cat cursor-pointer"${suggested.has(c.key) ? ' checked' : ''}> ${esc(c.name)}</label>`
                    ).join('');
                } catch { /* категории опциональны */ }

                // Р124: у дня-соседа — блок «куда ляжет день» вместо выбора события; у обычного — прячется.
                applySibling(p);
            }

            // Штамповка loglig-id по протоколу уже импортированного соревнования. Идёт по
            // страницам заплывов, поэтому долгая — кнопку гасим и пишем в общий info.
            window.discStampLoglig = async (orgCompId, btn) => {
                btn.disabled = true;
                info.textContent = `Loglig-id (compID ${orgCompId}): читаю протокол на loglig… (может занять минуту)`;
                try {
                    const r = await apiJson(`${API}/stamp-loglig`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ orgCompId })
                    });
                    info.textContent = `Loglig-id (compID ${orgCompId}): ${r.message}`;
                } catch (e) {
                    info.textContent = `Loglig-id (compID ${orgCompId}): ОШИБКА — ${e.message}`;
                } finally {
                    btn.disabled = false;
                }
            };

            // Стартовый протокол (С5): забор одного соревнования — под две сотни запросов
            // с паузой, поэтому долго. Отчёт идёт в модалку (а не в «sync-info» в шапке:
            // со прокрученной вниз страницы его не видно, и кнопка кажется мёртвой).
            // Модалка не закрывается сама — при ошибке из неё копируют диагностику.
            // Status empty — ожидаемое (нет loglig-id / посев не сделан), не ошибка.
            const slDlg = document.getElementById('startlist-modal');
            const slTitle = document.getElementById('startlist-title');
            const slStatus = document.getElementById('startlist-status');
            const slLog = document.getElementById('startlist-log');
            let slReport = '';

            const slLine = (t) => {
                slReport += t + '\n';
                slLog.textContent = slReport;
                slLog.scrollTop = slLog.scrollHeight;
            };
            const slSet = (text, cls, busy = false) => {
                slStatus.textContent = text;
                slStatus.className = 'mt-3 text-[13px] ' + cls;
                if (busy) slStatus.insertAdjacentHTML('afterbegin',
                    '<span class="admin-spinner mr-1.5 align-middle text-admin-accent"></span>');
            };
            document.getElementById('startlist-close')
                .addEventListener('click', () => slDlg.close());
            document.getElementById('startlist-copy').addEventListener('click', async () => {
                const hint = document.getElementById('startlist-copy-hint');
                try {
                    await navigator.clipboard.writeText(slReport);
                    hint.textContent = 'Скопировано';
                } catch {
                    hint.textContent = 'Не вышло — выделите текст выше вручную';
                }
            });

            window.discStartList = async (id, btn) => {
                adminBusy(btn, true);
                const url = `${API}/${id}/start-list`;
                slReport = '';
                slLog.textContent = '';
                slTitle.textContent = discName(id);
                slSet('Тяну с loglig… (может занять несколько минут)', 'text-admin-text-muted', true);
                slLine(`POST ${url}`);
                slLine(`Соревнование: ${discName(id)} (discoveredId ${id})`);
                if (!slDlg.open) slDlg.showModal();
                try {
                    // Свой fetch, а не apiJson: при ошибке нужны код ответа и сырое тело —
                    // именно их Влад присылает на разбор.
                    const res = await fetch(url, {
                        method: 'POST',
                        credentials: 'same-origin',
                        headers: { 'X-XSRF-TOKEN': _csrfToken }
                    });
                    const raw = await res.text();
                    slLine(`HTTP ${res.status} ${res.statusText}`);
                    let r = null;
                    try { r = JSON.parse(raw); } catch { /* не JSON */ }

                    if (!res.ok) {
                        slSet(`ОШИБКА HTTP ${res.status} — ${r?.error ?? res.statusText}`, 'text-red-400');
                        slLine('Ответ сервера:');
                        slLine(raw || '(пустое тело)');
                        return;
                    }
                    if (!r) {
                        slSet('ОШИБКА: сервер вернул не JSON', 'text-red-400');
                        slLine(raw || '(пустое тело)');
                        return;
                    }
                    slLine('Ответ сервера:');
                    slLine(JSON.stringify(r, null, 2));

                    if (r.status === 'error') {
                        slSet(`ОШИБКА (compID ${r.orgCompId}) — ${r.error || 'неизвестная ошибка'}`, 'text-red-400');
                    } else if (r.status === 'empty') {
                        slSet(`Пусто (compID ${r.orgCompId}): ${r.error || 'нечего тянуть'}`, 'text-amber-400');
                    } else {
                        slSet(`Готово (compID ${r.orgCompId}): ${r.status} — `
                            + `заплывов ${r.eventsFetched}/${r.events}, строк ${r.entries}; `
                            + `новых ${r.added}, пересев ${r.moved}, снялись ${r.removed}, `
                            + `без изменений ${r.unchanged}`, 'text-emerald-400');
                    }
                } catch (e) {
                    // Сеть/прерванный запрос: HTTP-кода нет, но имя и текст ошибки есть.
                    slSet(`ОШИБКА запроса — ${e.name}: ${e.message}`, 'text-red-400');
                    slLine(`Исключение: ${e.name}: ${e.message}`);
                    if (e.stack) slLine(e.stack);
                } finally {
                    adminBusy(btn, false);
                }
            };

            // Справка о старте (Т1): разминка по дням руками + переопределение флага
            // «чемпионат». Флаг сам ставит забор по регламенту — здесь он только показан,
            // а меняется отдельным полем: иначе следующий перезабор затёр бы решение админа.
            const miDlg = document.getElementById('meetinfo-modal');
            const miBody = document.getElementById('meetinfo-body');
            const miHint = document.getElementById('meetinfo-hint');
            let miId = null;

            const miDay = (iso) => new Date(iso).toLocaleDateString('ru-RU',
                { weekday: 'short', day: '2-digit', month: 'short' });

            const miRender = (info) => {
                const auto = info.isChampionship ? 'чемпионат' : 'не чемпионат';
                const ovr = info.isChampionshipOverride;
                const reg = info.regulationUrl
                    ? `<a href="${info.regulationUrl}" target="_blank" rel="noopener" class="underline">регламент</a>`
                    : '<span class="text-admin-text-muted">регламента нет</span>';

                const days = info.days.map(d => `
                    <div class="flex items-center gap-3 py-1.5">
                        <div class="w-40 shrink-0">${miDay(d.date)}
                            <span class="text-[11px] text-admin-text-muted">${d.entries ? d.entries + ' заявок' : 'заявок нет'}</span>
                        </div>
                        <input type="time" data-date="${d.date}" value="${d.warmUpLocal ?? ''}"
                               class="bg-transparent border border-admin-border rounded px-2 py-1 text-[13px]">
                    </div>`).join('');

                miBody.innerHTML = `
                    <div class="rounded border border-admin-border p-3">
                        <div>Забор по регламенту: <b>${auto}</b> · ${reg}</div>
                        <label class="mt-2 flex items-center gap-2">
                            <span>Переопределить:</span>
                            <select id="meetinfo-override" class="bg-transparent border border-admin-border rounded px-2 py-1 text-[13px]">
                                <option value=""${ovr == null ? ' selected' : ''}>как определил забор</option>
                                <option value="true"${ovr === true ? ' selected' : ''}>чемпионат</option>
                                <option value="false"${ovr === false ? ' selected' : ''}>не чемпионат</option>
                            </select>
                        </label>
                        <div class="mt-1 text-[11px] text-admin-text-muted">Витрина видит: <b>${info.championshipEffective ? 'чемпионат' : 'не чемпионат'}</b></div>
                    </div>
                    <div class="mt-3 font-semibold">Начало разминки по дням</div>
                    ${days || '<div class="text-admin-text-muted">Дней не нашлось: ни заявок, ни дат во «Входящих».</div>'}`;
            };

            document.getElementById('meetinfo-close').addEventListener('click', () => miDlg.close());

            window.discMeetInfo = async (id) => {
                miId = id;
                miHint.textContent = '';
                miBody.textContent = 'Загружаю…';
                document.getElementById('meetinfo-title').textContent = discName(id);
                if (!miDlg.open) miDlg.showModal();
                try {
                    miRender(await apiJson(`${API}/${id}/meet-info`));
                } catch (e) {
                    miBody.textContent = `Не вышло: ${e.message}`;
                }
            };

            document.getElementById('meetinfo-save').addEventListener('click', async (e) => {
                if (miId == null) return;
                const btn = e.currentTarget;
                const ovr = document.getElementById('meetinfo-override')?.value ?? '';
                const days = [...miBody.querySelectorAll('input[data-date]')]
                    .map(i => ({ date: i.dataset.date, warmUpLocal: i.value || null }));
                btn.disabled = true;
                miHint.textContent = 'Сохраняю…';
                try {
                    const info = await apiJson(`${API}/${miId}/meet-info`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ isChampionshipOverride: ovr === '' ? null : ovr === 'true', days })
                    });
                    miRender(info);
                    miHint.textContent = 'Сохранено';
                } catch (err) {
                    miHint.textContent = `Не вышло: ${err.message}`;
                } finally {
                    btn.disabled = false;
                }
            });

            document.getElementById('btn-cancel-preview').addEventListener('click', () => {
                preview = null;
                document.getElementById('preview-error').classList.add('hidden');
                pullFlow.cancel();
            });
            document.getElementById('event-select').addEventListener('change', (e) => {
                document.getElementById('event-name').classList.toggle('hidden', e.target.value !== '__new__');
            });

            // Завести правило по снятой шкале. Шкалу сервер берёт из своего кэша превью —
            // отсюда уезжает только имя версии.
            document.getElementById('btn-create-rule').addEventListener('click', async (e) => {
                if (!preview) return;
                const btn = e.currentTarget;
                const version = document.getElementById('preview-rule-version').value.trim();
                btn.disabled = true;
                try {
                    const r = await apiJson(`${API}/club-rule`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ previewId: preview.previewId, version })
                    });

                    // Правило создано — кладём в селект и выбираем, чтобы импорт ушёл уже с ним.
                    const select = document.getElementById('preview-rule-clubs');
                    const opt = document.createElement('option');
                    opt.value = String(r.ruleId);
                    opt.textContent = `${r.version} · manual`;
                    select.appendChild(opt);
                    select.value = String(r.ruleId);

                    document.getElementById('preview-rule-create').classList.add('hidden');
                    const box = document.getElementById('preview-standing');
                    box.className = 'mb-2 rounded-md border border-admin-success/40 bg-admin-success/10 px-3 py-2 text-[13px] text-admin-text';
                    box.textContent = `Правило «${r.version}» заведено и подставлено — соревнование импортируется с ним.`;
                } catch (err) {
                    alert(`Правило не заведено: ${err.message}`);
                    btn.disabled = false;
                }
            });

            document.getElementById('btn-import').addEventListener('click', async () => {
                if (!preview) return;
                const allowCollapsed = collapsedOverride();
                if (preview.collapsed && !allowCollapsed) {
                    alert('Импорт заблокирован: ' + preview.collapsed.message
                        + '\n\nЕсли блок ложный — отметь «Я проверил — импортировать всё равно» в красном блоке.');
                    return;
                }
                // Бассейн протокол не пишет, а у парсера умолчание 25 м (И-30: летний чемпионат
                // лёг 25-метровым). Файл, где есть хоть один НОВЫЙ день, без явного выбора не
                // импортируем (Р118: новый день события иначе молча получит умолчание парсера);
                // существующим дням бассейн не меняется — пустое поле им ничего не делает.
                if (preview.needsPool && !document.getElementById('flag-pool').value) {
                    alert('Выбери длину бассейна (25 / 50 м): протокол её не указывает, регламент тоже.');
                    document.getElementById('flag-pool').focus();
                    return;
                }
                const discId = preview.id;
                const sib = preview.sibling;
                if (sib && !sib.eventId && !document.getElementById('sibling-event-name').value.trim()) {
                    alert('Впиши общее название события — оба дня получат его.');
                    document.getElementById('sibling-event-name').focus();
                    return;
                }
                if (!pullFlow.beginImport(discId)) return;
                const categoryKeys = [...document.querySelectorAll('.disc-cat:checked')].map(i => i.value);
                const eventChoice = document.getElementById('event-select').value;
                const suspectFlags = () => [...document.querySelectorAll('.suspect-flag:checked')]
                    .map(i => ({ rowIndex: Number(i.value), note: i.dataset.note || null }));
                // Р124: день-сосед — свой эндпоинт (сервер перепроверяет кандидата, дату, пересечение и
                // параллельный импорт); событие решает сервер: лежащего дня или новое с этим именем.
                const send = sib
                    ? apiJson(`${PROGRAMME_API}/${discId}/sibling-import`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            previewId: preview.previewId,
                            logligId: sib.logligId,
                            categoryKeys: categoryKeys.length ? categoryKeys : null,
                            eventName: sib.eventId ? null : document.getElementById('sibling-event-name').value.trim(),
                            poolType: document.getElementById('flag-pool').value || null,
                            isAward: document.getElementById('flag-award').checked,
                            isChampionship: document.getElementById('flag-championship').checked,
                            isMasters: document.getElementById('flag-masters').checked,
                            clubPointsDisabled: document.getElementById('flag-club-off').checked,
                            pointRuleClubsId: Number(document.getElementById('preview-rule-clubs').value) || null,
                            suspectFlags: suspectFlags()
                        })
                    })
                    : null;
                try {
                    const r = send ? await send : await apiJson(`${API}/import`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            previewId: preview.previewId,
                            categoryKeys: categoryKeys.length ? categoryKeys : null,
                            eventId: eventChoice && eventChoice !== '__new__' ? Number(eventChoice) : null,
                            newEventName: eventChoice === '__new__' ? document.getElementById('event-name').value.trim() : null,
                            overwriteExisting: document.getElementById('overwrite-existing-cb').checked,
                            deleteMissing: document.getElementById('overwrite-existing-cb').checked && document.getElementById('delete-missing-cb').checked,
                            // Правило клубных очков: подставлено по шкале официального зачёта
                            // либо выбрано вручную. Пусто — автоподбор по дате и типу.
                            pointRuleClubsId: Number(document.getElementById('preview-rule-clubs').value) || null,
                            // Флаги соревнования: уезжает то, что видел человек, — уже с его правками.
                            isAward: document.getElementById('flag-award').checked,
                            isChampionship: document.getElementById('flag-championship').checked,
                            isMasters: document.getElementById('flag-masters').checked,
                            clubPointsDisabled: document.getElementById('flag-club-off').checked,
                            poolType: document.getElementById('flag-pool').value || null,
                            // Галочки «пометить заплыв сомнительным» у строк-рекордов: строка
                            // ляжет в БД сразу помеченной и не побудет рекордом на витрине.
                            suspectFlags: [...document.querySelectorAll('.suspect-flag:checked')]
                                .map(i => ({ rowIndex: Number(i.value), note: i.dataset.note || null })),
                            // Р119: явное согласие на импорт свёрнутого протокола (галочка «Я проверил»).
                            // Сервер примет его, только если у этого превью блок был.
                            allowCollapsedProtocol: allowCollapsed
                        })
                    });
                    preview = null;
                    document.getElementById('preview-error').classList.add('hidden');
                    syncImportButton();
                    pullFlow.importQueued(r.jobId, discId);
                } catch (e) {
                    pullFlow.importNotStarted(discId);
                    syncImportButton();
                    // Превью остаётся открытым: поправить и нажать снова (истёк previewId — «Отмена» и «Затянуть»).
                    const err = document.getElementById('preview-error');
                    err.textContent = `Импорт не запустился: ${e.message}`;
                    err.classList.remove('hidden');
                }
            });
        })();

        // ── Проверка качества результатов (SuspectResultsAdminController) ────────
        // Ищет ошибки САМОГО источника — то, что парсером не лечится: протокол напечатан
        // так, как напечатан (эталон — 00:32.59 на 100 м баттерфляем у Маккабиады-2026).
        // Помеченная строка остаётся в результатах, но выпадает из детекции рекордов.
        (function () {
            const API = '/api/admin/suspect-results';
            const dlg = document.getElementById('quality-modal');
            const body = document.getElementById('quality-body');
            const titleEl = document.getElementById('quality-title');
            const statsEl = document.getElementById('quality-stats');
            const scanBtn = document.getElementById('quality-scan');
            let scope = null; // { eventId } | { competitionId }

            const REASONS = {
                time_vs_distance: 'Быстрее мирового рекорда',
                relay_time_in_individual: 'Время отрезка, не дистанции',
                time_outlier: 'Выброс по времени',
                gender_mismatch: 'Пол не совпадает',
                duplicate_swim: 'Повтор дисциплины за день',
                personal_outlier: 'Выброс по личным результатам',
                seed_time_jump: 'Заплыв намного быстрее заявок',
                manual: 'Помечено вручную'
            };

            function esc(s) {
                return String(s ?? '').replace(/[&<>"']/g, c =>
                    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
            }

            async function api(url, opts = {}) {
                opts.headers = { 'X-XSRF-TOKEN': _csrfToken, ...(opts.headers ?? {}) };
                const res = await fetch(url, { credentials: 'same-origin', ...opts });
                let data = null;
                try { data = await res.json(); } catch { /* пустое тело */ }
                if (!res.ok) throw new Error(data?.error ?? `${res.status} ${res.statusText}`);
                return data;
            }

            const qs = () => scope.eventId
                ? `eventId=${scope.eventId}`
                : `competitionId=${scope.competitionId}`;

            function render(rows) {
                if (!rows.length) {
                    body.innerHTML = '<div class="text-sm text-admin-text-muted py-6 text-center">'
                        + 'Подозрительных результатов нет.</div>';
                    return;
                }
                body.innerHTML = `
                    <table class="admin-table w-full text-sm">
                        <thead><tr>
                            <th>Пловец</th><th>Дисциплина</th><th class="text-right">Время</th>
                            <th>Причина</th><th></th>
                        </tr></thead>
                        <tbody>${rows.map(r => `
                            <tr>
                                <td>
                                    <div class="font-medium">${esc(r.swimmerName)}</div>
                                    <div class="text-[11px] text-admin-text-muted">${esc(r.club)}</div>
                                </td>
                                <td class="whitespace-nowrap">${esc(r.distance)} ${esc(r.styleName)}
                                    <span class="text-admin-text-muted">· ${esc(r.gender)}</span></td>
                                <td class="text-right tabular-nums font-semibold">${esc(r.time)}</td>
                                <td>
                                    <span class="inline-block py-0.5 px-1.5 rounded text-[10px] font-semibold
                                        ${r.isManual ? 'bg-admin-accent/15 text-admin-accent' : 'bg-admin-warning/15 text-admin-warning'}">
                                        ${esc(REASONS[r.reason] ?? r.reason)}${r.isManual ? ' (вручную)' : ''}</span>
                                    <div class="text-[11px] text-admin-text-muted mt-0.5">${esc(r.note ?? '')}</div>
                                </td>
                                <td class="col-actions text-right whitespace-nowrap">
                                    <button type="button" class="admin-btn admin-btn-ghost py-1 px-2 quality-clear"
                                            data-id="${r.resultId}" title="Строка в порядке — снять пометку">Снять</button>
                                </td>
                            </tr>`).join('')}
                        </tbody>
                    </table>`;
            }

            // ── Ручная пометка: найти строку и пометить ──────────────────────────────
            const searchInput = document.getElementById('quality-search');
            const searchBtn = document.getElementById('quality-search-btn');
            const searchHint = document.getElementById('quality-search-hint');
            const foundEl = document.getElementById('quality-found');

            function renderFound(rows) {
                if (!rows.length) {
                    foundEl.innerHTML = '<div class="text-[12px] text-admin-text-muted">Ничего не нашлось.</div>';
                    return;
                }
                foundEl.innerHTML = `
                    <table class="admin-table w-full text-[13px]">
                        <tbody>${rows.map(r => `
                            <tr>
                                <td>${esc(r.swimmerName)} <span class="text-admin-text-muted">· ${esc(r.club)}</span></td>
                                <td class="whitespace-nowrap">${esc(r.distance)} ${esc(r.styleName)}</td>
                                <td class="text-right tabular-nums font-semibold">${esc(r.time)}</td>
                                <td class="col-actions text-right whitespace-nowrap">
                                    ${r.reason
                                        ? '<span class="text-[11px] text-admin-text-muted">уже помечено</span>'
                                        : `<button type="button" class="admin-btn admin-btn-ghost py-1 px-2 quality-flag"
                                                data-id="${r.resultId}">Пометить</button>`}
                                </td>
                            </tr>`).join('')}
                        </tbody>
                    </table>`;
            }

            async function runSearch() {
                const q = searchInput.value.trim();
                if (q.length < 2) { searchHint.textContent = 'Минимум 2 символа'; return; }
                searchHint.textContent = 'Ищу…';
                try {
                    const rows = await api(`${API}/search?${qs()}&q=${encodeURIComponent(q)}`);
                    searchHint.textContent = `найдено ${rows.length}`;
                    renderFound(rows);
                } catch (err) { searchHint.textContent = 'Ошибка: ' + err.message; }
            }

            searchBtn.addEventListener('click', runSearch);
            searchInput.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); runSearch(); } });

            foundEl.addEventListener('click', async (e) => {
                const btn = e.target.closest('.quality-flag');
                if (!btn) return;
                // Заметка обязательна: пометка без причины через полгода неотличима от случайного клика.
                const note = prompt('Почему строка недостоверна? (попадёт в аудит и в список)');
                if (note === null || !note.trim()) return;
                btn.disabled = true;
                try {
                    await api(`${API}/${btn.dataset.id}/manual`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ flagged: true, note: note.trim() })
                    });
                    render(await api(`${API}?${qs()}`));
                    await runSearch();
                    toast('Помечено вручную — переимпорт и повторная проверка это сохранят');
                } catch (err) { btn.disabled = false; toast('Ошибка: ' + err.message, 6000); }
            });

            body.addEventListener('click', async (e) => {
                const btn = e.target.closest('.quality-clear');
                if (!btn) return;
                btn.disabled = true;
                try {
                    await api(`${API}/${btn.dataset.id}/manual`, {
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ flagged: false, note: null })
                    });
                    render(await api(`${API}?${qs()}`));
                    toast('Пометка снята — повторная проверка её не вернёт');
                } catch (err) { btn.disabled = false; toast('Ошибка: ' + err.message, 6000); }
            });

            scanBtn.addEventListener('click', async () => {
                scanBtn.disabled = true;
                const prev = scanBtn.textContent;
                scanBtn.textContent = 'Проверяю…';
                try {
                    const r = await api(`${API}/scan?${qs()}`, { method: 'POST' });
                    statsEl.textContent = `просмотрено ${r.scanned}, помечено ${r.flagged}, `
                        + `снято ${r.cleared}, ручных решений сохранено ${r.manualKept}`;
                    render(r.rows);
                } catch (err) { toast('Ошибка проверки: ' + err.message, 6000); }
                finally { scanBtn.disabled = false; scanBtn.textContent = prev; }
            });

            document.querySelectorAll('.quality-btn').forEach(btn => {
                btn.addEventListener('click', async () => {
                    scope = btn.dataset.eventId
                        ? { eventId: Number(btn.dataset.eventId) }
                        : { competitionId: Number(btn.dataset.competitionId) };
                    titleEl.textContent = btn.dataset.title || 'Качество результатов';
                    statsEl.textContent = '';
                    searchInput.value = '';
                    searchHint.textContent = '';
                    foundEl.innerHTML = '';
                    body.innerHTML = '<div class="text-sm text-admin-text-muted py-6 text-center">Загружаю…</div>';
                    dlg.showModal();
                    try { render(await api(`${API}?${qs()}`)); }
                    catch (err) { body.innerHTML = `<div class="text-sm text-admin-danger py-6 text-center">${esc(err.message)}</div>`; }
                });
            });

            document.getElementById('quality-close').addEventListener('click', () => dlg.close());
        })();

        // ── Источники стартового протокола (CompetitionSourcesAdminController) ───
        // Один наш старт бывает собран из НЕСКОЛЬКИХ compID федерации: окружные чемпионаты
        // 8-11 лежат под четырьмя. Импорт знает только тот compID, из которого пришёл файл,
        // остальные привязывают здесь — иначе таб Start list показывает один округ из четырёх.
        (function () {
            const API = '/api/admin/competition-sources';
            const dlg = document.getElementById('sources-modal');
            if (!dlg) return;
            const titleEl = document.getElementById('sources-title');
            const statusEl = document.getElementById('sources-status');
            const body = document.getElementById('sources-body');

            const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c =>
                ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

            const setStatus = (text, cls) => {
                statusEl.textContent = text;
                statusEl.className = 'mt-3 text-[13px] ' + (cls || 'text-admin-text-muted');
            };

            async function api(url, opts = {}) {
                opts.headers = { 'X-XSRF-TOKEN': _csrfToken, ...(opts.headers ?? {}) };
                const res = await fetch(url, { credentials: 'same-origin', ...opts });
                let payload = null;
                try { payload = await res.json(); } catch { /* пустое тело */ }
                if (!res.ok) throw new Error(payload?.error ?? `HTTP ${res.status} ${res.statusText}`);
                return payload;
            }

            function render(view) {
                titleEl.textContent = view.competitionName;

                // Селект дня: источник принадлежит конкретному дню многодневки. У однодневного
                // соревнования выбора нет, но селект оставляем — так один путь кода.
                const dayOptions = view.days.map(d =>
                    `<option value="${d.competitionId}">${d.dayNumber ? 'День ' + d.dayNumber + ' · ' : ''}${esc(d.date)}</option>`
                ).join('');

                const linkedRows = view.linked.length
                    ? view.linked.map(l => `
                        <tr>
                            <td class="whitespace-nowrap">
                                <a href="https://isr.org.il/comp.asp?compID=${l.orgCompId}" target="_blank" rel="noopener"
                                   class="text-admin-accent hover:underline">${l.orgCompId} ↗</a>
                            </td>
                            <td class="whitespace-nowrap text-admin-text-muted">${esc(l.date ?? '—')}</td>
                            <td dir="rtl" style="text-align:left" class="text-admin-text-muted">${esc(l.sourceName ?? '—')}</td>
                            <td class="text-right tabular-nums">${l.entries}</td>
                            <td class="col-actions text-right whitespace-nowrap">
                                <button type="button" class="admin-btn admin-btn-danger py-1 px-2.5 src-unlink"
                                        data-comp="${l.competitionId}" data-org="${l.orgCompId}">Отвязать</button>
                            </td>
                        </tr>`).join('')
                    : '<tr><td colspan="5" class="text-admin-text-muted">Привязок нет — таба Start list у соревнования не будет.</td></tr>';

                const candidateRows = view.candidates.length
                    ? view.candidates.map(c => `
                        <tr>
                            <td class="whitespace-nowrap">
                                <a href="https://isr.org.il/comp.asp?compID=${c.orgCompId}" target="_blank" rel="noopener"
                                   class="text-admin-accent hover:underline">${c.orgCompId} ↗</a>
                                ${c.linkedElsewhere ? '<span class="ml-1 text-[10px] font-bold text-amber-400" title="Этот compID уже привязан к ДРУГОМУ соревнованию">занят</span>' : ''}
                            </td>
                            <td class="whitespace-nowrap text-admin-text-muted">${esc(c.date)}</td>
                            <td dir="rtl" style="text-align:left" class="text-admin-text-muted">${esc(c.name)}</td>
                            <td class="text-admin-text-muted">${esc(c.status)}</td>
                            <td class="col-actions text-right whitespace-nowrap">
                                <button type="button" class="admin-btn admin-btn-primary py-1 px-2.5 src-link"
                                        data-org="${c.orgCompId}">Привязать</button>
                            </td>
                        </tr>`).join('')
                    : '<tr><td colspan="5" class="text-admin-text-muted">Кандидатов в датах этого соревнования нет.</td></tr>';

                body.innerHTML = `
                    <div class="text-[12px] font-semibold text-admin-text-muted">Привязано</div>
                    <table class="admin-table mt-1"><tbody>${linkedRows}</tbody></table>

                    <div class="mt-4 flex items-center gap-2">
                        <div class="text-[12px] font-semibold text-admin-text-muted">Кандидаты из «Входящих»</div>
                        <span class="text-[11px] text-admin-text-muted">привязать к дню:</span>
                        <select id="src-day" class="admin-input py-1 px-2 text-[13px] w-auto">${dayOptions}</select>
                    </div>
                    <table class="admin-table mt-1"><tbody>${candidateRows}</tbody></table>`;
            }

            async function load(id) {
                setStatus('Загружаю…');
                try {
                    render(await api(`${API}/${id}`));
                    setStatus('');
                } catch (e) {
                    setStatus(`ОШИБКА — ${e.message}`, 'text-red-400');
                }
            }

            document.getElementById('sources-close').addEventListener('click', () => dlg.close());

            document.querySelectorAll('.sources-btn').forEach(btn => {
                btn.addEventListener('click', () => {
                    titleEl.textContent = btn.dataset.title || '';
                    body.innerHTML = '';
                    setStatus('');
                    if (!dlg.open) dlg.showModal();
                    load(Number(btn.dataset.competitionId));
                });
            });

            // Делегирование: строки таблиц перерисовываются после каждой правки.
            body.addEventListener('click', async (e) => {
                const link = e.target.closest('.src-link');
                const unlink = e.target.closest('.src-unlink');
                if (!link && !unlink) return;

                // Привязываем к ВЫБРАННОМУ дню, отвязываем — от того, где привязка лежит.
                const targetComp = link
                    ? Number(document.getElementById('src-day').value)
                    : Number(unlink.dataset.comp);
                const orgCompId = Number((link ?? unlink).dataset.org);
                const url = `${API}/${link ? 'link' : 'unlink'}`;

                (link ?? unlink).disabled = true;
                setStatus(link ? `Привязываю ${orgCompId}…` : `Отвязываю ${orgCompId}…`);
                try {
                    const view = await api(url, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ competitionId: targetComp, orgCompId })
                    });
                    render(view);
                    setStatus(link
                        ? `compID ${orgCompId} привязан. Теперь затяните его стартовый протокол во «Входящих».`
                        : `compID ${orgCompId} отвязан. Заявки остались в базе.`, 'text-emerald-400');
                } catch (err) {
                    setStatus(`ОШИБКА — ${err.message}`, 'text-red-400');
                    (link ?? unlink).disabled = false;
                }
            });
        })();

        // Ход одиночного затягивания после F5 — когда все обработчики выше уже объявлены.
        pullFlow.restore();
