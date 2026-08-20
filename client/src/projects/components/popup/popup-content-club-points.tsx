import React from 'react';
import { useAppSelector } from '../../../store/store';
import type {
  ClubPointsRule, CompetitionMismatchNote,
} from '../../results-main-project/components/competition-header/types';
import { UI_LangTabs, useInfoLang, type InfoLang } from '../info-popup/info-popup';

/**
 * Попап «как начисляются клубные очки»: шкала мест того правила (или правил), по которому
 * реально посчитан зачёт этого соревнования. Данные приходят в overview
 * (`club_points_rules`) — клиент ничего не пересчитывает и не подбирает правило сам.
 */
interface PopupData {
  rules: ClubPointsRule[];
  /** Чем наши очки расходятся с официальными — приходит только у соревнований с бейджем
   *  «Differs from official». null — расхождения нет либо объяснение не записано. */
  mismatchNote?: CompetitionMismatchNote | null;
}

/** Подписи таблички расхождения — статика интерфейса, поэтому в коде, а не в БД. */
const DIFF_LABELS: Record<InfoLang, {
  title: string; place: string; subject: string; expected: string; actual: string;
}> = {
  en: { title: 'The official standings were scored incorrectly',
        place: 'Place', subject: 'Went to', expected: 'Per regulations', actual: 'Officially awarded' },
  ru: { title: 'Официальный зачёт посчитан неверно',
        place: 'Место', subject: 'Кому', expected: 'По регламенту', actual: 'Начислено официально' },
  he: { title: 'הדירוג הרשמי חושב באופן שגוי',
        place: 'מקום', subject: 'למי', expected: 'לפי התקנון', actual: 'הוענק רשמית' },
};

const FOLLOW_UP: Record<InfoLang, string> = {
  en: 'The scale below is the one from the meet regulations — the points on this page follow it.',
  ru: 'Шкала ниже — из регламента соревнования, очки на этой странице считаются по ней.',
  he: 'הסולם למטה הוא זה שבתקנון התחרות — הנקודות בעמוד זה מחושבות לפיו.',
};

/** Подпись ссылки на регламент — статика интерфейса, как и заголовки таблички. */
const SOURCE_LINK: Record<InfoLang, string> = {
  en: 'Read the meet regulations',
  ru: 'Открыть регламент соревнования',
  he: 'לצפייה בתקנון התחרות',
};

/**
 * Ссылка идёт в href, поэтому схему проверяем ещё раз здесь: сервер уже фильтрует, но
 * «javascript:» в href — слишком дешёвый XSS, чтобы полагаться на одну проверку.
 */
function safeHttpUrl(url?: string | null): string | null {
  if (!url) return null;
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? url : null;
  } catch {
    return null;
  }
}

/**
 * Расхождение с официальной таблицей. Стоит ПЕРЕД шкалой: читатель пришёл сюда по бейджу
 * «Differs from official» и ищет объяснение, а не список мест.
 *
 * Языки переключаются теми же вкладками и тем же запомненным выбором, что и остальные
 * объяснялки сайта (`useInfoLang`). Табличка расхождения приходит ДАННЫМИ и рисуется здесь —
 * так она живёт в теме сайта и переживает узкий экран, чего готовая вёрстка из базы не умеет.
 */
function MismatchBlock({ note }: { note: CompetitionMismatchNote }) {
  const [lang, pickLang] = useInfoLang();
  const texts = note.texts ?? {};
  // Языка может не быть — тогда показываем любой заполненный, но вкладку не подсвечиваем ложно.
  const shown = texts[lang] ?? texts.en ?? Object.values(texts)[0] ?? '';
  const labels = DIFF_LABELS[lang];
  const isRtl = lang === 'he';
  const sourceUrl = safeHttpUrl(note.source_url);
  const hasSubjects = note.scale_diff?.some((row) => !!row.subject) ?? false;

  return (
    <div
      className="mb-4 rounded-lg p-3"
      style={{
        background: 'color-mix(in srgb, #dc2626 10%, transparent)',
        border: '1px solid color-mix(in srgb, #dc2626 30%, transparent)',
      }}
    >
      <UI_LangTabs lang={lang} onPick={pickLang} available={texts} />

      <div dir={isRtl ? 'rtl' : 'ltr'} style={{ textAlign: isRtl ? 'right' : 'left' }}>
        <div className="mb-1 text-[13px] font-bold" style={{ color: '#dc2626' }}>{labels.title}</div>
        <p className="whitespace-pre-line text-[13px]" style={{ color: 'var(--theme-mode-text-secondary)' }}>
          {shown}
        </p>

        {note.scale_diff?.length > 0 && (
          <div className="mt-3 overflow-x-auto">
            {/* Подпись обязана стоять НАД таблицей: столбик мест без названия заплыва
                читается как «шкала соревнования», а это разбор одной дистанции. */}
            {note.scale_diff_caption && (
              <div className="mb-1 text-[12px] font-semibold" style={{ color: 'var(--theme-mode-text-secondary)' }}>
                {note.scale_diff_caption}
              </div>
            )}
            <table className="text-[12.5px] tabular-nums">
              <thead>
                <tr style={{ color: 'var(--theme-mode-text-muted)' }}>
                  <th className="px-2 py-1 text-start font-semibold">{labels.place}</th>
                  {hasSubjects && <th className="px-2 py-1 text-start font-semibold">{labels.subject}</th>}
                  <th className="px-2 py-1 text-start font-semibold">{labels.expected}</th>
                  <th className="px-2 py-1 text-start font-semibold">{labels.actual}</th>
                </tr>
              </thead>
              <tbody>
                {note.scale_diff.map((row) => (
                  <tr key={row.place}>
                    <td className="px-2 py-1 font-semibold">{row.place}</td>
                    {/* Колонку показываем только когда «кому» заполнено хоть где-то: у старых
                        заметок его нет, и пустой столбец выглядел бы как потерянные данные. */}
                    {hasSubjects && (
                      <td className="px-2 py-1 whitespace-nowrap">{row.subject ?? '—'}</td>
                    )}
                    <td className="px-2 py-1">{row.expected}</td>
                    <td className="px-2 py-1 font-bold" style={{ color: '#dc2626' }}>{row.actual}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <p className="mt-2 text-[12px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
          {FOLLOW_UP[lang]}
        </p>

        {/* Ссылка на регламент — то, чем объяснение доказывается: читатель может проверить сам. */}
        {sourceUrl && (
          <a
            href={sourceUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="mt-2 inline-block text-[12px] font-semibold underline"
            style={{ color: '#dc2626' }}
          >
            {SOURCE_LINK[lang]} ↗
          </a>
        )}
      </div>
    </div>
  );
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

      {mismatchNote && <MismatchBlock note={mismatchNote} />}

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
