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
                form.querySelectorAll('input[name], select[name]').forEach(el => {
                    if (el.type === 'checkbox') { if (!el.checked) el.disabled = true; }
                    else if (el.value === '') el.disabled = true;
                });
                // Смена фильтров = новая выборка: страницу пагинации сбрасываем (p не в форме).
            });
            form.querySelectorAll('select, input[type=checkbox]').forEach(el =>
                el.addEventListener('change', () => form.requestSubmit()));
        })();

        // Одна панель на строку: действия + дни события + быстрая правка. Состояние открытых
        // строк переживает перезагрузку (после сохранения страница редиректится) — sessionStorage.
        (function () {
            const KEY = 'admin-competitions-open-rows';
            const open = new Set(JSON.parse(sessionStorage.getItem(KEY) || '[]'));

            const apply = (btn, isOpen) => {
                const panel = document.getElementById('panel-' + btn.dataset.key);
                if (!panel) return;
                panel.classList.toggle('hidden', !isOpen);
                btn.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
                btn.textContent = isOpen ? '▾ Закрыть' : '▸ Открыть';
                // Подсветка: строка-заголовок и её панель — один блок. Без неё при
                // нескольких раскрытых строках непонятно, что именно сейчас тянется.
                btn.closest('tr')?.classList.toggle('row-open', isOpen);
                panel.classList.toggle('row-panel-open', isOpen);
            };

            document.querySelectorAll('.row-toggle').forEach(btn => {
                if (open.has(btn.dataset.key)) apply(btn, true);
                btn.addEventListener('click', () => {
                    const isOpen = btn.getAttribute('aria-expanded') !== 'true';
                    apply(btn, isOpen);
                    isOpen ? open.add(btn.dataset.key) : open.delete(btn.dataset.key);
                    sessionStorage.setItem(KEY, JSON.stringify([...open]));
                });
            });
        })();

        // Подтверждение каскадного удаления (одиночное / день / всё событие) — как в /Admin/Competitions.
        document.querySelectorAll('.comp-delete-form').forEach(form => {
            form.addEventListener('submit', async (e) => {
                e.preventDefault();
                e.stopPropagation();
                const kind = form.dataset.kind;
                const name = form.dataset.name || 'соревнование';
                const rows = form.dataset.rows || '0';
                let title, message;
                if (kind === 'event') {
                    const days = form.dataset.days || '?';
                    title = 'Удалить всё событие?';
                    message = `«${name}» — будут удалены ВСЕ ${days} дн. вместе с результатами (${rows} строк), эстафетами и галереями. Действие необратимо.`;
                } else if (kind === 'day') {
                    title = 'Удалить день?';
                    message = `«${name}» — будут удалены его результаты (${rows} строк). Действие необратимо.`;
                } else {
                    title = 'Удалить соревнование?';
                    message = `«${name}» — будут удалены его результаты (${rows} строк), эстафеты и галереи. Действие необратимо.`;
                }
                const ok = await adminConfirm({ title, message, confirmText: 'Удалить', danger: true });
                if (ok) form.submit();
            });
        });

        // ── Панель хода затягивания ─────────────────────────────────────────────
        // Затягивание идёт минуты и в несколько шагов (скачать PDF → распарсить → превью →
        // фоновый job импорта), а тост живёт секунды и не переживает перезагрузку. Панель
        // держит шаги на экране, опрашивает job до конца и после успеха обновляет таблицу,
        // помечая строку бейджем «затянуто».
        window.pullPanel = (function () {
            const PENDING_KEY = 'pullPending';  // job, за которым следим (переживает F5)
            const DONE_KEY = 'pullDone';        // итог, который надо показать после reload

            const panel = document.getElementById('pull-panel');
            const nameEl = document.getElementById('pull-name');
            const stepsEl = document.getElementById('pull-steps');
            const footerEl = document.getElementById('pull-footer');
            let steps = [];  // [{ text, state: 'run'|'ok'|'fail'|'wait' }]

            const ICON = {
                run: '<span class="text-admin-accent">●</span>',
                ok: '<span class="text-admin-success">✓</span>',
                fail: '<span class="text-[#ef9a9a]">✕</span>',
                wait: '<span class="text-admin-text-muted">○</span>',
            };

            function render() {
                stepsEl.innerHTML = steps.map(s => `
                    <li class="flex gap-2 ${s.state === 'fail' ? 'text-[#ef9a9a]' : s.state === 'wait' ? 'text-admin-text-muted' : ''}">
                        <span class="w-3.5 shrink-0">${ICON[s.state] ?? ''}</span>
                        <span class="min-w-0">${s.text}</span>
                    </li>`).join('');
            }

            function open(name) {
                steps = [];
                nameEl.textContent = name ?? '';
                footerEl.classList.add('hidden');
                footerEl.textContent = '';
                panel.classList.remove('hidden');
                render();
            }

            /// Новый шаг «в работе»; предыдущий незакрытый считаем успешным.
            function step(text) {
                const last = steps[steps.length - 1];
                if (last && last.state === 'run') last.state = 'ok';
                steps.push({ text, state: 'run' });
                render();
            }

            function finish(state, text) {
                const last = steps[steps.length - 1];
                if (last && last.state === 'run') last.state = state === 'fail' ? 'fail' : 'ok';
                if (text) steps.push({ text, state });
                render();
            }

            function footer(html) {
                footerEl.innerHTML = html;
                footerEl.classList.remove('hidden');
            }

            document.getElementById('pull-close').addEventListener('click', () => {
                panel.classList.add('hidden');
                sessionStorage.removeItem(PENDING_KEY);
            });

            // Бейдж на строке: вешаем в ячейку стадии, рядом с «импортировано».
            function markRow(discId) {
                const row = document.querySelector(`tr[data-disc-id="${discId}"]`);
                if (!row) return false;
                const cell = row.querySelector('td');
                if (cell && !cell.querySelector('.pulled-badge')) {
                    cell.insertAdjacentHTML('beforeend',
                        '<span class="pulled-badge ml-1 inline-block rounded px-1.5 py-0.5 text-[10px] font-bold uppercase bg-admin-success/20 text-admin-success">затянуто</span>');
                }
                row.scrollIntoView({ behavior: 'smooth', block: 'center' });
                return true;
            }

            // Опрос job'а. Импорт идёт в фоне, поэтому единственный честный признак конца —
            // состояние job'а, а не ответ на POST /import (он лишь ставит в очередь).
            async function watch(jobId, discId, name) {
                sessionStorage.setItem(PENDING_KEY, JSON.stringify({ jobId, discId, name }));
                const deadline = Date.now() + 10 * 60 * 1000;
                let running = false;

                while (Date.now() < deadline) {
                    await new Promise(r => setTimeout(r, 1500));
                    let s;
                    try {
                        s = await fetch(`/api/admin/import/status/${jobId}`, { credentials: 'same-origin' })
                            .then(r => r.ok ? r.json() : null);
                    } catch { continue; } // сеть моргнула — пробуем дальше
                    if (!s) continue;

                    if (s.state === 'running' && !running) { running = true; step('Импортирую результаты…'); }

                    if (s.state === 'completed') {
                        const r = s.result ?? {};
                        sessionStorage.removeItem(PENDING_KEY);
                        sessionStorage.setItem(DONE_KEY, JSON.stringify({
                            discId, name, created: r.created ?? 0, total: r.totalRows ?? 0,
                            skipped: r.skipped ?? 0, errors: r.errors ?? 0,
                            // Сверка с файлом (Д1): главный признак того, что импорт лёг верно.
                            recon: r.reconciliation ?? '', reconBad: r.reconciliationMismatches ?? 0,
                            // Прогон реестра проверок (Д5): гоняется автоматически в конце импорта.
                            checks: r.dataChecks ?? '', checkErr: r.dataCheckErrors ?? 0,
                            checkWarn: r.dataCheckWarnings ?? 0,
                            // Галочки «сомнительный» из превью — подтверждаем, что пометка легла.
                            suspect: r.suspectFlagged ?? 0
                        }));
                        finish('ok', 'Готово. Обновляю таблицу…');
                        setTimeout(() => location.reload(), 700);
                        return;
                    }
                    if (s.state === 'failed') {
                        sessionStorage.removeItem(PENDING_KEY);
                        finish('fail', `Импорт не прошёл: ${esc(s.error ?? 'причина неизвестна')}`);
                        footer('Подробности — в <a class="text-admin-accent hover:underline" href="/Admin/ImportHistory">Import History</a>.');
                        return;
                    }
                }

                finish('fail', 'Жду слишком долго — статус смотри в Import History.');
            }

            // После reload: показать итог и повесить бейдж; если job ещё шёл — продолжить опрос.
            function restore() {
                const done = sessionStorage.getItem(DONE_KEY);
                if (done) {
                    sessionStorage.removeItem(DONE_KEY);
                    const d = JSON.parse(done);
                    open(d.name);
                    steps = [{ text: `Затянуто: создано ${d.created} из ${d.total}`, state: 'ok' }];
                    if (d.skipped) steps.push({ text: `пропущено ${d.skipped}`, state: 'wait' });
                    if (d.errors) steps.push({ text: `ошибок ${d.errors}`, state: 'fail' });
                    // Сверка с файлом важнее счётчиков: она отвечает на вопрос «легло ли верно».
                    if (d.recon) steps.push({ text: esc(d.recon), state: d.reconBad ? 'fail' : 'ok' });
                    // Пометки из превью: молча применённая пометка так же плоха, как молча потерянная.
                    if (d.suspect) steps.push({ text: `Помечено сомнительными: ${d.suspect} (в рекорды не идут)`, state: 'ok' });
                    // Проверки данных прогоняются сами (Д5) — показываем итог тут же.
                    if (d.checks) steps.push({
                        text: esc(d.checks),
                        state: d.checkErr ? 'fail' : (d.checkWarn ? 'wait' : 'ok')
                    });
                    render();

                    const rowFound = markRow(d.discId);
                    if (d.reconBad) {
                        footer('Данные разошлись с протоколом. Разбор — <code>docs/data-integrity.md</code> §8; '
                             + 'чаще всего лечится повторным «Перезатянуть» с галкой «удалять лишние».');
                    } else if (d.checkErr || d.checkWarn) {
                        footer('Проверки данных нашли, что разобрать — '
                             + '<a class="text-admin-accent hover:underline" href="/Admin/Health">Health</a>.');
                    } else if (!rowFound) {
                        footer('Строка не видна при текущих фильтрах — сбрось их, чтобы увидеть её со стадией «импортировано».');
                    } else {
                        footer('Дальше стоит нажать <b>⚑ Качество</b> — автозапуска проверки нет.');
                    }
                    return;
                }

                const pending = sessionStorage.getItem(PENDING_KEY);
                if (pending) {
                    const p = JSON.parse(pending);
                    open(p.name);
                    step('Импорт продолжается в фоне…');
                    watch(p.jobId, p.discId, p.name);
                }
            }

            return { open, step, finish, footer, watch, restore };
        })();

        pullPanel.restore();

        // ── Пакетное затягивание (docs/plans/bulk-pull-plan.md) ────────────────────
        // Забор идёт минутами и живёт на сервере: панель только поллит состояние, а batchId
        // лежит в sessionStorage — перезагрузка страницы пачку не теряет.
        (function () {
            const API = '/api/admin/discovery';
            const KEY = 'admin-bulk-pull-batch';
            const card = document.getElementById('bulk-card');
            const startBtn = document.getElementById('bulk-pull-btn');
            if (!card) return;

            const progress = document.getElementById('bulk-progress');
            const skippedBox = document.getElementById('bulk-skipped');
            const rowsBox = document.getElementById('bulk-rows');
            const importBtn = document.getElementById('bulk-import-btn');
            const importOut = document.getElementById('bulk-import-result');
            let timer = null;
            let batchId = sessionStorage.getItem(KEY);

            function esc(v) {
                return String(v ?? '').replace(/[&<>"']/g, c =>
                    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
            }

            async function api(url, opts = {}) {
                opts.headers = { 'X-XSRF-TOKEN': _csrfToken, ...(opts.headers ?? {}) };
                const res = await fetch(url, { credentials: 'same-origin', ...opts });
                let body = null;
                try { body = await res.json(); } catch { /* пустое тело */ }
                if (!res.ok) throw new Error(body?.error ?? `${res.status} ${res.statusText}`);
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

            // Импортировать из пачки больше нечего: всё затянутое уже ушло в очередь и доимпортировалось.
            const nothingLeft = batch => batch.finished && !batch.importsPending
                && !(batch.rows || []).some(r => r.previewId && !r.queued);

            function renderRows(rows) {
                if (!rows.length) { rowsBox.innerHTML = ''; return; }
                const body = rows.map(r => {
                    // Уже отправленная в импорт строка — не предложение: разбор отдан очереди,
                    // повторно импортировать нечем. Без этого после перезагрузки страницы
                    // строки рисовались отмеченными, и пачка выглядела свежей (06.10.2026).
                    const v = r.queued ? QUEUED : (VERDICTS[r.verdict] ?? VERDICTS.Failed);
                    const canImport = !!r.previewId && !r.queued;
                    // Галочку, которую админ уже трогал, перерисовка сохраняет: поллинг идёт
                    // каждые 2 секунды, и снятые вручную отметки не должны возвращаться.
                    const prev = rowsBox.querySelector(`input.bulk-row[value="${r.discoveredId}"]`);
                    const checked = prev ? prev.checked : (v.checked && canImport);
                    const reasons = (r.reasons || []).map(x => esc(x)).join('<br>');
                    const reg = r.regulationUrl
                        ? `<a href="${esc(r.regulationUrl)}" target="_blank" rel="noopener" class="text-admin-accent hover:underline">תקנון ↗</a>`
                        : '<span class="text-admin-text-muted">—</span>';
                    const flags = [
                        r.hasMedals ? 'медали' : null,
                        r.hasClubStanding ? 'клубный зачёт' : null,
                        r.isChampionship ? '🏆' : null,
                        r.pointRuleClubsId ? `правило #${r.pointRuleClubsId}` : null
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

            function updateImportBtn() {
                // Пока импорт идёт, кнопка «занята» — подпись и доступность ведёт render().
                if (importBtn.getAttribute('aria-busy') === 'true') return;
                const n = rowsBox.querySelectorAll('input.bulk-row:checked').length;
                importBtn.disabled = n === 0;
                importBtn.textContent = n ? `Импортировать отмеченные (${n})` : 'Импортировать отмеченные';
            }

            rowsBox.addEventListener('change', updateImportBtn);

            function render(batch) {
                card.classList.remove('hidden');
                const sent = (batch.rows || []).filter(r => r.queued);
                const done = sent.filter(r => r.importState === 'completed' || r.importState === 'failed');
                const failed = sent.filter(r => r.importState === 'failed').length;
                const spin = '<span class="admin-spinner mr-1.5 align-middle text-admin-accent"></span>';

                if (batch.error) {
                    progress.textContent = batch.error;
                } else if (!batch.finished) {
                    progress.innerHTML = spin + `Тяну ${Math.min(batch.done + 1, batch.total)} из ${batch.total}… `
                        + '<span class="opacity-70">(можно уйти со страницы и вернуться)</span>';
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

                // Кнопки «заняты», пока идёт их работа: забор пачки и импорт отмеченных.
                if (startBtn) adminBusy(startBtn, !batch.finished, 'Тяну…');
                adminBusy(importBtn, !!batch.importsPending, 'Импортирую…');

                const skipped = batch.skippedChampionships || [];
                skippedBox.classList.toggle('hidden', skipped.length === 0);
                if (skipped.length) {
                    skippedBox.innerHTML = `Исключено чемпионатов: ${skipped.length} — `
                        + skipped.map(esc).join(', ')
                        + '. Отметьте «включая чемпионаты», если нужны они.';
                }
                renderRows(batch.rows || []);
            }

            async function poll() {
                if (!batchId) return;
                try {
                    const batch = await api(`${API}/bulk-pull/${batchId}`);
                    render(batch);
                    // Опрос живёт, пока тянется пачка ИЛИ идёт импорт отправленных строк.
                    if (batch.finished && !batch.importsPending) { clearInterval(timer); timer = null; }
                } catch (err) {
                    clearInterval(timer); timer = null;
                    sessionStorage.removeItem(KEY);
                    batchId = null;
                    progress.textContent = err.message;
                }
            }

            function watch() {
                if (timer) clearInterval(timer);
                poll();
                timer = setInterval(poll, 2000);
            }

            if (startBtn) {
                startBtn.addEventListener('click', async () => {
                    const ids = (startBtn.dataset.ids || '').split(',').filter(Boolean).map(Number);
                    const includeChampionships = document.getElementById('bulk-include-champ').checked;
                    adminBusy(startBtn, true, 'Тяну…'); // снимет render(), когда пачка закончится
                    card.classList.remove('hidden');
                    importOut.textContent = '';
                    rowsBox.innerHTML = '';
                    progress.textContent = 'Ставлю пачку…';
                    try {
                        const batch = await api(`${API}/bulk-pull`, {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ ids, includeChampionships })
                        });
                        batchId = batch.batchId;
                        sessionStorage.setItem(KEY, batchId);
                        render(batch);
                        watch();
                    } catch (err) {
                        progress.textContent = err.message;
                        adminBusy(startBtn, false);
                    }
                });
            }

            document.getElementById('bulk-uncheck-noreg').addEventListener('click', () => {
                rowsBox.querySelectorAll('tr[data-verdict="NoRegulation"] input.bulk-row')
                    .forEach(cb => { cb.checked = false; });
                updateImportBtn();
            });

            document.getElementById('bulk-close').addEventListener('click', () => {
                card.classList.add('hidden');
                sessionStorage.removeItem(KEY);
                if (timer) { clearInterval(timer); timer = null; }
                batchId = null;
                // Закрытие только прячет панель: пачка и импорт на сервере идут дальше.
                if (startBtn) adminBusy(startBtn, false);
                adminBusy(importBtn, false);
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
                    const skipped = (r.skipped || []).map(esc).join('<br>');
                    importOut.innerHTML = `В очередь ушло: ${r.queued}. Ход — в таблице выше и в `
                        + '<a href="/Admin/ImportHistory" class="text-admin-accent hover:underline">истории импортов</a>.'
                        + (skipped ? `<div class="mt-1 text-admin-warning">${skipped}</div>` : '');
                    rowsBox.querySelectorAll('input.bulk-row:checked').forEach(cb => { cb.checked = false; cb.disabled = true; });
                    // Опрос мог остановиться на конце забора — запускаем снова: теперь он ждёт
                    // конца импорта, а «занятость» кнопки ведёт render() по importsPending.
                    watch();
                } catch (err) {
                    importOut.innerHTML = `<span class="text-admin-danger">${esc(err.message)}</span>`;
                    adminBusy(importBtn, false);
                    updateImportBtn();
                }
            });

            // Незакрытая пачка с прошлой загрузки страницы — показываем её снова, но только
            // если из неё ещё есть что импортировать. Отработанная пачка (всё затянутое уже в
            // очереди) снова не всплывает: она выглядела бы предложением затянуть то, что
            // уже импортировано.
            if (batchId) {
                api(`${API}/bulk-pull/${batchId}`).then(batch => {
                    if (nothingLeft(batch)) {
                        sessionStorage.removeItem(KEY);
                        batchId = null;
                    } else {
                        watch();
                    }
                }).catch(() => { sessionStorage.removeItem(KEY); batchId = null; });
            }
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
                const panel = btn.closest('.row-panel') ?? btn.closest('td');
                const box = btn.classList.contains('splits-btn')
                    ? panel?.querySelector('.splits-box')
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

            document.getElementById('btn-sync').addEventListener('click', async () => {
                const btn = document.getElementById('btn-sync');
                const year = document.getElementById('sync-year').value;
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
            });

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
            window.discPreview = async function (id, repull) {
                const name = discName(id);
                pullPanel.open(name);
                pullPanel.step(repull
                    ? 'Скачиваю протокол заново и разбираю…'
                    : 'Скачиваю PDF с isr.org.il и разбираю…');
                try {
                    const p = await apiJson(`${API}/${id}/preview`, { method: 'POST' });
                    preview = { previewId: p.previewId, id, name };
                    await showPreview(p, name, repull);
                    pullPanel.finish('ok', `Распознано результатов: ${p.resultCount}`);
                    pullPanel.step('Жду: проверь превью и нажми «Импортировать»');
                    pullPanel.footer(
                        repull && p.existingCompetitionId
                            ? 'Перезапись существующих результатов уже включена. Превью живёт 15 минут.'
                            : repull
                                ? 'Совпадения с существующим соревнованием НЕ найдено — импорт создаст новое. Проверь дату и название.'
                                : 'Превью живёт 15 минут.');
                } catch (e) {
                    pullPanel.finish('fail', `Не удалось разобрать: ${esc(e.message)}`);
                }
            };

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
                    `<div class="font-semibold">${many ? '⚠ ' : ''}Файл побьёт рекордов: ${rp.count}</div>` +
                    (many ? '<div class="text-admin-text-muted">Столько рекордов сразу — обычно признак того, что протокол разобрался неверно. Стоит посмотреть глазами до «Применить».</div>' : '') +
                    '<ul class="mt-2 space-y-2">' + [...byRow.values()].map(g => {
                        const s = g.swim;
                        const recs = g.records.map(r =>
                            `<div class="text-admin-text-muted" dir="rtl" style="text-align:left">${esc(r.kind)}: ${esc(r.recordTime)} ${esc(r.recordHolder)}</div>`).join('');
                        return `<li class="rounded-md border border-admin-border bg-admin-surface/60 px-2.5 py-2">`
                            + `<div class="font-semibold text-admin-text" dir="rtl" style="text-align:left">${esc(s.swimmerName)} · ${esc(s.distance)} ${esc(s.styleName)} · <b>${esc(s.time)}</b></div>`
                            + `<div class="mt-0.5 space-y-0.5 text-[11.5px]">${recs}</div>`
                            + `<div class="mt-1.5 border-t border-admin-border pt-1.5">`
                            + `<label class="flex items-center gap-1.5 cursor-pointer text-admin-text-muted">`
                            + `<input type="checkbox" class="suspect-flag cursor-pointer" value="${s.rowIndex}"`
                            + ` data-note="${esc(suspectNote(g))}"> пометить заплыв сомнительным</label>`
                            // Сюда приезжает вердикт по карточке loglig (или поле для привязки
                            // id и пола, если пловец ещё не привязан) — см. loadRecordCheck.
                            + `<div class="record-check mt-1 text-[11.5px]" data-row="${s.rowIndex}"></div>`
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
                           class="text-admin-accent hover:underline">карточка loglig #${shownId} ↗</a>`
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
                document.getElementById('preview-card').classList.remove('hidden');
                document.getElementById('preview-title').textContent = `Превью: ${name}`;
                document.getElementById('preview-summary').innerHTML =
                    `Распознано результатов: <b>${p.resultCount}</b>` +
                    (p.competitions?.length
                        ? '<ul class="mt-1 list-disc pl-5">' + p.competitions.map(c =>
                            `<li dir="rtl" style="text-align:left">${esc(c.competition)} — ${esc(c.date)} (${c.resultCount})</li>`).join('') + '</ul>'
                        : '');
                const warn = document.getElementById('preview-warnings');
                warn.classList.toggle('hidden', !(p.warnings?.length));
                warn.innerHTML = (p.warnings ?? []).map(esc).join('<br>');

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

                document.getElementById('preview-card').scrollIntoView({ behavior: 'smooth' });
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
                document.getElementById('preview-card').classList.add('hidden');
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
                // Бассейн протокол не пишет, а у парсера умолчание 25 м (И-30: летний чемпионат
                // лёг 25-метровым). Новое соревнование без явного выбора не импортируем; у
                // существующего пустое поле ничего не меняет.
                if (!preview.existingCompetitionId && !document.getElementById('flag-pool').value) {
                    alert('Выбери длину бассейна (25 / 50 м): протокол её не указывает, регламент тоже.');
                    document.getElementById('flag-pool').focus();
                    return;
                }
                const categoryKeys = [...document.querySelectorAll('.disc-cat:checked')].map(i => i.value);
                const eventChoice = document.getElementById('event-select').value;
                try {
                    const r = await apiJson(`${API}/import`, {
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
                                .map(i => ({ rowIndex: Number(i.value), note: i.dataset.note || null }))
                        })
                    });
                    const { id: discId, name } = preview;
                    preview = null;
                    document.getElementById('preview-card').classList.add('hidden');
                    pullPanel.step('Импорт поставлен в очередь…');
                    pullPanel.watch(r.jobId, discId, name);
                } catch (e) { pullPanel.finish('fail', `Импорт не запустился: ${esc(e.message)}`); }
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

    