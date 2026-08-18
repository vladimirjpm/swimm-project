import React from 'react';
import { useAppSelector } from '../../../store/store';
import type { ClubPointsRule } from '../../results-main-project/components/competition-header/types';

/**
 * Попап «как начисляются клубные очки»: шкала мест того правила (или правил), по которому
 * реально посчитан зачёт этого соревнования. Данные приходят в overview
 * (`club_points_rules`) — клиент ничего не пересчитывает и не подбирает правило сам.
 */
interface PopupData {
  rules: ClubPointsRule[];
  /** Чем наши очки расходятся с официальными — приходит только у соревнований с бейджем
   *  «Differs from official». null — расхождения нет либо объяснение не записано. */
  mismatchNote?: string | null;
}

const SCOPE_LABELS: Record<string, string> = {
  all: 'all competitions',
  masters: 'masters only',
  'non-masters': 'non-masters only',
};

function RuleBlock({ rule }: { rule: ClubPointsRule }) {
  const places = rule.points_by_place;

  return (
    <div className="mb-5 last:mb-0">
      <div className="mb-1 flex flex-wrap items-baseline gap-2">
          <span className="font-mono text-[13px] font-bold">{rule.version}</span>
          <span className="text-[12px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
          {SCOPE_LABELS[rule.scope] ?? rule.scope} · in force since {rule.effective_from}
        </span>
      </div>

      {rule.description && (
        <p className="mb-3 text-[13px]" style={{ color: 'var(--theme-mode-text-secondary)' }}>
          {rule.description}
        </p>
      )}

      {places.length > 0 ? (
        <div className="flex flex-wrap gap-1.5">
          {places.map((p) => (
            <span
              key={p.place}
              className="flex items-baseline gap-1.5 rounded-lg px-2.5 py-1.5 text-[13px]"
              style={{ background: 'var(--theme-mode-surface-2, rgba(127,127,127,.08))' }}
            >
              <span className="font-semibold" style={{ color: 'var(--theme-mode-text-muted)' }}>
                {p.place}
              </span>
              <span className="font-bold tabular-nums">{p.points}</span>
            </span>
          ))}
        </div>
      ) : (
        <p className="text-[13px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
          No place scale — every result scores {rule.default_points} points.
        </p>
      )}

      <ul className="mt-3 flex flex-col gap-1 text-[12.5px]" style={{ color: 'var(--theme-mode-text-secondary)' }}>
        <li>Relay results score ×{rule.relay_multiplier}.</li>
        {rule.max_scoring_place != null && <li>Only places up to {rule.max_scoring_place} score.</li>}
        {places.length > 0 && <li>Any other place scores {rule.default_points}.</li>}
        <li>Disqualified swims and unofficial times score nothing.</li>
      </ul>
    </div>
  );
}

const PopupContentClubPoints: React.FC = () => {
  const popUpObj = useAppSelector((state) => state.popUpObj) as PopupData | null;
  const rules = popUpObj?.rules ?? [];
  const mismatchNote = popUpObj?.mismatchNote;

  return (
    <div>
      <div className="mb-1 text-lg font-bold">How club points are scored</div>
      <p className="mb-4 text-[13px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
        Points per place, as defined by the rule this meet is scored with.
      </p>

      {/* Расхождение с официальной таблицей. Стоит ПЕРЕД шкалой: читатель пришёл сюда по
          бейджу «Differs from official» и ищет объяснение, а не список мест. */}
      {mismatchNote && (
        <div
          className="mb-4 rounded-lg p-3 text-[13px]"
          style={{
            background: 'color-mix(in srgb, #dc2626 10%, transparent)',
            border: '1px solid color-mix(in srgb, #dc2626 30%, transparent)',
          }}
        >
          <div className="mb-1 font-bold" style={{ color: '#dc2626' }}>
            The official standings were scored incorrectly
          </div>
          <p style={{ color: 'var(--theme-mode-text-secondary)' }}>{mismatchNote}</p>
          <p className="mt-2" style={{ color: 'var(--theme-mode-text-muted)' }}>
            The scale below is the one from the meet regulations — the points on this page follow it.
          </p>
        </div>
      )}

      {rules.length === 0 ? (
        <p className="text-[13px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
          No scoring rule applies to this meet — club points are not awarded.
        </p>
      ) : (
        // Несколько правил бывает в сезонной выборке: у соревнований разные регламенты.
        rules.map((r) => <RuleBlock key={r.version} rule={r} />)
      )}
    </div>
  );
};

export default PopupContentClubPoints;
