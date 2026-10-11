// Скрипт страницы Pages/Admin/Records/Index.cshtml. Вынесен из разметки как есть (10.10.2026):
// классический скрипт, не модуль — функции из onclick в разметке остаются глобальными.
// Tailwind-классы отсюда собирает @source в Styles/admin.css.
        function toggleAddRecord() { document.getElementById('add-record-panel').classList.toggle('hidden'); }
        function toggleAddStandard() { document.getElementById('add-standard-panel').classList.toggle('hidden'); }
        function toggleAddIssue() { document.getElementById('add-issue-panel').classList.toggle('hidden'); }

        function rowField(rowId, field) {
            return document.querySelector(`#${rowId} [data-field="${field}"]`);
        }

        async function saveRecord(id) {
            const row = 'rec-row-' + id;
            try {
                await api(`/api/admin/records/${id}`, {
                    method: 'PATCH',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        time: rowField(row, 'time').value,
                        holderName: rowField(row, 'holderName').value,
                        holderNameEn: rowField(row, 'holderNameEn').value,
                        club: rowField(row, 'club').value,
                        holderCountry: rowField(row, 'holderCountry').value,
                        recordDate: rowField(row, 'recordDate').value,
                        isRelayLeadOff: rowField(row, 'isRelayLeadOff').checked,
                    }),
                });
                toast('Сохранено');
            } catch (e) {
                toast('Ошибка: ' + e.message, 4000);
            }
        }

        async function deleteRecord(id) {
            const ok = await adminConfirm({ title: 'Удалить рекорд?', confirmText: 'Удалить', danger: true });
            if (!ok) return;
            try {
                await api(`/api/admin/records/${id}`, { method: 'DELETE' });
                document.getElementById('rec-row-' + id)?.remove();
                toast('Рекорд удалён');
            } catch (e) {
                toast('Ошибка: ' + e.message, 4000);
            }
        }

        async function createRecord() {
            const val = idSuffix => document.getElementById('nr-' + idSuffix).value;
            try {
                await api('/api/admin/records', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        regionType: val('regionType'), regionCode: val('regionCode'), category: val('category'),
                        ageKey: val('ageKey'), gender: val('gender'), poolType: val('poolType'), style: val('style'),
                        distance: val('distance'), time: val('time'), holderName: val('holderName'),
                        holderNameEn: val('holderNameEn'),
                        club: val('club'), holderCountry: val('holderCountry'), recordDate: val('recordDate'),
                    }),
                });
                location.reload();
            } catch (e) {
                toast('Ошибка: ' + e.message, 4000);
            }
        }

        // --- Спорные записи (реестр Sys_RecordIssues) ---
        // ⚠ Рекорд при этом НЕ правится: наша копия обязана совпадать с файлом федерации.
        async function saveIssue(id) {
            const row = 'iss-row-' + id;
            try {
                await api(`/api/admin/record-issues/${id}`, {
                    method: 'PATCH',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        status: rowField(row, 'status').value,
                        reason: rowField(row, 'reason').value,
                        note: rowField(row, 'note').value,
                    }),
                });
                toast('Сохранено');
            } catch (e) {
                toast('Ошибка: ' + e.message, 4000);
            }
        }

        async function deleteIssue(id) {
            const ok = await adminConfirm({ title: 'Удалить запись реестра?', confirmText: 'Удалить', danger: true });
            if (!ok) return;
            try {
                await api(`/api/admin/record-issues/${id}`, { method: 'DELETE' });
                document.getElementById('iss-row-' + id)?.remove();
                toast('Запись удалена');
            } catch (e) {
                toast('Ошибка: ' + e.message, 4000);
            }
        }

        async function createIssue() {
            const val = idSuffix => document.getElementById('ni-' + idSuffix).value;
            try {
                await api('/api/admin/record-issues', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        regionType: val('regionType'), regionCode: val('regionCode'), category: val('category'),
                        ageKey: val('ageKey'), gender: val('gender'), poolType: val('poolType'), style: val('style'),
                        distance: val('distance'), flaggedTime: val('flaggedTime'),
                        reason: val('reason'), note: val('note'),
                    }),
                });
                location.reload();
            } catch (e) {
                toast('Ошибка: ' + e.message, 4000);
            }
        }

        // Кнопка «⚑» в таблице рекордов: заводит претензию на ЭТУ ось с ТЕКУЩИМ временем
        // (время берётся из поля строки — если его отредактировали, но не сохранили, метка
        // всё равно уйдёт на то значение, которое видит админ).
        async function flagRecord(id, axis) {
            const time = rowField('rec-row-' + id, 'time').value;
            const ok = await adminConfirm({
                title: 'Пометить запись спорной?',
                message: `${axis.style} ${axis.distance} · ${axis.poolType} · ${time}. Сам рекорд не изменится — обоснование допишете во вкладке «Спорные записи».`,
                confirmText: 'Пометить',
            });
            if (!ok) return;
            try {
                await api('/api/admin/record-issues', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ ...axis, flaggedTime: time, reason: 'manual' }),
                });
                toast('Помечено — допишите обоснование во вкладке «Спорные записи»', 4000);
            } catch (e) {
                toast('Ошибка: ' + e.message, 4000);
            }
        }

        async function saveStandard(id) {
            const row = 'std-row-' + id;
            try {
                await api(`/api/admin/normative-standards/${id}`, {
                    method: 'PATCH',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ time: rowField(row, 'time').value }),
                });
                toast('Сохранено');
            } catch (e) {
                toast('Ошибка: ' + e.message, 4000);
            }
        }

        async function deleteStandard(id) {
            const ok = await adminConfirm({ title: 'Удалить норматив?', confirmText: 'Удалить', danger: true });
            if (!ok) return;
            try {
                await api(`/api/admin/normative-standards/${id}`, { method: 'DELETE' });
                document.getElementById('std-row-' + id)?.remove();
                toast('Норматив удалён');
            } catch (e) {
                toast('Ошибка: ' + e.message, 4000);
            }
        }

        async function createStandard() {
            const val = idSuffix => document.getElementById('ns-' + idSuffix).value;
            try {
                await api('/api/admin/normative-standards', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        kind: val('kind'), country: val('country'), gender: val('gender'), poolType: val('poolType'),
                        style: val('style'), distance: val('distance'), ageKey: val('ageKey'),
                        level: val('level'), time: val('time'),
                    }),
                });
                location.reload();
            } catch (e) {
                toast('Ошибка: ' + e.message, 4000);
            }
        }
    