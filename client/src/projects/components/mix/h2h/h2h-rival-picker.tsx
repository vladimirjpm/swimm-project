import React from 'react';
import UI_SwimmerAvatar from '../swimmer-avatar/swimmer-avatar';
import './h2h.css';

/**
 * Выбор соперника (макет 1b, §4): полоса избранного + поиск по имени.
 *
 * Стоит В ПОТОКЕ под слотами, а не всплывает поповером: всплывающая выдача уезжала за
 * нижний край страницы, и до последних имён нельзя было доскроллить (поймано на живой
 * странице 01.09.2026). Раздвигать ей всё равно нечего — сравнение появляется только
 * после выбора.
 */
export interface H2HPickerFavorite {
  id: number;
  name: string;
}

export interface H2HPickerHit {
  id: number;
  name: string;
  birthYear: number;
  clubName?: string | null;
  /** male | female — от него зависит дефолтный портрет строки (своего фото у поиска нет). */
  gender?: string | null;
}

interface Props {
  /** Избранные пловцы; пустой список — полоса не рисуется (гостю её тоже не показывают). */
  favorites: H2HPickerFavorite[];
  query: string;
  onQuery: (q: string) => void;
  hits: H2HPickerHit[] | null;
  loading: boolean;
  error: boolean;
  onPick: (id: number) => void;
  /** Ссылка на поле поиска: по ней пустой слот переводит фокус, когда его нажали. */
  inputRef?: React.Ref<HTMLInputElement>;
  /**
   * Текст пустой выдачи. Вызывающий может отфильтровать уже выбранных, и тогда «никого не
   * нашлось» — неправда: нашёлся, но он уже на доске.
   */
  emptyText?: string;
}

const UI_H2HRivalPicker: React.FC<Props> = ({
  favorites, query, onQuery, hits, loading, error, onPick, inputRef,
  emptyText = 'Nobody found.',
}) => (
  <div className="h2h-picker">
    {favorites.length > 0 && (
      <div className="h2h-picker__favs">
        <span className="h2h-picker__cap">Favorites</span>
        {favorites.map((f) => (
          <button key={f.id} type="button" className="h2h-fav-chip" onClick={() => onPick(f.id)}>
            <span className="h2h-fav-chip__heart" aria-hidden="true">♥</span>
            <span dir="auto">{f.name}</span>
          </button>
        ))}
      </div>
    )}

    <input
      ref={inputRef}
      className="h2h-search"
      type="search"
      value={query}
      onChange={(e) => onQuery(e.target.value)}
      placeholder="Search a swimmer to compare with..."
      aria-label="Search a swimmer to compare with"
    />

    {query.trim().length >= 2 && (
      <div className="h2h-hits">
        {error ? (
          <div className="h2h-hint">Could not search right now.</div>
        ) : !hits ? (
          <div className="h2h-hint">{loading ? 'Loading…' : 'Type at least two letters.'}</div>
        ) : hits.length === 0 ? (
          <div className="h2h-hint">{emptyText}</div>
        ) : (
          hits.map((hit) => (
            <button key={hit.id} type="button" className="h2h-hit" onClick={() => onPick(hit.id)}>
              {/* Портрет тем же компонентом, что в карточках: в списке выбора человек
                  узнаётся по лицу быстрее, чем по строке. Фото у поиска нет — идёт дефолт
                  по полу, страна домашняя. */}
              <UI_SwimmerAvatar gender={hit.gender} name={hit.name} size={28} className="h2h-hit__avatar" />
              <span className="h2h-hit__name" dir="auto">{hit.name}</span>
              <span className="h2h-hit__meta">
                {hit.birthYear > 0 ? hit.birthYear : '—'}
                {hit.clubName ? ` · ${hit.clubName}` : ''}
              </span>
            </button>
          ))
        )}
      </div>
    )}
  </div>
);

export default UI_H2HRivalPicker;
