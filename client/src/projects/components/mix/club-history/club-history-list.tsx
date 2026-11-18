import React from 'react';
import './club-history.css';
import UI_ClubIcon from '../club-icon/club-icon';
import UI_SeasonIcon, { isSeason } from '../season-icon/season-icon';
import { routes } from '../../../../utils/routes';
import { clubHistoryDate, clubSeasonName, clubSeasonThenName, type ClubSeason } from './club-history.types';

/** Подпись значка части сезона: зима — короткая вода, лето — длинная. */
const COURSE_LABEL: Record<string, string> = {
  winter: 'Winter · short course (25m)',
  summer: 'Summer · long course (50m)',
};

/**
 * Список «в каком сезоне за какой клуб» (решение К5 плана swimmer-club-change): новые сезоны
 * сверху, сезон с двумя клубами — двумя строками (свежий клуб первым). Подписи английские,
 * название клуба — ивритом, английское только фоллбеком. ❄️/☀️ — где он плыл за клуб:
 * в короткой воде (зима) и/или длинной (лето), общий `UI_SeasonIcon`.
 */
const UI_ClubHistoryList: React.FC<{ history: ClubSeason[] }> = ({ history }) => (
  <ol className="club-history">
    {history.map((row, i) => {
      // Метка сезона — только у первой строки сезона: вторая строка того же сезона читается
      // как продолжение, а не как ещё один год.
      const first = i === 0 || history[i - 1].season !== row.season;
      const name = clubSeasonName(row);
      const then = clubSeasonThenName(row);
      const range = row.firstDate === row.lastDate
        ? clubHistoryDate(row.lastDate)
        : `${clubHistoryDate(row.firstDate)} – ${clubHistoryDate(row.lastDate)}`;
      return (
        <li key={`${row.season}-${row.clubId}`} className={`club-history__row${first ? '' : ' club-history__row--same'}`}>
          <span className="club-history__season">
            {first && <span>{row.seasonLabel}</span>}
            <span className="club-history__courses">
              {(row.courses ?? []).filter(isSeason).map((c) => (
                <span key={c} title={COURSE_LABEL[c]}>
                  <UI_SeasonIcon season={c} size={13} label={COURSE_LABEL[c]} />
                </span>
              ))}
            </span>
          </span>
          {/* Эмблема — тем же `UI_ClubIcon` (с манифестом), что в чипе клуба шапки. */}
          <span className="club-history__logo">
            <UI_ClubIcon clubName={row.name || row.nameEn || ''} iconWidth="22px" />
          </span>
          <span className="club-history__club">
            <a dir="auto" className="club-history__name" href={routes.club(row.clubId)}>{name}</a>
            <span className="club-history__meta">
              {row.swims} {row.swims === 1 ? 'swim' : 'swims'} · {range}
            </span>
            {/* Клуб с тех пор сменил название (Н8): строка подписана текущим, мелко — прежнее. */}
            {then && (
              <span className="club-history__then" title={`The club was then called ${then}`}>
                then: <bdi>{then}</bdi>
              </span>
            )}
          </span>
        </li>
      );
    })}
  </ol>
);

export default UI_ClubHistoryList;
