import React from 'react';

/**
 * Строка переноса «Move <name> to [1][2][3] [Unassigned] [Not today] [Lead]» (Т7, Р6 п.1).
 * Появляется в карточке дорожки / корзины выбранного пловца; кнопки — ДРУГИЕ места, «Lead» —
 * только если он в дорожке не первый. Выбор снимает повторный тап по плашке или Esc (в редакторе).
 */
function LaneMoveRow({
  name, laneNos, canUnassign, canOut, canLead, onLane, onUnassign, onOut, onLead,
}: {
  name: string;
  /** Номера ДРУГИХ видимых дорожек. */
  laneNos: number[];
  canUnassign: boolean;
  canOut: boolean;
  canLead: boolean;
  onLane: (no: number) => void;
  onUnassign: () => void;
  onOut: () => void;
  onLead: () => void;
}) {
  const btn = 'h-[36px] flex-none cursor-pointer rounded-[8px] border bg-transparent text-[12px] font-extrabold';
  const style = { borderColor: 'var(--deep-ow-border)', color: 'var(--deep-ow)' };
  return (
    <div
      role="group"
      aria-label={`Move ${name}`}
      className="flex flex-wrap items-center gap-1.5 rounded-[10px] border"
      style={{ padding: '6px 8px', background: 'var(--deep-ow-soft)', borderColor: 'var(--deep-ow-border)' }}
    >
      <span className="mr-auto text-[11px] font-extrabold" style={{ color: 'var(--deep-ow)' }}>
        Move <bdi>{name}</bdi> to
      </span>
      {laneNos.map((no) => (
        <button key={no} type="button" className={`${btn} hp-mono w-[36px]`} style={style} onClick={() => onLane(no)} aria-label={`Lane ${no}`}>
          {no}
        </button>
      ))}
      {canUnassign && <button type="button" className={`${btn} px-2.5`} style={style} onClick={onUnassign}>Unassigned</button>}
      {canOut && <button type="button" className={`${btn} px-2.5`} style={style} onClick={onOut}>Not today</button>}
      {canLead && <button type="button" className={`${btn} px-2.5`} style={style} onClick={onLead}>Lead</button>}
    </div>
  );
}

export default LaneMoveRow;
