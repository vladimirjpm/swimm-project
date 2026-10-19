import React from 'react';
import { routes } from '../../../utils/routes';
import { IconArrowsLeftRight, IconFlag, IconGlobe, IconRuler, IconStar } from './calendar-icons';

/*
 * Полоса «Records & standards» под календарём: четыре ссылки на рекорды и плитка Normatives.
 * Страницы нормативов нет (решение В7), поэтому Normatives — «Soon» и не кликается, как и в
 * топбаре. Пути из макета (`· /records`) — пометки дизайнера, на витрину не идут.
 */

const LINKS: { title: string; hint: string; href: string; Icon: typeof IconFlag }[] = [
  { title: 'National records', hint: 'By country', href: routes.records(), Icon: IconFlag },
  { title: 'World records', hint: 'LCM · SCM', href: routes.records({ tab: 'world' }), Icon: IconGlobe },
  { title: 'World junior', hint: 'Under 18/19', href: routes.records({ tab: 'junior' }), Icon: IconStar },
  { title: 'H2H records', hint: 'Compare two countries', href: routes.recordsCompare(), Icon: IconArrowsLeftRight },
];

// Плитка «не готово» приглушена цветом текста, а не opacity всей плитки: под opacity .7
// тег Soon в светлой терял контраст (замечание хендоффа к .tag-neutral).
function SoonTag() {
  return (
    <span className="rounded-[6px] border border-[var(--t-border)] bg-[var(--t-nested)] px-2 py-[2px] text-[11px] font-semibold text-[var(--t-text-2)]">
      Soon
    </span>
  );
}

function RecordsStandards() {
  return (
    <section className="relative pt-[38px] lg:pt-16" aria-label="Records and standards">
      <h2 className="m-0 mb-3 text-[20px] font-extrabold text-[var(--t-text)]">Records &amp; standards</h2>

      <div className="grid grid-cols-2 gap-2 lg:grid-cols-5 lg:gap-3">
        {LINKS.map(({ title, hint, href, Icon }) => (
          <a
            key={title}
            href={href}
            className="cc-focus flex min-h-[96px] flex-col gap-[10px] rounded-[14px] border border-[var(--t-border-2)] bg-[var(--t-surface)] p-4 text-inherit no-underline shadow-[var(--t-shadow)] backdrop-blur-[14px] hover:border-[var(--t-accent)]"
          >
            <Icon size={22} className="text-[var(--t-accent)]" />
            <span className="flex flex-col gap-[2px]">
              <span className="text-[14px] font-semibold text-[var(--t-text)]">{title}</span>
              {/* На телефоне — только иконка и заголовок. */}
              <span className="hidden text-[12px] text-[var(--t-text-2)] lg:block">{hint}</span>
            </span>
          </a>
        ))}

        {/* Десктоп: пятая плитка в ряду. */}
        <div
          aria-disabled="true"
          className="hidden min-h-[96px] flex-col gap-[10px] rounded-[14px] border border-dashed border-[var(--t-border)] bg-transparent p-4 lg:flex"
        >
          <span className="flex items-start justify-between">
            <IconRuler size={22} className="text-[var(--t-text-3)]" />
            <SoonTag />
          </span>
          <span className="flex flex-col gap-[2px]">
            <span className="text-[14px] font-semibold text-[var(--t-text-2)]">Normatives</span>
            <span className="text-[12px] text-[var(--t-text-2)]">Qualifying times</span>
          </span>
        </div>
      </div>

      {/* Телефон: Normatives отдельной строкой во всю ширину. */}
      <div
        aria-disabled="true"
        className="mt-2 flex h-11 items-center justify-between rounded-[14px] border border-dashed border-[var(--t-border)] bg-transparent px-4 lg:hidden"
      >
        <span className="text-[13px] text-[var(--t-text-2)]">Normatives</span>
        <SoonTag />
      </div>
    </section>
  );
}

export default RecordsStandards;
