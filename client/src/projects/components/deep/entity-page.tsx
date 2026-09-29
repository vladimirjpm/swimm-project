import React, { useRef, useState } from 'react';
import './deep-theme.css';
import AppTopbar from '../app-topbar/app-topbar';
import UI_ModeToggle from '../mix/mode-toggle/mode-toggle';
import DeepTabs from './tabs';
import DeepStickyBar, { useEntitySticky } from './sticky-bar';
import { useDeepThemeClass } from './use-deep-theme-class';
import type {
  DeepEntityPageProps, EntityCardSpec, EntityTabNav, EntityTabSpec,
} from './entity-page-types';

/**
 * Каркас страницы сущности: тема → топбар → шапка → слот → «папка» табов → панель карточек.
 *
 * До него этот скелет был написан руками на странице клуба и на странице пловца, построчно
 * одинаково: обёртка темы, ширина `main`, лесенка плашек, правило `?tab=`, корпус «папки».
 * Каркас ОДИН на все варианты сущности (клуб, группа, пловец); отличаются они набором
 * блоков, а не устройством страницы — план `docs/plans/entity-page-shell-plan.md`.
 *
 * Каркас «тупой»: данных не грузит, про сезон/клуб/группу/права не знает, DTO не нормализует.
 * Что значит карточка — решает страница, которая её положила.
 */

function DeepEntityPage<T extends string>({
  topbarActive, status, messages, hero, beforeTabs, tabsAriaLabel, tabs, defaultTabId,
  activeTabId, onTabChange, noticeClassName, toolsLabel, sticky,
}: DeepEntityPageProps<T>) {
  const themeClass = useDeepThemeClass();
  const stickyState = useEntitySticky(status === 'ready' && sticky != null);
  const fallbackId = defaultTabId ?? tabs[0]?.id;
  const folderRef = useRef<HTMLDivElement>(null);

  // Активный таб — вид, поэтому живёт в query (?tab=), а не в пути: правило routes.ts
  // «в путь только идентичность ресурса». Диплинк на таб работает сразу.
  const [ownTab, setOwnTab] = useState<T>(() => {
    const fromUrl = new URLSearchParams(window.location.search).get('tab');
    return (fromUrl as T | null) ?? fallbackId;
  });
  const tab = activeTabId ?? ownTab;

  // Набор табов — данные, и он может измениться (у сущности не оказалось прав, отсеялся
  // виртуальный случай). Активный таб поэтому ВЫВОДИМ, а не храним как истину: пропал из
  // набора — молча падаем на первый, без setState во время рендера.
  const activeTab: EntityTabSpec<T> | undefined = tabs.find((t) => t.id === tab) ?? tabs[0];

  const handleTab = (next: T) => {
    setOwnTab(next);
    const url = new URL(window.location.href);
    // Таб по умолчанию живёт БЕЗ параметра, остальные — с ним. replaceState, а не push:
    // иначе кнопка «назад» начинает ходить по табам вместо возврата на прошлую страницу.
    if (next === fallbackId) url.searchParams.delete('tab');
    else url.searchParams.set('tab', next);
    window.history.replaceState(null, '', url.toString());
    onTabChange?.(next);
  };

  /**
   * Переход, которым пользуются карточки дайджеста («All 20 records →»). Отличается от клика
   * по табу ровно одним: поднимает страницу к верху «папки». Клик по самому табу не
   * прокручивает — там человек и так смотрит на полосу плиток.
   */
  const nav: EntityTabNav<T> = {
    go: (next) => {
      handleTab(next);
      const top = folderRef.current?.getBoundingClientRect().top;
      // Верх папки встаёт под липкую зону — топбар и (если она есть) полосу сущности, которая
      // к этому моменту уже выедет: иначе ряд табов уходил бы под них.
      const chrome = stickyState.top + (sticky ? 52 : 0) + 8;
      if (top != null) window.scrollTo({ top: window.scrollY + top - chrome, behavior: 'smooth' });
    },
  };

  // Инструменты управляющего на телефоне — закреплённая панель у низа экрана; странице
  // тогда нужен отступ снизу, чтобы последняя карточка не уходила под панель.
  const hasTools = tabs.some((t) => t.pinned);

  return (
    <div
      className={themeClass}
      style={{
        background: 'var(--deep-page-bg)',
        minHeight: '100vh',
        // Куда липнет ряд табов: под топбар или под выехавшую полосу сущности.
        ['--deep-sticky-top' as string]: `${stickyState.stickyTop}px`,
      }}
    >
      <AppTopbar active={topbarActive} />
      {status === 'ready' && sticky && (
        <DeepStickyBar bar={sticky} shown={stickyState.shown} top={stickyState.top} />
      )}

      {/* На телефоне отступов у края нет — ни по бокам, ни сверху: шапка встаёт вплотную
          к топбару, блоки от края до края (`.deep-entity-main` в deep-theme.css). Места и так
          мало, а своих паддингов у карточек хватает. Строка кнопки темы пустая (кнопка
          `fixed`), поэтому и её отступ на телефоне снят. */}
      <main className={`deep-entity-main${hasTools ? ' deep-entity-main--dock' : ''} mx-auto max-w-[1180px] px-0 pt-0 pb-6 sm:px-4 sm:pt-6`} style={{ color: 'var(--deep-text)' }}>
        <div className="flex justify-end sm:mb-4">
          <UI_ModeToggle />
        </div>

        {status === 'loading' && (
          <DeepEntityNotice className={noticeClassName}>{messages?.loading ?? 'Loading…'}</DeepEntityNotice>
        )}
        {status === 'notfound' && (
          <DeepEntityNotice className={noticeClassName}>{messages?.notfound ?? 'Not found'}</DeepEntityNotice>
        )}
        {status === 'error' && (
          <DeepEntityNotice className={noticeClassName}>{messages?.error ?? 'Could not load this page'}</DeepEntityNotice>
        )}

        {status === 'ready' && (
          <>
            {typeof hero === 'function' ? hero(nav) : hero}
            {beforeTabs}

            {/* «Папка» (TABS.md 3a folder-tab): плитки и панель контента — один корпус,
                активная плитка срастается с панелью. Поэтому они в общей обёртке, а не
                двумя блоками с отступом между ними; вставлять что-либо между DeepTabs и
                панелью нельзя — разорвётся стык. */}
            {activeTab != null && (
              <div className="deep-folder mb-4" ref={folderRef}>
                <DeepTabs
                  ariaLabel={tabsAriaLabel}
                  active={activeTab.id}
                  onSelect={handleTab}
                  toolsLabel={toolsLabel}
                  tabs={tabs.map(({
                    id, icon, label, shortLabel, sub, editable, locked, pinned, badge,
                  }) => ({
                    id, icon, label, shortLabel, sub, editable: editable || pinned, locked, pinned, badge,
                  }))}
                />

                <div
                  className={[
                    'deep-tabs-panel',
                    activeTab.editable || activeTab.pinned ? 'deep-tabs-panel--edit' : '',
                    activeTab.pinned ? 'deep-tabs-panel--tool' : '',
                  ].filter(Boolean).join(' ')}
                >
                  {activeTab.locked
                    ? activeTab.lockNotice
                    : (
                      <>
                        {/* Фиолетовый — только вместе с подписью (правило хендоффа §3). */}
                        {(activeTab.editable || activeTab.pinned) && (
                          <div className="deep-panel-row mb-3 flex justify-end">
                            <span className="deep-edit-chip">✎ You can edit</span>
                          </div>
                        )}
                        <EntityPanel cards={activeTab.cards(nav)} />
                      </>
                    )}
                </div>
              </div>
            )}
          </>
        )}
      </main>
    </div>
  );
}

/**
 * Раскладка панели: соседние `half` встают парой, `full` идёт во всю ширину и разрывает ряд.
 *
 * Порог 960px, а не стандартный lg (1024) — выстрадан на паре «грид клуба + таблица зачёта»:
 * строке грида нужно ~525px (кружок группы + название + две линии чемпионатов по 10 сегментов),
 * таблице ~380px; вдвоём они помещаются уже с 960. Уже — одна колонка.
 *
 * `items-start` обязателен: без него половинки растягиваются до высоты соседа.
 */
function EntityPanel({ cards }: { cards: EntityCardSpec[] }) {
  return (
    <div className="grid grid-cols-1 items-start gap-4 min-[960px]:grid-cols-2">
      {cards.map((card, i) => (
        <div
          key={card.id}
          className={[
            'min-w-0',
            card.span === 'half' ? '' : 'min-[960px]:col-span-2',
            // Карточки несут собственный `mb-4` — вместе с gap он и даёт привычный ритм
            // между рядами. У последней его надо снять, иначе панель получает лишний отступ
            // снизу: раньше это делало правило `.deep-tabs-panel > *:last-child`, но теперь
            // прямой ребёнок панели — сетка, а не карточка, и до карточки оно не достаёт.
            i === cards.length - 1 ? '[&>*]:mb-0' : '',
          ].filter(Boolean).join(' ')}
        >
          {card.render()}
        </div>
      ))}
    </div>
  );
}

/**
 * Плашка состояния; текст задаёт страница. Оболочка по умолчанию — `.deep-card` (клуб);
 * своя чужая (`.deep-notice` у пловца — пунктирная рамка) подставляется через `className`,
 * и тогда цвет тоже её, а не наш инлайновый.
 */
function DeepEntityNotice({ children, className }: { children: React.ReactNode; className?: string }) {
  return (
    <div
      className={className ?? 'deep-card text-center text-[14px] font-extrabold'}
      style={className ? undefined : { color: 'var(--deep-text-mute)' }}
    >
      {children}
    </div>
  );
}

export default DeepEntityPage;
export { DeepEntityNotice };
