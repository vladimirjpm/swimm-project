import React from 'react';
import UI_ClubIcon from '../../../../components/mix/club-icon/club-icon';
import type { FilterZoneClub, FilterZoneMode, FilterZonePerson } from './types';

/**
 * Строка 3 зоны — люди. В All это избранные (тап по пловцу открывает его карточку, по клубу
 * — переводит в My plan: у клуба в программе нет ответа «во сколько плывёт»). В My plan —
 * состав с крестиками и кнопки «Add swimmer / club» и «Edit».
 */
export default function PeopleRow({
  mode, swimmers, clubs, onPickSwimmer, onRemoveSwimmer, onRemoveClub, onModeChange, onEditPlan,
}: {
  mode: FilterZoneMode;
  swimmers: FilterZonePerson[];
  clubs: FilterZoneClub[];
  onPickSwimmer: (id: number) => void;
  onRemoveSwimmer: (id: number) => void;
  onRemoveClub: (id: number) => void;
  onModeChange: (mode: FilterZoneMode) => void;
  /** «Add swimmer / club» и «Edit» — оба ведут в пикер состава. */
  onEditPlan: () => void;
}) {
  const plan = mode === 'plan';

  const label = (
    <span
      className="flex-none text-[10px] font-black uppercase tracking-wide"
      style={{ color: plan ? 'var(--theme-personal-accent)' : 'var(--deep-text-mute)' }}
    >
      {plan ? '⭐ Plan' : '❤️ Favorites'}
    </span>
  );

  const chips = (
    <>
      {swimmers.map((p) => (
        <PersonChip
          key={`s${p.id}`}
          name={p.name}
          gold={plan || !!p.favorite}
          removable={plan}
          onClick={() => (plan ? onRemoveSwimmer(p.id) : onPickSwimmer(p.id))}
        />
      ))}

      {/* Клуб-чип одинаков в обеих вкладках (правило хендоффа). */}
      {clubs.map((c) => (
        <ClubChip
          key={`c${c.id}`}
          club={c}
          removable={plan}
          onClick={() => (plan ? onRemoveClub(c.id) : onModeChange('plan'))}
        />
      ))}

    </>
  );

  // pb-2 у прокручиваемой строки — зазор под горизонтальной полосой прокрутки: без него она
  // ложилась вплотную к чипам и резала их низ (замечание Влада 22.10.2026). На широком
  // экране строка переносится и полосы нет — отступ там снимаем.
  if (!plan) {
    return (
      <div className="-mx-3 mt-2 flex items-center gap-1.5 overflow-x-auto px-3 pb-2 sm:mx-0 sm:flex-wrap sm:px-0 sm:pb-0">
        {label}
        {chips}
      </div>
    );
  }

  // My plan на телефоне — ДВЕ строки (решение Влада 22.10.2026): сверху «⭐ Plan» и кнопки
  // правки состава, снизу сами пловцы и клубы с прокруткой. В одной строке кнопки уезжали за
  // правый край вслед за чипами, и до «Edit» надо было докручивать. От 640px — одна строка,
  // как была: `sm:contents` растворяет обе обёртки, и порядок задают `sm:order-*`.
  return (
    <div className="mt-2 flex flex-col gap-1.5 sm:flex-row sm:flex-wrap sm:items-center">
      <div className="flex items-center gap-1.5 sm:contents">
        {label}
        <button
          type="button"
          onClick={onEditPlan}
          className="ml-auto flex-none whitespace-nowrap rounded-full px-3 py-[6px] text-[12.5px] font-extrabold sm:order-2 sm:ml-0"
          style={{
            border: '1px dashed var(--theme-personal-border)',
            color: 'var(--theme-personal-accent)',
            background: 'transparent',
          }}
        >
          ＋ Add swimmer / club
        </button>
        <button
          type="button"
          onClick={onEditPlan}
          className="flex-none whitespace-nowrap rounded-full px-3 py-[6px] text-[12.5px] font-black sm:order-2 sm:ml-auto"
          style={{ background: 'var(--deep-accent-hover)', color: 'var(--deep-accent-ink)' }}
        >
          Edit ▾
        </button>
      </div>
      <div className="-mx-3 flex items-center gap-1.5 overflow-x-auto px-3 pb-2 sm:contents">
        {chips}
      </div>
    </div>
  );
}

function PersonChip({ name, gold, removable, onClick }: {
  name: string; gold: boolean; removable: boolean; onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={removable ? 'Remove from my plan' : undefined}
      className="flex flex-none items-center gap-1.5 whitespace-nowrap rounded-full border px-3 py-[6px] text-[12.5px] font-extrabold"
      style={{
        borderColor: gold ? 'var(--theme-personal-border)' : 'var(--deep-card-border)',
        background: gold ? 'var(--theme-personal-badge-bg)' : 'transparent',
        color: gold ? 'var(--theme-personal-accent)' : 'var(--deep-text-mute)',
      }}
    >
      {removable && <span className="opacity-60" aria-hidden>✕</span>}
      <span dir="auto">{name}</span>
    </button>
  );
}

/**
 * Клуб: эмблема из манифеста (`UI_ClubIcon`, свой фоллбек «no-club»), имя и суффикс «club» —
 * иначе чип клуба не отличить от чипа пловца. В макете на его месте кружок с инициалами:
 * у макета эмблем не было, у нас они есть.
 */
function ClubChip({ club, removable, onClick }: {
  club: FilterZoneClub; removable: boolean; onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={removable ? 'Remove from my plan' : undefined}
      className="flex flex-none items-center gap-1.5 whitespace-nowrap rounded-full border py-[5px] pl-1.5 pr-3 text-[12.5px] font-extrabold"
      style={{
        borderColor: 'var(--deep-accent-border)',
        background: 'var(--deep-accent-chip)',
        color: 'var(--deep-accent)',
      }}
    >
      {removable && <span className="ml-1 opacity-60" aria-hidden>✕</span>}
      <span className="flex h-5 w-5 flex-none items-center justify-center overflow-hidden rounded-full">
        <UI_ClubIcon clubName={club.name} iconWidth="20px" />
      </span>
      <span dir="auto">{club.name}</span>
      <span className="text-[10.5px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>club</span>
    </button>
  );
}
