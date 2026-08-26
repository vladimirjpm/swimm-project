import React from 'react';
import UI_SwimTime from '../../components/mix/swim-time/swim-time';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_SwimmerNameCell from '../../components/mix/swimmer-name-cell/swimmer-name-cell';
import { routes } from '../../../utils/routes';
import type { SeasonBestListItem } from '../../../hooks/useSeasonBestList';
import type { SeasonBestModules } from '../season-best-modules';

/**
 * Список среза — «кто быстрее всех» в связке стиль × дистанция × бассейн × возраст × пол.
 *
 * Строка построена по эталону со страницы спортсмена (`.deep-pb-row` / `.deep-rank-row`,
 * `swimmer-panels.tsx`), но раскладка перевёрнута: там дисциплина меняется от строки к
 * строке, а здесь она одна на весь список, и место плашки события занимает ИМЯ ПЛОВЦА.
 * Плашки события здесь нет вовсе: дисциплину называют шапка страницы и чип Event.
 *
 * Дедупа по пловцу нет: один человек законно занимает и первое место, и третье. Повтор
 * помечается номером попытки («2nd swim»), а не прячется.
 */

/** «+3.42» — отставание от лидера; у самого лидера прочерк. */
const gapLabel = (ms: number) => (ms <= 0 ? '—' : `+${(ms / 1000).toFixed(2)}`);

/** «2nd swim» / «3rd swim» — какой это по счёту заплыв пловца в списке. */
function attemptLabel(attempt: number): string {
  const suffix = attempt === 2 ? 'nd' : attempt === 3 ? 'rd' : 'th';
  return `${attempt}${suffix} swim`;
}

interface RowProps {
  row: SeasonBestListItem;
  modules: SeasonBestModules;
  columns: string;
  /**
   * Пловец, ради которого пришли (`?swimmer=`) — подсвечиваем все его строки.
   * ⚠ Это НЕ «я»: адрес говорит лишь, с чьей страницы пришла ссылка, и любой может её
   * переслать. «Я» в продукте — это primary favorite залогиненного пользователя
   * (правило: привязка из URL недоверенная), и бейджа ★ ME здесь быть не должно.
   */
  highlightSwimmerId?: number | null;
}

function SbRow({ row, modules, columns, highlightSwimmerId }: RowProps) {
  const isLinked = highlightSwimmerId != null && row.swimmer_id === highlightSwimmerId;
  const isRepeat = row.attempt > 1;
  // Имя как в протоколе — так его показывают и таблица результатов, и страница пловца.
  // Латиница доступна за флагом `latinNames`; поле `name_en` заполнено не у всех.
  const name = (modules.latinNames && row.name_en?.trim()) || row.name;
  // Клуб — тоже как в протоколе: по этому же имени `UI_ClubIcon` находит логотип
  // (файлы названы оригиналом), так что латинский вариант оставил бы строку без эмблемы.
  const club = (modules.latinNames && row.club_en?.trim()) || row.club || '';

  // Мягкая заливка по полу — та же семантика, что в таблице результатов
  // (`Helper.getGenderBgClass`). Пол здесь берётся из ответа API, а не парсится из названия
  // заплыва: на results поля пола в данных нет, а у нас оно есть и оно надёжнее.
  const tint = modules.genderTint && (row.gender === 'male' || row.gender === 'female')
    ? ` sb-row--${row.gender}`
    : '';

  return (
    <a
      className={`sb-row${tint}${isLinked ? ' sb-row--linked' : ''}${isRepeat ? ' sb-row--repeat' : ''}`}
      style={{ ['--sb-cols' as string]: columns }}
      href={routes.swimmer(row.swimmer_id)}
    >
      <span className={`sb-place${row.place === 1 ? ' sb-place--first' : ''}`}>#{row.place}</span>

      <span className="sb-swimmer">
        {/* Блок «эмблема клуба + имя + клуб» рисует общий `UI_SwimmerNameCell` — тот же, что
            в таблице результатов. Своей вёрсткой это делать нельзя: эмблему он ищет по
            ОРИГИНАЛЬНОМУ имени клуба через манифест, с фоллбеком на no-club.png, и копия
            этой логики разъехалась бы на первом же новом клубе.
            RTL решается в CSS (`unicode-bidi: plaintext`), а не атрибутом dir на обёртке:
            dir разворачивал ВСЮ ячейку, и эмблема с текстом уезжали к правому краю. */}
        <UI_SwimmerNameCell
          firstName={name}
          club={modules.clubInRow ? club : undefined}
          showClubIcon={modules.clubLogoInRow}
          clubIconSide="left"
          clubIconWidth="10"
          firstLineClassName="sb-swimmer__name"
          secondLineClassName="sb-swimmer__club"
          nameBlockClassName="min-w-0 flex-1"
          className="min-w-0"
        />
        {modules.attemptInRow && isRepeat && (
          <span className="sb-swimmer__attempt">{attemptLabel(row.attempt)}</span>
        )}
        {/* Мобайл: соревнование и дата не помещаются отдельными колонками, поэтому уходят
            второй строкой под имя — скрывать их нельзя, они и есть ответ «где и когда». */}
        {(modules.meetInRow || modules.dateInRow) && (
          <span className="sb-swimmer__meet">
            {modules.meetInRow && row.competition}
            {modules.meetInRow && modules.dateInRow && ' · '}
            {modules.dateInRow && row.date}
          </span>
        )}
      </span>

      <span className="sb-time">
        <UI_SwimTime
          time={row.time || '—'}
          quality={row.suspect_reason ? { kind: 'protocol', reason: row.suspect_reason } : null}
          marker="chip"
          chipSize="sm"
        />
        {/* Первое место в срезе — это и есть season best, носится как рекорд (правило Влада). */}
        {row.place === 1 && !row.suspect_reason && <span className="deep-chip-sb">SB</span>}
      </span>

      {modules.gapInRow && <span className="sb-gap">{gapLabel(row.gap_ms)}</span>}
      {modules.pointsInRow && <span className="sb-points">{row.points}</span>}
      {modules.meetInRow && <span className="sb-meet">{row.competition}</span>}
      {/* Дата — общим `UI_DateIcon` (вариант `row-style-1`: «16 JUL 2026»), а не голой
          строкой: формат даты в продукте живёт в одном месте. Цвет задаём через
          `fontClassName`, иначе компонент подставит свой серый, мимо токенов темы. */}
      {modules.dateInRow && (
        <UI_DateIcon styleType="row-style-1" date={row.date} fontClassName="sb-date" />
      )}
    </a>
  );
}

interface Props {
  rows: SeasonBestListItem[];
  modules: SeasonBestModules;
  highlightSwimmerId?: number | null;
}

function SbList({ rows, modules, highlightSwimmerId }: Props) {
  // Сетка собирается из включённых модулей: выключенная колонка не оставляет пустого места.
  const columns = [
    '52px',
    'minmax(0, 1.3fr)',
    '104px',
    modules.gapInRow ? '62px' : null,
    modules.pointsInRow ? '52px' : null,
    modules.meetInRow ? 'minmax(0, 1.1fr)' : null,
    modules.dateInRow ? '84px' : null,
  ].filter(Boolean).join(' ');

  return (
    <div className="sb-list">
      <div className="sb-row sb-row--head" style={{ ['--sb-cols' as string]: columns }}>
        <span>Place</span>
        <span>Swimmer</span>
        <span>Time</span>
        {modules.gapInRow && <span>Behind</span>}
        {modules.pointsInRow && <span>FINA</span>}
        {modules.meetInRow && <span>Meet</span>}
        {modules.dateInRow && <span>Date</span>}
      </div>

      {rows.map((row) => (
        <SbRow
          key={row.result_id}
          row={row}
          modules={modules}
          columns={columns}
          highlightSwimmerId={highlightSwimmerId}
        />
      ))}
    </div>
  );
}

export default SbList;
