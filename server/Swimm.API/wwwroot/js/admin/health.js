// Скрипт страницы Pages/Admin/Health.cshtml. Вынесен из разметки как есть (10.10.2026):
// классический скрипт, не модуль — функции из onclick в разметке остаются глобальными.
// Tailwind-классы отсюда собирает @source в Styles/admin.css.
(function () {
    const API = '/api/admin/data-checks';
    // База публичного сайта: пусто = тот же origin (прод), в dev — Vite на своём порту.
    // Сами находки хранят только относительный путь (контракт client/src/utils/routes.ts).
    const PUBLIC_BASE = window.__HEALTH.publicBase;
    // Правила клубных очков для селекта в находке: их единицы, поэтому отдаём страницей.
    const CLUB_RULES = window.__HEALTH.clubRules;
    const SEV = { 2: ['Ошибка', 'bg-admin-danger/20 text-[#ef9a9a]'],
                  1: ['Предупреждение', 'bg-admin-warning/15 text-admin-warning'],
                  0: ['Инфо', 'bg-white/[.08] text-admin-text-muted'] };

    async function api(url, opts = {}) {
        opts.headers = { 'X-XSRF-TOKEN': _csrfToken, ...(opts.headers ?? {}) };
        const res = await fetch(url, { credentials: 'same-origin', ...opts });
        let body = null;
        try { body = await res.json(); } catch { }
        if (!res.ok) throw new Error(body?.error ?? `${res.status} ${res.statusText}`);
        return body;
    }

    function renderSummary(groups) {
        const open = { 2: 0, 1: 0, 0: 0 };
        let accepted = 0;
        groups.forEach(g => { open[g.severity] += g.openCount; accepted += g.acceptedCount; });

        document.getElementById('summary').innerHTML = [2, 1, 0]
            .map(s => `<div class="admin-card px-4 py-2.5">
                <div class="text-[11px] uppercase tracking-wide text-admin-text-muted">${SEV[s][0]}</div>
                <div class="text-xl font-bold ${open[s] ? '' : 'text-admin-text-muted'}">${open[s]}</div></div>`)
            .join('') +
            `<div class="admin-card px-4 py-2.5">
                <div class="text-[11px] uppercase tracking-wide text-admin-text-muted">Принято как есть</div>
                <div class="text-xl font-bold text-admin-text-muted">${accepted}</div></div>`;
    }

    // Пояснение находки: первая строка видна, остальные (причина на каждую заявку у
    // «Заявки без результата», Влад 25.11.2026) — раскрывающимся списком.
    function findingDetails(details) {
        if (!details) return '';
        const [head, ...rest] = details.split('\n');
        const first = `<div class="text-[11px] text-admin-text-muted">${esc(head)}</div>`;
        if (!rest.length) return first;
        return first + `<details class="text-[11px] text-admin-text-muted">
                <summary class="cursor-pointer">причина по каждой заявке — ${rest.filter(l => !l.startsWith('…')).length}</summary>
                <ul class="mt-1 list-disc pl-5">${rest.map(l => `<li>${isolateHebrew(esc(l))}</li>`).join('')}</ul></details>`;
    }

    // Имя на иврите посреди русской строки разворачивает соседние цифры и скобки
    // («#5877 רגב פלג (2016), 4X50» → «רגב פלג #5877 4 ,(2016)X50»). <bdi> изолирует каждый
    // ивритский кусок. Вход — уже экранированный текст.
    function isolateHebrew(html) {
        return html.replace(/[֐-׿][֐-׿\s׳״'\-]*[֐-׿׳]|[֐-׿]/g, m => `<bdi>${m}</bdi>`);
    }

    function findingRow(f) {
        const accepted = f.resolution === 'accepted';
        return `<tr class="${accepted ? 'opacity-60' : ''}">
            <td class="align-top">${f.link ? `<a href="${esc(f.link)}" class="text-admin-accent hover:underline">${esc(f.message)}</a>` : esc(f.message)}
                ${findingDetails(f.details)}
                ${f.subjectName ? `<div class="mt-1 flex items-center gap-1.5">
                    <code class="rounded bg-white/[.06] px-1.5 py-0.5 text-[12px]" dir="auto">${esc(f.subjectName)}</code>
                    <button class="admin-btn admin-btn-ghost py-0.5 px-1.5 text-[11px] finding-copy"
                            data-name="${esc(f.subjectName)}" title="Скопировать имя — искать в интернете">копировать</button>
                    ${f.fixKind === 'swimmer-gender' || f.fixKind === 'swimmer-gender-align' ? renderFixControls(f) : ''}
                </div>` : ''}
                ${f.fixKind === 'competition-club-rule' ? renderClubRule(f) : ''}
                ${f.fixKind === 'swimmer-gender-loglig' && !accepted ? renderLogligGenderReview(f) : ''}
                ${accepted && f.note ? `<div class="text-[11px] text-admin-text-muted">принято: ${esc(f.note)}</div>` : ''}</td>
            <td class="align-top whitespace-nowrap text-[11px] text-admin-text-muted">
                ${f.publicLink ? `<a href="${esc(PUBLIC_BASE + f.publicLink)}" target="_blank" rel="noopener"
                    class="text-admin-accent hover:underline" title="Открыть публичную страницу — посмотреть глазами">на swim-hub ↗</a><br>` : ''}
                ${f.subjectOrgCompId ? `<a href="https://isr.org.il/comp.asp?compID=${f.subjectOrgCompId}"
                    target="_blank" rel="noopener" class="text-admin-accent hover:underline"
                    title="Протокол на сайте федерации — сверить с первоисточником">на isr.org.il ↗</a><br>` : ''}
                с ${new Date(f.firstSeenAt).toLocaleDateString('ru-RU')}</td>
            <td class="align-top text-right whitespace-nowrap">
                ${accepted
                    ? `<button class="admin-btn admin-btn-ghost py-1 px-2 finding-reopen" data-id="${f.id}">Вернуть</button>`
                    : f.fixKind === 'dedup-ignore'
                        // У пары дедупа «принять» значит «это тёзки»: пара уйдёт в
                        // Sys_DedupIgnoredPairs и пропадёт и здесь, и в списке дублей.
                        ? `<button class="admin-btn admin-btn-ghost py-1 px-2 finding-accept" data-id="${f.id}"
                                   title="Не дубли, а тёзки — развязать пару навсегда">Не дубли</button>`
                        : f.fixKind === 'swimmer-gender-loglig'
                            ? `<button class="admin-btn admin-btn-ghost py-1 px-2 finding-accept" data-id="${f.id}"
                                       title="Пол остаётся пустым; находка больше не считается проблемой">Оставить как есть</button>`
                            : `<button class="admin-btn admin-btn-ghost py-1 px-2 finding-accept" data-id="${f.id}">Принять</button>`}
            </td></tr>`;
    }

    // Правило клубных очков выбирается прямо здесь: заходить в карточку соревнования ради
    // одного селекта незачем, а находок бывает десяток за раз. Сохраняем сразу по выбору —
    // отдельная кнопка «ок» на один селект только добавляет клик.
    function renderClubRule(f) {
        const opts = CLUB_RULES.map(r =>
            `<option value="${r.id}" ${f.subjectClubRuleId === r.id ? 'selected' : ''}
                     title="${esc(r.description || '')}">${esc(r.version)}</option>`).join('');
        return `<div class="mt-1 flex items-center gap-1.5">
            <span class="text-[11px] text-admin-text-muted">правило клубных очков:</span>
            <select class="admin-input py-0.5 px-1.5 text-[12px] w-auto finding-club-rule" data-id="${f.id}">
                <option value="">— выбрать —</option>${opts}
            </select>
            <span class="text-[11px] text-admin-text-muted finding-club-rule-info"></span>
        </div>`;
    }

    // Проставленное должно быть ВИДНО: иначе после нажатия непонятно, что уже сделано, и
    // человек жмёт по второму разу. Активный пол — залитая кнопка с галочкой, привязанный
    // loglig — чип со ссылкой на карточку вместо поля ввода.
    const GENDER_TONE = {
        male:   { border: '#4fa8ff', label: '♂', title: 'Мужской' },
        female: { border: '#ff7ab8', label: '♀', title: 'Женский' },
    };

    function genderButton(f, gender) {
        const t = GENDER_TONE[gender];
        const active = f.subjectGender === gender;
        return `<button class="py-0.5 px-2 rounded text-[12px] font-bold cursor-pointer finding-gender
                        ${active ? 'ring-1 ring-offset-0' : ''}"
                    style="border:1px solid ${t.border}${active ? '' : '80'};
                           background:${t.border}${active ? '' : '26'};
                           color:${active ? '#0f1420' : t.border};"
                    data-id="${f.id}" data-gender="${gender}" data-fix-kind="${f.fixKind}"
                    title="${t.title}">${active ? '✓ ' : ''}${t.label}</button>`;
    }

    function renderFixControls(f) {
        return `
            <span class="text-[11px] text-admin-text-muted">пол:</span>
            ${genderButton(f, 'male')}
            ${genderButton(f, 'female')}
            <span class="ml-2 text-[11px] text-admin-text-muted">loglig:</span>
            ${f.subjectLogligId
                ? `<a href="https://loglig.com:2053/Players/Details/${f.subjectLogligId}" target="_blank" rel="noopener"
                       class="rounded bg-admin-success/15 px-1.5 py-0.5 text-[11px] font-bold text-admin-success hover:underline"
                       title="Пловец привязан — открыть карточку">✓ ${f.subjectLogligId} ↗</a>`
                : `<input type="number" class="admin-input py-0.5 px-1.5 w-[7.5rem] text-[12px] finding-loglig-input"
                          data-swimmer="${f.fixEntityId}" placeholder="id с loglig" />
                   <button class="admin-btn admin-btn-ghost py-0.5 px-1.5 text-[11px] finding-loglig"
                           data-swimmer="${f.fixEntityId}" title="Привязать пловца к карточке loglig">привязать</button>`}`;
    }

    // Пол пловца часто уже известен — приехал из другого протокола, где заплыв не был
    // смешанным. Тогда «правильный» ответ есть в базе, и кликать по каждой находке нечего:
    // одна кнопка дописывает его в строки. Гадать за человека она не станет — находки, где
    // пол неизвестен и у пловца, останутся в списке.
    function bulkGenderButton(g) {
        if (g.checkId !== 'results.no-gender' || !g.openCount) return '';
        return `<div class="mt-2 flex flex-wrap items-center gap-1.5">
                <button id="btn-gender-all" class="admin-btn admin-btn-ghost py-1 px-2 text-[12px]"
                    title="Дописать пол в строки тех находок, где пол пловца уже известен">
                    проставить всем по полу пловца</button>
                <button id="btn-gender-loglig" class="admin-btn admin-btn-ghost py-1 px-2 text-[12px]"
                    title="Пловцам без пола с подтверждённым loglig-id — пол с их карточки loglig (имя и год должны совпасть). Запрос на пловца, до 30 за нажатие (не дольше 2,5 мин); проставленный пол не трогает; случаи с серьёзными последствиями уходят в «Пол с loglig ждёт ручной проверки»">
                    взять пол с карточек loglig</button>
                <button id="btn-gender-loglig-last" class="admin-btn admin-btn-ghost py-1 px-1.5 text-[12px]"
                    title="Итог прошлого нажатия «взять пол с карточек loglig» — кто, когда, что вышло и причина по каждому пловцу">ⓘ</button></div>
                <div id="gender-loglig-run-slot">${genderLogligRunBox()}</div>`;
    }

    // Итог кнопки «взять пол с карточек loglig» не пропадает вместе с тостом (просьба Влада
    // 25.11.2026): блок под кнопкой живёт до ✕ и переживает перерисовку списка; после
    // перезагрузки страницы тот же итог достаёт значок ⓘ — из журнала аудита.
    let genderLogligRun = null;

    function genderLogligRunBox() {
        const r = genderLogligRun;
        if (!r) return '';
        const at = r.at ? new Date(r.at).toLocaleString('ru-RU') : '';
        // Причина на каждого пловца — раскрывающимся списком (Влад, 25.11.2026: «как таг details»).
        // Нажатия до 25.11 вечера списка в аудите не хранят — тогда честная подсказка.
        const notes = r.notes?.length
            ? `<details class="mt-1"><summary class="cursor-pointer text-admin-text-muted">причина по каждому пловцу — ${r.notes.length}</summary>
                <ul class="mt-1 list-disc pl-5">${r.notes.map(n => `<li>${esc(n)}</li>`).join('')}</ul></details>`
            : (/ждут ручной проверки|не совпали|не открылась|пола нет/.test(r.message ?? '')
                ? `<div class="mt-1 text-[11px] text-admin-text-muted">Причины по каждому пловцу у этого нажатия не сохранены (оно было до 25.11.2026) — нажмите кнопку ещё раз.</div>`
                : '');
        return `<div class="mt-2 rounded border border-admin-border bg-admin-surface-hover px-3 py-2 text-[12px]">
                <div class="flex items-start gap-2">
                    <div class="flex-1">
                        <div class="text-[11px] text-admin-text-muted">Пол с карточек loglig — ${esc(at)}${r.by ? ` · ${esc(r.by)}` : ''}</div>
                        <div class="text-admin-text">${esc(r.message)}</div>
                    </div>
                    <button id="btn-gender-loglig-run-close" class="text-admin-text-muted hover:text-admin-text" title="Скрыть">✕</button>
                </div>
                ${notes}
            </div>`;
    }

    function showGenderLogligRun() {
        const slot = document.getElementById('gender-loglig-run-slot');
        if (!slot) return;
        slot.innerHTML = genderLogligRunBox();
        const close = document.getElementById('btn-gender-loglig-run-close');
        if (close) close.addEventListener('click', () => { genderLogligRun = null; showGenderLogligRun(); });
    }

    async function showLastGenderLogligRun(btn) {
        btn.disabled = true;
        try {
            const run = await api(`${API}/fix-gender-from-loglig/last`);
            if (!run) { toast('Кнопку «взять пол с карточек loglig» ещё не нажимали'); return; }
            genderLogligRun = run;
            showGenderLogligRun();
        } catch (e) { toast('Ошибка: ' + e.message, 6000); }
        finally { btn.disabled = false; }
    }

    // Р126: автоматика пол не проставила — есть серьёзные последствия или личность не подтверждена
    // заплывами. Перечень последствий — в details находки; «Проставить» пишет пол с карточки
    // loglig осознанно (сервер берёт его из сохранённой карточки, не от страницы).
    function renderLogligGenderReview(f) {
        return `<div class="mt-1 flex items-center gap-1.5">
            <button class="admin-btn admin-btn-ghost py-0.5 px-1.5 text-[11px] finding-gender-confirm" data-id="${f.id}"
                    title="Записать пол с карточки loglig пловцу и его строкам без пола. В журнале останется «подтвердил человек»">
                Проставить</button>
            ${f.subjectLogligId
                ? `<a href="https://loglig.com:2053/Players/Details/${f.subjectLogligId}" target="_blank" rel="noopener"
                       class="text-[11px] text-admin-accent hover:underline">карточка loglig ↗</a>` : ''}
        </div>`;
    }

    async function confirmLogligGender(btn) {
        const ok = await adminConfirm({
            title: 'Проставить пол с карточки loglig?',
            message: 'Последствия перечислены в находке: рекорды, места, медали, очки или топы изменятся. Действие запишется в журнал как подтверждённое человеком.',
            confirmText: 'Проставить',
        });
        if (!ok) return;
        btn.disabled = true;
        try {
            const res = await api(`${API}/${btn.dataset.id}/confirm-gender-from-loglig`, { method: 'POST' });
            const data = await api(`${API}/run`, { method: 'POST' });
            render(data);
            toast(`Пол проставлен, строк дописано: ${res.rows}; соревнований пересчитано: ${res.recalculated}`, 8000);
        } catch (e) { btn.disabled = false; toast('Ошибка: ' + e.message, 6000); }
    }

    // Пол неизвестен и у пловца, но рядом подтверждённый loglig-id — на его карточке пол
    // напечатан (Р125). Сервер идёт по карточкам по одной, поэтому нажатие длится: ~1–2 с на пловца.
    async function fixGenderFromLoglig(btn) {
        btn.disabled = true;
        const prev = btn.textContent;
        btn.textContent = 'иду по карточкам loglig…';
        try {
            const res = await api(`${API}/fix-gender-from-loglig`, { method: 'POST' });
            genderLogligRun = {
                at: new Date().toISOString(),
                message: res.message + (res.alreadyKnown ? ` У ${res.alreadyKnown} пол уже известен — «проставить всем по полу пловца».` : ''),
                notes: res.notes ?? [],
            };
            showGenderLogligRun();
            // Проставлено или кто-то ушёл на ручную проверку — список находок изменился, гоним проверки.
            if (res.set || res.review) {
                const data = await api(`${API}/run`, { method: 'POST' });
                render(data);
                toast(`${res.message} Закрыто находок: ${data.run.fixedCount}`, 12000);
            } else {
                btn.disabled = false;
                btn.textContent = prev;
                toast(res.message + (res.alreadyKnown ? ` У ${res.alreadyKnown} пол уже известен — «проставить всем по полу пловца».` : ''), 12000);
            }
            if (res.notes?.length) console.info('Пол с карточек loglig — причина по каждому пловцу:\n' + res.notes.join('\n'));
        } catch (e) {
            btn.disabled = false; btn.textContent = prev; toast('Ошибка: ' + e.message, 6000);
            genderLogligRun = { at: new Date().toISOString(), message: 'Ошибка: ' + e.message, notes: [] };
            showGenderLogligRun();
        }
    }

    async function fixGenderAll(btn) {
        btn.disabled = true;
        try {
            const res = await api(`${API}/fix-gender-all`, { method: 'POST' });
            if (!res.rows) {
                toast('Нечего проставлять: пол неизвестен и у пловцов — эти находки только руками');
                btn.disabled = false;
                return;
            }
            // Сразу гоним проверки: иначе поправленные находки висели бы до ручного прогона,
            // и кнопка выглядела бы не сработавшей.
            const data = await api(`${API}/run`, { method: 'POST' });
            render(data);
            toast(`Проставлено по ${res.findings} пловцам, строк — ${res.rows}; закрыто находок: ${data.run.fixedCount}`, 6000);
        } catch (e) { btn.disabled = false; toast('Ошибка: ' + e.message, 6000); }
    }

    // Проверки без открытых находок (чистые и «только принятые») — одним свёрнутым блоком внизу
    // (просьба Влада 26.11.2026: «скрой этот див если ошибок нет»). Наверху — только то, на что
    // смотреть; упавшая проверка тоже наверху. Раскрыт ли блок — помним между перерисовками.
    function renderGroups(groups) {
        const host = document.getElementById('groups');
        const quietWasOpen = host.querySelector('#quiet-checks')?.open ?? false;
        const loud = groups.filter(g => g.openCount > 0 || g.failed);
        const quiet = groups.filter(g => !(g.openCount > 0 || g.failed));
        const acceptedOnly = quiet.filter(g => g.acceptedCount > 0).length;

        host.innerHTML = (loud.length
                ? loud.map(groupCard).join('')
                : `<div class="admin-card mb-2 px-4 py-3 text-[13px] text-admin-success">Открытых находок нет — смотреть нечего</div>`)
            + (quiet.length
                ? `<details id="quiet-checks" class="mb-2" ${quietWasOpen ? 'open' : ''}>
                    <summary class="cursor-pointer select-none py-2 text-[12px] text-admin-text-muted">
                        Без открытых находок — ${quiet.length}
                        (чисто ${quiet.length - acceptedOnly}${acceptedOnly ? `, только принятые ${acceptedOnly}` : ''})
                    </summary>
                    ${quiet.map(groupCard).join('')}
                </details>`
                : '');
        wireGroups();
    }

    function groupCard(g) {
            const [sevName, sevCls] = SEV[g.severity];
            const clean = g.openCount === 0 && g.acceptedCount === 0;
            // Без открытых находок (и не упала) серьёзность не главное — главное, что всё в порядке
            // (Влад, 26.11.2026: яркое «Предупреждение» у чистой проверки бросается в глаза).
            // Чистая — зелёная ✓, «только принятые» — серая ✓ с числом; название приглушено.
            const quiet = !(g.openCount > 0 || g.failed);
            const mark = !quiet
                ? `<span class="inline-block rounded px-1.5 py-0.5 text-[10px] font-semibold ${sevCls}">${sevName}</span>`
                : clean
                    ? `<span class="text-[15px] font-bold leading-none text-admin-success" title="Чисто — находок нет">✓</span>`
                    : `<span class="text-[15px] font-bold leading-none text-admin-text-muted" title="Открытых нет — только принятые как есть">✓</span>`;
            return `<details id="check-${esc(g.checkId)}" class="admin-card mb-2 px-4 py-3 scroll-mt-4" ${g.openCount ? 'open' : ''}>
                <summary class="cursor-pointer select-none flex flex-wrap items-center gap-2">
                    ${mark}
                    <span class="font-semibold ${quiet ? 'text-admin-text-muted' : 'text-admin-text'}">${esc(g.title)}</span>
                    ${quiet ? '' : `<span class="font-bold text-admin-text">${g.openCount}</span>`}
                    ${g.acceptedCount ? `<span class="text-[11px] text-admin-text-muted">${quiet ? '' : '+'}${g.acceptedCount} принято</span>` : ''}
                    ${g.total != null && g.total > g.findings.length
                        ? `<span class="text-[11px] text-admin-warning" title="Список находок обрезан — полное число известно из последнего прогона">показано ${g.findings.length} из ${g.total}</span>`
                        : ''}
                    ${g.failed ? '<span class="text-[11px] text-admin-danger">проверка упала</span>' : ''}
                    <code class="ml-auto text-[10px] text-admin-text-muted/70">${esc(g.checkId)}</code>
                </summary>
                <div class="mt-2 text-[12px]">${esc(g.description)}</div>
                ${bulkGenderButton(g)}
                ${g.findings.length ? `<div class="overflow-x-auto mt-2"><table class="admin-table w-full">
                    <tbody>${g.findings.map(findingRow).join('')}</tbody></table></div>` : ''}
            </details>`;
    }

    // Кнопки находок — после каждой перерисовки списка.
    function wireGroups() {
        document.querySelectorAll('.finding-accept').forEach(b => b.addEventListener('click', () => accept(b.dataset.id)));
        document.querySelectorAll('.finding-reopen').forEach(b => b.addEventListener('click', () => reopen(b.dataset.id)));
        document.querySelectorAll('.finding-copy').forEach(b => b.addEventListener('click', () => copyName(b)));
        document.querySelectorAll('.finding-gender').forEach(b =>
            b.addEventListener('click', () => fixGender(b.dataset.id, b.dataset.gender, b, b.dataset.fixKind)));
        document.querySelectorAll('.finding-club-rule').forEach(sel =>
            sel.addEventListener('change', () => fixClubRule(sel)));
        document.querySelectorAll('.finding-loglig').forEach(b =>
            b.addEventListener('click', () => setLoglig(b)));
        const bulk = document.getElementById('btn-gender-all');
        if (bulk) bulk.addEventListener('click', () => fixGenderAll(bulk));
        document.querySelectorAll('.finding-gender-confirm').forEach(b =>
            b.addEventListener('click', () => confirmLogligGender(b)));
        const fromLoglig = document.getElementById('btn-gender-loglig');
        if (fromLoglig) fromLoglig.addEventListener('click', () => fixGenderFromLoglig(fromLoglig));
        const lastLoglig = document.getElementById('btn-gender-loglig-last');
        if (lastLoglig) lastLoglig.addEventListener('click', () => showLastGenderLogligRun(lastLoglig));
        showGenderLogligRun();   // блок пережил перерисовку — повесить ✕ заново
    }

    // Имя субъекта отдельной кнопкой: его копируют, чтобы найти пловца в интернете.
    // Fallback через textarea — clipboard API недоступен без https на некоторых dev-хостах.
    async function copyName(btn) {
        const name = btn.dataset.name ?? '';
        try {
            await navigator.clipboard.writeText(name);
        } catch {
            const ta = document.createElement('textarea');
            ta.value = name;
            document.body.appendChild(ta);
            ta.select();
            document.execCommand('copy');
            ta.remove();
        }
        const prev = btn.textContent;
        btn.textContent = 'скопировано';
        setTimeout(() => { btn.textContent = prev; }, 1200);
    }

    // Проставить пол пловцу прямо из находки. Сервер правит и строки без пола — иначе
    // находка осталась бы висеть до переимпорта и кнопка выглядела бы сломанной.
    async function fixClubRule(sel) {
        const ruleId = Number(sel.value);
        if (!ruleId) return;
        const info = sel.parentElement.querySelector('.finding-club-rule-info');
        sel.disabled = true;
        info.textContent = 'сохраняю…';
        try {
            await api(`${API}/${sel.dataset.id}/fix-club-rule`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ruleId })
            });
            // Зачёт пересчитан на сервере; находка исчезнет на следующем прогоне, поэтому
            // здесь просто показываем, что правило встало.
            info.textContent = '✓ привязано, зачёт пересчитан';
        } catch (e) {
            info.textContent = '';
            toast('Ошибка: ' + e.message, 5000);
        } finally {
            sel.disabled = false;
        }
    }

    // Две находки про пол чинятся одними и теми же кнопками, но разными эндпоинтами:
    // `results.no-gender` дописывает пол в пустые строки, `results.gender-vs-card`
    // перезаписывает разошедшиеся. Эндпоинт выбирается по fixKind находки.
    async function fixGender(id, gender, btn, fixKind) {
        btn.disabled = true;
        try {
            const path = fixKind === 'swimmer-gender-align' ? 'fix-gender-align' : 'fix-gender';
            const res = await api(`${API}/${id}/${path}`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ gender })
            });
            toast(`Пол проставлен, строк поправлено: ${res.rows}. Прогони проверки, чтобы находка закрылась.`);
            await load();
        } catch (e) { btn.disabled = false; toast('Ошибка: ' + e.message, 6000); }
    }

    // Привязка к loglig прямо отсюда: раз человек уже пошёл на loglig искать пол, id он
    // видит там же. Используем ШТАТНЫЙ эндпоинт привязки (/api/admin/loglig/set) — он
    // тянет карточку, сверяет год и клуб и пишет свой аудит; дублировать это в реестре
    // значило бы завести вторую, расходящуюся реализацию.
    async function setLoglig(btn) {
        const input = btn.parentElement.querySelector('.finding-loglig-input');
        const logligId = Number(input?.value);
        if (!Number.isFinite(logligId) || logligId <= 0) { toast('Введите числовой id с loglig'); return; }

        btn.disabled = true;
        try {
            const res = await api('/api/admin/loglig/set', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ swimmerId: Number(btn.dataset.swimmer), logligId })
            });
            // linked=false — доменный отказ (уже привязан, карточка недоступна), не HTTP-ошибка.
            if (res && res.linked === false) { toast('Не привязано: ' + (res.error ?? 'причина неизвестна'), 6000); btn.disabled = false; return; }
            toast(`Привязан loglig #${logligId}`);
            await load();
        } catch (e) { btn.disabled = false; toast('Ошибка: ' + e.message, 6000); }
    }

    function renderHistory(history) {
        document.getElementById('history').innerHTML = history.length === 0
            ? '<div class="italic">Прогонов ещё не было.</div>'
            : `<div class="overflow-x-auto"><table class="admin-table">
                <thead><tr><th>Когда</th><th>Кто запустил</th><th>Ошибки</th><th>Предупр.</th><th>Инфо</th><th>Закрыто</th></tr></thead>
                <tbody>${history.map(r => `<tr>
                    <td class="whitespace-nowrap">${new Date(r.startedAt).toLocaleString('ru-RU')}</td>
                    <td>${esc(r.trigger)}</td>
                    <td>${r.errorCount}</td><td>${r.warningCount}</td><td>${r.infoCount}</td>
                    <td>${r.fixedCount}</td></tr>`).join('')}</tbody></table></div>`;
    }

    // Ссылка «в Health ↗» из блока «Что не так» /Admin/Competitions ведёт на #check-<id проверки>:
    // группа рисуется после загрузки, поэтому браузер сам к якорю не прокрутит — раскрываем и
    // прокручиваем сами, один раз (повторный render после «Принять» страницу не дёргает).
    let hashFocused = false;
    function focusHashCheck() {
        if (hashFocused || !location.hash.startsWith('#check-')) return;
        hashFocused = true;
        const el = document.getElementById(decodeURIComponent(location.hash.slice(1)));
        if (!el) return;
        const quiet = el.closest('#quiet-checks');   // ссылка на проверку в свёрнутом блоке
        if (quiet) quiet.open = true;
        el.open = true;
        el.classList.add('ring-1', 'ring-admin-accent');
        el.scrollIntoView({ block: 'start' });
    }

    function render(data) {
        renderSummary(data.groups);
        renderGroups(data.groups);
        renderHistory(data.history);
        focusHashCheck();
        const last = data.history[0];
        document.getElementById('run-info').textContent = last
            ? `последний прогон: ${new Date(last.startedAt).toLocaleString('ru-RU')}`
            : 'проверки ещё не гонялись';
    }

    async function accept(id) {
        const ok = await adminConfirm({
            title: 'Принять находку как есть?',
            message: 'Она перестанет считаться проблемой и переживёт следующие прогоны. Так помечают неустранимое — например ошибку в протоколе федерации.',
            confirmText: 'Принять',
        });
        if (!ok) return;
        try { await api(`${API}/${id}/accept`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ note: null }) }); await load(); }
        catch (e) { toast('Ошибка: ' + e.message, 5000); }
    }

    async function reopen(id) {
        try { await api(`${API}/${id}/reopen`, { method: 'POST' }); await load(); }
        catch (e) { toast('Ошибка: ' + e.message, 5000); }
    }

    async function load() {
        try { render(await api(API)); }
        catch (e) { document.getElementById('groups').textContent = 'Ошибка: ' + e.message; }
    }

    document.getElementById('btn-run').addEventListener('click', async () => {
        const btn = document.getElementById('btn-run');
        btn.disabled = true;
        document.getElementById('run-info').textContent = 'Гоняю проверки…';
        try {
            const data = await api(`${API}/run`, { method: 'POST' });
            render(data);
            const r = data.run;
            toast(`Прогон завершён: ошибок ${r.errorCount}, предупреждений ${r.warningCount}, закрыто ${r.fixedCount}`, 5000);
        } catch (e) {
            toast('Ошибка прогона: ' + e.message, 6000);
        } finally { btn.disabled = false; }
    });

    load();
})();
