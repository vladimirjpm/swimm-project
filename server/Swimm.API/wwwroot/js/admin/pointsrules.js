// Скрипт страницы Pages/Admin/PointsRules/Index.cshtml. Вынесен из разметки как есть (10.10.2026):
// классический скрипт, не модуль — функции из onclick в разметке остаются глобальными.
// Tailwind-классы отсюда собирает @source в Styles/admin.css.
        // Панель «Соревнования» у правила. Состояние переживает перезагрузку: после сохранения
        // страница редиректится, чтобы пересчитались счётчики в верхней таблице.
        (function () {
            const KEY = 'admin-pointsrules-open-rules';
            const open = new Set(JSON.parse(sessionStorage.getItem(KEY) || '[]'));

            const apply = (btn, isOpen) => {
                const panel = document.getElementById('rule-panel-' + btn.dataset.key);
                if (!panel) return;
                panel.classList.toggle('hidden', !isOpen);
                btn.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
                btn.textContent = isOpen ? '▾ Соревнования' : '▸ Соревнования';
            };

            document.querySelectorAll('.rule-toggle').forEach(btn => {
                if (open.has(btn.dataset.key)) apply(btn, true);
                btn.addEventListener('click', () => {
                    const isOpen = btn.getAttribute('aria-expanded') !== 'true';
                    apply(btn, isOpen);
                    isOpen ? open.add(btn.dataset.key) : open.delete(btn.dataset.key);
                    sessionStorage.setItem(KEY, JSON.stringify([...open]));
                });
            });
        })();

        // Фильтр панели по ручной сверке: строки уже в DOM, поэтому просто прячем лишние.
        (function () {
            document.querySelectorAll('.verify-filter').forEach(group => {
                const panel = document.getElementById('rule-panel-' + group.dataset.rule);
                if (!panel) return;

                group.querySelectorAll('.verify-filter-btn').forEach(btn => {
                    btn.addEventListener('click', () => {
                        const value = btn.dataset.value;
                        group.querySelectorAll('.verify-filter-btn')
                            .forEach(b => b.classList.toggle('import-tab-active', b === btn));
                        panel.querySelectorAll('tr[data-verified]').forEach(tr => {
                            tr.classList.toggle('hidden', value !== 'all' && tr.dataset.verified !== value);
                        });
                    });
                });
            });

            // Дашборд ведёт сюда ссылкой «?filter=mismatch» (карточка «Клубные очки»).
            // Без чтения параметра ссылка обещала бы фильтр, а показывала полный список —
            // поэтому раскрываем панели правил и жмём нужную кнопку за человека.
            const wanted = new URLSearchParams(location.search).get('filter');
            if (!wanted || wanted === 'all') return;

            document.querySelectorAll('.rule-toggle').forEach(toggle => {
                const panel = document.getElementById('rule-panel-' + toggle.dataset.key);
                // Открываем только правила, у которых искомые строки есть: иначе фильтр
                // разворачивает пустые панели и прячет от человека то, за чем он пришёл.
                if (!panel || !panel.querySelector(`tr[data-verified="${wanted}"]`)) return;
                if (toggle.getAttribute('aria-expanded') !== 'true') toggle.click();
            });

            document.querySelectorAll('.verify-filter').forEach(group => {
                const btn = group.querySelector(`.verify-filter-btn[data-value="${wanted}"]`);
                if (btn) btn.click();
            });
        })();
    