// Скрипт страницы Pages/Admin/Settings.cshtml. Вынесен из разметки как есть (10.10.2026):
// классический скрипт, не модуль — функции из onclick в разметке остаются глобальными.
// Tailwind-классы отсюда собирает @source в Styles/admin.css.
    // ── Debug details: двухуровневый выключатель (DebugDetails × галочка опции) ──
    // «effective» считает сервер — на клиенте правило не дублируем, иначе разъедется.
    async function loadDebugOptions() {
        try {
            renderDebugOptions(await api('/api/admin/debug-options'));
        } catch (e) {
            document.getElementById('debug-options-list').textContent =
                'Не удалось загрузить опции: ' + e.message;
        }
    }

    function renderDebugOptions(data) {
        const list = document.getElementById('debug-options-list');
        const badge = document.getElementById('debug-master-badge');

        badge.textContent = data.masterEnabled ? 'DebugDetails: on' : 'DebugDetails: off';
        badge.className = 'text-[0.7rem] py-0.5 px-1.5 rounded-sm font-semibold uppercase '
            + (data.masterEnabled
                ? 'bg-admin-accent/15 text-admin-accent'
                : 'bg-white/5 text-admin-text-muted');

        if (!data.options || !data.options.length) {
            list.textContent = 'Опций пока нет.';
            return;
        }

        list.innerHTML = data.options.map(o => `
            <label class="flex items-start gap-2.5 py-2 border-t border-white/[.05] cursor-pointer">
                <input type="checkbox" class="mt-1 debug-option-toggle" data-key="${esc(o.key)}"
                       ${o.enabled ? 'checked' : ''}>
                <span>
                    <span class="text-admin-text font-semibold">${esc(o.title)}</span>
                    ${o.effective
                        ? '<span class="ml-2 text-[11px] text-admin-accent">действует</span>'
                        : (o.enabled
                            ? '<span class="ml-2 text-[11px] text-admin-warning">включена, но DebugDetails выключен</span>'
                            : '')}
                    <span class="block text-[11px] text-[#90a4ae] leading-snug">${esc(o.description)}</span>
                    <span class="block text-[10px] text-[#546e7a]">${esc(o.key)}</span>
                </span>
            </label>`).join('');

        list.querySelectorAll('.debug-option-toggle').forEach(cb => {
            cb.addEventListener('change', () => saveDebugOption(cb.dataset.key, cb.checked));
        });
    }

    async function saveDebugOption(key, enabled) {
        try {
            const data = await api('/api/admin/debug-options/' + encodeURIComponent(key), {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ enabled })
            });
            renderDebugOptions(data);
            toast(`${key} = ${enabled}`);
        } catch (e) {
            toast(`Error: ${e.message}`);
            loadDebugOptions();
        }
    }

(async function () {
    const API = '/api/admin/settings';
    let settings = [];
    let activeScope = 'all';

    function renderValueControl(s) {
        const id = `val-${s.key}`;
        const okId = `ok-${s.key}`;

        if (s.dataType === 'bool') {
            const checked = s.value.toLowerCase() === 'true' ? 'checked' : '';
            const isDanger = s.key === 'MaintenanceMode';
            const cls = isDanger ? 'toggle-switch danger' : 'toggle-switch';
            const label = s.value.toLowerCase() === 'true'
                ? (isDanger ? '<span class="text-[0.78rem] text-[#ef5350] ml-0.5">ON</span>' : '<span class="text-[0.78rem] text-[#90a4ae] ml-0.5">ON</span>')
                : '<span class="text-[0.78rem] text-[#90a4ae] ml-0.5">OFF</span>';
            return `<div class="flex items-center gap-1.5 flex-wrap">
                <label class="${cls}">
                    <input type="checkbox" ${checked} onchange="saveBool('${esc(s.key)}', this.checked)">
                    <span class="toggle-slider"></span>
                </label>
                ${label}
                <span class="saved-icon text-[#66bb6a] text-[0.85rem] opacity-0 transition-opacity duration-300" id="${okId}">&#10003;</span>
            </div>`;
        }

        const widthCls = s.dataType === 'string' ? 'w-[200px]' : 'w-[140px]';
        return `<div class="flex items-center gap-1.5 flex-wrap">
            <input class="bg-black/30 border border-[#546e7a] rounded py-1 px-2 text-gray-300 text-[0.82rem] font-[inherit] ${widthCls} focus:outline-none focus:border-[#4fc3f7]" id="${id}" value="${esc(s.value)}"
                   onkeydown="if(event.key==='Enter')saveField('${esc(s.key)}')">
            <button class="bg-[#66bb6a] border-none text-white py-1 px-3 rounded cursor-pointer text-[0.78rem] font-[inherit] transition-opacity hover:opacity-85" onclick="saveField('${esc(s.key)}')">Save</button>
            <span class="saved-icon text-[#66bb6a] text-[0.85rem] opacity-0 transition-opacity duration-300" id="${okId}">&#10003;</span>
        </div>`;
    }

    const scopeClasses = {
        admin: 'bg-[rgba(79,195,247,0.15)] text-[#4fc3f7]',
        livesite: 'bg-[rgba(129,199,132,0.15)] text-[#81c784]',
        both: 'bg-[rgba(206,147,216,0.15)] text-[#ce93d8]'
    };

    function render() {
        const tbody = document.getElementById('settings-body');
        const filtered = activeScope === 'all'
            ? settings
            : settings.filter(s => s.scope === activeScope);

        if (!filtered.length) {
            tbody.innerHTML = '<tr><td colspan="5" class="text-center text-[#546e7a] py-8">No settings for this scope</td></tr>';
            return;
        }

        tbody.innerHTML = filtered.map(s => `
            <tr class="hover:bg-white/[.03]">
                <td class="py-2.5 px-2.5 text-[0.85rem] border-b border-white/[.05] align-top"><span class="text-[#ffd54f] font-semibold">${esc(s.key)}</span></td>
                <td class="py-2.5 px-2.5 text-[0.85rem] border-b border-white/[.05] align-top"><span class="text-[0.7rem] py-0.5 px-1.5 rounded-sm font-semibold uppercase whitespace-nowrap ${scopeClasses[s.scope] || ''}">${esc(s.scope)}</span></td>
                <td class="py-2.5 px-2.5 text-[0.85rem] border-b border-white/[.05] align-top"><span class="text-[0.7rem] text-[#546e7a] italic">${esc(s.dataType)}</span></td>
                <td class="py-2.5 px-2.5 text-[0.85rem] border-b border-white/[.05] align-top">${renderValueControl(s)}</td>
                <td class="py-2.5 px-2.5 text-[0.85rem] border-b border-white/[.05] align-top"><span class="text-[#90a4ae] text-[0.78rem] leading-snug">${esc(s.description)}</span></td>
            </tr>
        `).join('');
    }

    function flashOk(key) {
        const el = document.getElementById('ok-' + key);
        if (!el) return;
        el.classList.add('show');
        setTimeout(() => el.classList.remove('show'), 2000);
    }

    window.saveField = async function (key) {
        const input = document.getElementById('val-' + key);
        if (!input) return;
        const newValue = input.value;
        try {
            await api(`${API}/${encodeURIComponent(key)}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ value: newValue })
            });
            const s = settings.find(x => x.key === key);
            if (s) s.value = newValue;
            flashOk(key);
            toast(`${key} = ${newValue}`);
        } catch (e) {
            toast(`Error: ${e.message}`);
        }
    };

    window.saveBool = async function (key, checked) {
        try {
            const newValue = String(checked);
            await api(`${API}/${encodeURIComponent(key)}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ value: newValue })
            });
            const s = settings.find(x => x.key === key);
            if (s) s.value = newValue;
            flashOk(key);
            toast(`${key} = ${checked}`);
            render();
        } catch (e) {
            toast(`Error: ${e.message}`);
        }
    };

    document.getElementById('btn-invalidate-cache').addEventListener('click', async (e) => {
        const btn = e.currentTarget;
        btn.disabled = true;
        try {
            await api('/api/admin/cache/invalidate', { method: 'POST' });
            toast('Кэш сброшен — агрегаты пересчитаются при следующем запросе', 3500);
        } catch (err) {
            toast('Ошибка: ' + err.message, 5000);
        } finally {
            btn.disabled = false;
        }
    });

    document.getElementById('scope-tabs').addEventListener('click', (e) => {
        const btn = e.target.closest('.scope-tab');
        if (!btn) return;
        document.querySelectorAll('.scope-tab').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        activeScope = btn.dataset.scope;
        render();
    });

    // Опции отладки грузим независимо от таблицы настроек: упадёт одно — второе выживет.
    loadDebugOptions();

    try {
        settings = await api(API);
        render();
    } catch (e) {
        console.error('Settings load failed', e);
        document.getElementById('settings-body').innerHTML =
            '<tr><td colspan="5" style="text-align:center;color:#ef5350;padding:2rem">Failed to load settings. Check Admin access.</td></tr>';
    }
})();
