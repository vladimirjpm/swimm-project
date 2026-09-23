import React from 'react';
import { useRecordsFreshness } from '../../../../hooks/useRecordsFreshness';

/**
 * Подпись «Records checked: World Aquatics 3 days ago · Israel Swimming Association yesterday» — когда справочник рекордов сверяли с
 * его источниками (docs/plans/records-freshness-plan.md, U6).
 *
 * ⚠ «checked», а не «updated» (§2 плана): мировой рекорд может не биться два года, и
 * «updated 2024» читалось бы как заброшенный справочник, хотя мы сверялись вчера.
 *
 * Дата — ПО КАЖДОМУ источнику, который питает экран (`sources`), без свёртки в минимум
 * (решение Влада 21.09.2026): даже у одного издателя источники проверяются порознь, и
 * упавший World Junior не должен состарить мастерсов. Порогов и тревоги на витрине нет:
 * просто дата; «пора проверить» — дело админки. Источник, который ещё ни разу не сверяли,
 * не подписывается вовсе.
 */

/** Как источник подписан на витрине. */
const LABEL: Record<string, string> = {
  worldrecords: 'World Aquatics',
  // Прогон по странам — ОТДЕЛЬНЫЙ тип: страновые NR приезжают своим прогоном на час-два,
  // а отчёт мировых сверяется обычной проверкой. Подписывать первые датой второго значит
  // врать о свежести (замечание Влада 23.09.2026).
  'worldrecords-countries': 'World Aquatics national records',
  'wa-masters': 'World Aquatics masters',
  'wa-junior': 'World Aquatics juniors',
  'isrorg-age': 'Israel Swimming Association',
  'isrorg-masters': 'Israel Swimming Association masters',
};

/** «3 days ago» — относительная дата; полная — в title. */
export function checkedAgo(iso: string, now: number = Date.now()): string {
  const days = Math.floor((now - new Date(iso).getTime()) / 86400000);
  if (days <= 0) return 'today';
  if (days === 1) return 'yesterday';
  if (days < 30) return `${days} days ago`;
  return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
}

/** Строки подписи в порядке `sources`: только сверенные хоть раз. */
export function checkedLines(
  freshness: { source: string; checkedAt: string | null }[],
  sources: string[],
): { source: string; label: string; checkedAt: string }[] {
  return sources.flatMap((source) => {
    const f = freshness.find((x) => x.source === source);
    return f?.checkedAt ? [{ source, label: LABEL[source] ?? source, checkedAt: f.checkedAt }] : [];
  });
}

/**
 * Порядок источников в подписи — как в `RecordSources.Order` на сервере плюс прогон по
 * странам. Экспортируется, потому что по нему строят список и другие экраны.
 */
export const RECORD_SOURCE_ORDER = [
  'worldrecords', 'worldrecords-countries', 'wa-masters', 'wa-junior',
  'isrorg-age', 'isrorg-masters',
];

/** Домашняя страна: её рекорды пишут ДВОЕ — World Aquatics и федерация (И-13). */
const HOME = 'ISR';

/**
 * Какие источники питают показанные записи справочника — по региону и категории строки.
 *
 * Правила ровно те, по которым импорт раскладывает данные:
 * • `world` + open → отчёт мировых рекордов; masters/junior → свои мировые источники;
 * • `country` ИЗРАИЛЯ → федерация (возрастные, мастерские), а open у него двухозяйный (И-13);
 * • `country` любой другой страны → прогон по странам, и только он.
 *
 * Общая, потому что список рекордов показывают четыре экрана (страница пловца, стена клуба,
 * попапы, сравнение стран), и считать источники в каждом заново — это четыре шанса разойтись.
 */
export function recordSources(rows: Array<{
  regionType?: string | null;
  regionCode?: string | null;
  category?: string | null;
  /** Мировой эталон рядом со строкой (карточки «рекорд против мирового»). */
  worldKind?: 'masters' | 'junior' | null;
}>): string[] {
  const used = new Set<string>();

  for (const r of rows) {
    const region = (r.regionType ?? 'country').toLowerCase();
    const category = (r.category ?? 'open').toLowerCase();
    const code = (r.regionCode ?? HOME).toUpperCase();

    if (region === 'world') {
      if (category === 'masters') used.add('wa-masters');
      else if (category === 'junior' || category === 'age') used.add('wa-junior');
      else used.add('worldrecords');
    } else if (code === HOME) {
      if (category === 'age') used.add('isrorg-age');
      else if (category === 'masters') used.add('isrorg-masters');
      else { used.add('worldrecords'); used.add('isrorg-age'); }
    } else {
      used.add('worldrecords-countries');
    }

    if (r.worldKind) used.add(r.worldKind === 'junior' ? 'wa-junior' : 'wa-masters');
  }

  return RECORD_SOURCE_ORDER.filter((s) => used.has(s));
}

interface Props {
  /** Ключи источников, которые питают экран. */
  sources: string[];
  /** Слово перед «checked»: «Records», «World records»… */
  subject?: string;
  className?: string;
}

const UI_RecordsChecked: React.FC<Props> = ({ sources, subject = 'Records', className = '' }) => {
  const freshness = useRecordsFreshness();
  if (!freshness) return null;
  const lines = checkedLines(freshness, sources);
  if (lines.length === 0) return null;

  return (
    <div className={`records-checked ${className}`}>
      {subject} checked:{' '}
      {lines.map((l, i) => (
        <span key={l.source} title={`Last checked against ${l.label}: ${new Date(l.checkedAt).toLocaleString('en-GB')}`}>
          {i > 0 && ' · '}
          {l.label} {checkedAgo(l.checkedAt)}
        </span>
      ))}
    </div>
  );
};

export default UI_RecordsChecked;
