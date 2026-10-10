// Скрипт страницы Pages/Admin/ImportHistory.cshtml. Вынесен из разметки как есть (10.10.2026):
// классический скрипт, не модуль — функции из onclick в разметке остаются глобальными.
// Tailwind-классы отсюда собирает @source в Styles/admin.css.
(async function () {
    const API = '/api/admin/import-history';

    function fmtDate(iso) {
        if (!iso) return '—';
        return new Date(iso).toLocaleString('ru-RU', {
            day: '2-digit', month: '2-digit', year: 'numeric',
            hour: '2-digit', minute: '2-digit'
        });
    }

    async function load() {
        try {
            const data = await api(API);
            renderStats(data);
            renderTable(data);
        } catch (e) {
            toast('Ошибка загрузки: ' + e.message);
        }
    }

    function renderStats(data) {
        const total = data.length;
        const approved = data.filter(d => d.approved).length;
        document.getElementById('stat-total').textContent = total;
        document.getElementById('stat-approved').textContent = approved;
        document.getElementById('stat-pending').textContent = total - approved;
    }

    function renderTable(data) {
        const tbody = document.getElementById('history-body');
        const emptyMsg = document.getElementById('empty-msg');

        if (data.length === 0) {
            tbody.innerHTML = '';
            emptyMsg.classList.remove('hidden');
            return;
        }
        emptyMsg.classList.add('hidden');

        tbody.innerHTML = data.map(h => `
            <tr class="border-b border-white/[.04] hover:bg-white/[.03]">
                <td class="py-2.5 px-3 text-[#546e7a]">${h.id}</td>
                <td class="py-2.5 px-3">${esc(h.competitionName)}</td>
                <td class="py-2.5 px-3 text-[#90a4ae]">${esc(h.competitionDate)}</td>
                <td class="py-2.5 px-3 text-[#4fc3f7] font-mono text-xs">${esc(h.importFileName)}</td>
                <td class="py-2.5 px-3 text-[#90a4ae]">${fmtDate(h.importDate)}</td>
                <td class="py-2.5 px-3">
                    ${h.approved
                        ? '<span class="inline-block py-0.5 px-2 rounded text-xs font-semibold bg-[rgba(102,187,106,0.2)] text-[#66bb6a]">✓ Подтверждён</span>'
                        : '<span class="inline-block py-0.5 px-2 rounded text-xs font-semibold bg-[rgba(239,154,154,0.2)] text-[#ef9a9a]">✗ Ожидает</span>'
                    }
                </td>
                <td class="py-2.5 px-3">
                    <button onclick="toggleApprove(${h.id}, ${!h.approved})"
                        class="py-1 px-3 rounded text-xs font-semibold cursor-pointer transition-colors
                            ${h.approved
                                ? 'bg-[rgba(239,154,154,0.15)] text-[#ef9a9a] hover:bg-[rgba(239,154,154,0.25)]'
                                : 'bg-[rgba(102,187,106,0.15)] text-[#66bb6a] hover:bg-[rgba(102,187,106,0.25)]'
                            }">
                        ${h.approved ? 'Отменить' : 'Подтвердить'}
                    </button>
                </td>
            </tr>
        `).join('');
    }

    window.toggleApprove = async function (id, approved) {
        try {
            const result = await api(`${API}/${id}/approve`, {
                method: 'PATCH',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ approved })
            });
            toast(result.message);
            await load();
        } catch (e) {
            toast('Ошибка: ' + e.message);
        }
    };

    await load();
})();
