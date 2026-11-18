import React from 'react';
import './club-history.css';
import UI_H2HPickerModal from '../h2h/h2h-picker-modal';
import UI_ClubHistoryList from './club-history-list';
import { clubHistoryCount, type ClubSeason } from './club-history.types';

interface Props {
  history?: ClubSeason[] | null;
  /** Чья история — в заголовок окна. */
  swimmerName?: string;
  /** `sm` — под подписью клуба в мини-карточке H2H. */
  size?: 'md' | 'sm';
}

/**
 * Значок ⇄ «пловец выступал за несколько клубов» рядом с клубом + окно истории по клику
 * (план swimmer-club-change, Ф2, решения К4–К5). Рисуется, только когда настоящих клубов
 * за карьеру больше одного; при одном — ничего, чтобы не было шума у большинства пловцов.
 *
 * ⚠ Это `span role="button"`, а не `<button>`: в H2H значок стоит ВНУТРИ карточки-кнопки
 * («выбрать другого»), и кнопка в кнопке невалидна. Клик и Enter/Space не всплывают — иначе
 * открытие окна заодно меняло бы сторону сравнения.
 *
 * Окно — общее `UI_H2HPickerModal` (портал с классом темы, Esc и клик по фону закрывают).
 */
const UI_ClubHistoryBadge: React.FC<Props> = ({ history, swimmerName, size = 'md' }) => {
  const [open, setOpen] = React.useState(false);
  const close = React.useCallback(() => setOpen(false), []);
  const count = clubHistoryCount(history);
  if (!history || count < 2) return null;

  const hint = `Swam for ${count} clubs — see the club history`;
  const openWindow = (e: React.SyntheticEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setOpen(true);
  };

  return (
    <>
      <span
        role="button"
        tabIndex={0}
        className={`club-history-badge${size === 'sm' ? ' club-history-badge--sm' : ''}`}
        title={hint}
        aria-label={hint}
        onClick={openWindow}
        onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') openWindow(e); }}
      >
        <span className="club-history-badge__arrow" aria-hidden>⇄</span>
        <span className="club-history-badge__label">{count} clubs</span>
      </span>
      {/* ⚠ События портала React ВСПЛЫВАЮТ по дереву компонентов, а не по DOM: клик в окне
          дошёл бы до карточки-кнопки H2H и сменил сторону. Обёртка их глушит; переход по
          ссылке клуба при этом работает — гасится всплытие, а не действие. */}
      <span style={{ display: 'contents' }} onClick={(e) => e.stopPropagation()}>
        <UI_H2HPickerModal
          open={open}
          title={swimmerName ? `Clubs · ${swimmerName}` : 'Club history'}
          onClose={close}
        >
          <p className="club-history-intro">Club per season, from the results of each meet.</p>
          <UI_ClubHistoryList history={history} />
        </UI_H2HPickerModal>
      </span>
    </>
  );
};

export default UI_ClubHistoryBadge;
