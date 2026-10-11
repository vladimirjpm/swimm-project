import React from 'react';
import { useFavoritesContext } from '../../../../hooks/favorites-context';
import UI_H2HCompare, { h2hScopeLabel } from '../../../components/mix/h2h/h2h-compare';
import UI_H2HRivalPicker from '../../../components/mix/h2h/h2h-rival-picker';
import UI_H2HPickerModal from '../../../components/mix/h2h/h2h-picker-modal';
import type { H2HSlot } from '../../../components/mix/h2h/h2h.types';
import { routes } from '../../../../utils/routes';
import type { SwimmerCompare, SwimmerSearchHit } from '../../use-swimmer-page';
import { type PanelLoad, PanelHead } from '../swimmer-panels';

/**
 * Таб H2H (head-to-head) страницы пловца.
 *
 * Сам экран — общий `UI_H2HCompare` (`components/mix/h2h/`), тот же, что будет на странице
 * `/h2h` (план — `docs/plans/h2h-page-plan.md`). Здесь остаётся только то, что специфично
 * для ТАБА: левый слот занят хозяином страницы и несменяем (`onSelect: null`), правый —
 * соперник из адреса, а выбор ищет именно соперника.
 *
 * Соперник выбирается ВРУЧНУЮ (решение Влада 2026-09-01): таб отвечает на вопрос «как я
 * против ВОТ ЭТОГО пловца», а не «кто мои соперники», — автосписка соседей по времени нет.
 */
export function H2HPanel({
  compare, query, onQuery, hits, hitsState, onPick, rivalId, swimmerId, profileName,
  owner, season, state,
}: {
  compare: SwimmerCompare | null;
  /** Строка поиска — состояние живёт на странице, чтобы переживать смену сезона. */
  query: string;
  onQuery: (q: string) => void;
  hits: SwimmerSearchHit[] | null;
  hitsState: PanelLoad;
  onPick: (id: number) => void;
  /** Выбранный соперник; null — показываем слот выбора. */
  rivalId: number | null;
  /** Хозяин страницы: его самого из избранного убираем — сравнивать с собой нечего. */
  swimmerId: number;
  /** Имя хозяина страницы: слот рисуется ещё до того, как приедет сравнение. */
  profileName: string;
  /**
   * Остальная внешность хозяина страницы — из профиля, который на странице уже есть.
   * ⚠ Без неё слот до приезда сравнения оставался БЕЗ ПОЛА, а дефолтный портрет
   * (`identityDefaultAvatar`) считает «не male» женщиной: мужчина на табе H2H показывался
   * девочкой, пока соперник не выбран, — а без соперника сравнения и не запрашивают, так
   * что это было состояние по умолчанию (поймано Владом 21.09.2026 на 7467).
   */
  owner?: { gender?: string | null; avatarUrl?: string | null; countryCode?: string | null };
  /** Сезон карусели — уезжает в ссылку на страницу `/h2h`, чтобы она открыла тот же период. */
  season: number | null;
  state: PanelLoad;
}) {
  const { isAuthenticated, favorites, favoriteSwimmerIds, toggleFavoriteSwimmer, fullHint } =
    useFavoritesContext();
  // Выбор соперника живёт в ОКНЕ: в потоке он стоял под всеми карточками заплывов, и
  // чтобы сменить соперника, приходилось сперва доскроллить до низа таба.
  const [pickerOpen, setPickerOpen] = React.useState(false);
  const searchRef = React.useRef<HTMLInputElement>(null);

  const favoriteRivals = favorites
    .filter((f) => f.target_type === 'swimmer' && f.swimmer_id != null && f.swimmer_id !== swimmerId)
    // Порядок пользователя: «Me» первым, дальше его сортировка.
    .sort((a, b) => Number(b.is_primary) - Number(a.is_primary) || a.sort_order - b.sort_order)
    .map((f) => ({ id: f.swimmer_id!, name: f.swimmer_name ?? `#${f.swimmer_id}` }));

  // Себя соперником не предлагаем: сервер такой запрос отклоняет (rivalId == id), а в
  // выдаче собственное имя выглядит как приглашение сравнить себя с собой.
  const rivalHits = hits?.filter((h) => h.id !== swimmerId) ?? null;

  const picker = (
    <UI_H2HPickerModal
      open={pickerOpen}
      title="Choose a rival"
      onClose={() => setPickerOpen(false)}
    >
      <UI_H2HRivalPicker
        favorites={favoriteRivals}
        query={query}
        onQuery={onQuery}
        hits={rivalHits}
        loading={hitsState.loading}
        error={hitsState.error}
        onPick={(id) => { onPick(id); setPickerOpen(false); }}
        inputRef={searchRef}
      />
    </UI_H2HPickerModal>
  );

  /** Чип макета: «9 y · 2017». Возраст без года рождения не показываем — его нечем проверить. */
  const ageLabel = (side: SwimmerCompare['mine']) =>
    (side.ageInSeason != null && (side.birthYear ?? 0) > 0
      ? `${side.ageInSeason} y · ${side.birthYear}`
      : (side.birthYear ?? 0) > 0 ? `b. ${side.birthYear}` : null);

  const favProps = (id: number) => ({
    isFavorite: isAuthenticated ? favoriteSwimmerIds.has(id) : null,
    onToggleFavorite: () => toggleFavoriteSwimmer(id),
    // Лимит избранного выбран — у ещё-не-избранного сердечко погашено с подсказкой.
    favoriteBlockedHint: favoriteSwimmerIds.has(id) ? null : fullHint('swimmer'),
  });

  // Левый слот — хозяин страницы: имя берём из compare, когда оно уже приехало (там же клуб
  // и возраст), иначе из профиля, чтобы карточка не ждала запроса.
  const left: H2HSlot = {
    kind: 'swimmer',
    swimmer: compare
      ? {
        id: compare.mine.id,
        name: compare.mine.name || profileName,
        club: compare.mine.clubName,
        ageLabel: ageLabel(compare.mine),
        avatarUrl: compare.mine.avatarUrl,
        gender: compare.mine.gender,
        countryCode: compare.mine.countryCode,
      }
      : {
        id: swimmerId,
        name: profileName,
        avatarUrl: owner?.avatarUrl,
        gender: owner?.gender,
        countryCode: owner?.countryCode,
      },
    ...favProps(swimmerId),
    // Хозяина страницы сменить нельзя — это его профиль.
    onSelect: null,
  };

  const right: H2HSlot = rivalId == null
    ? { kind: 'empty', onPick: () => setPickerOpen(true) }
    : {
      kind: 'swimmer',
      swimmer: compare
        ? {
          id: compare.rival.id,
          name: compare.rival.name || `#${rivalId}`,
          club: compare.rival.clubName,
          ageLabel: ageLabel(compare.rival),
          avatarUrl: compare.rival.avatarUrl,
          gender: compare.rival.gender,
          countryCode: compare.rival.countryCode,
        }
        : { id: rivalId, name: `#${rivalId}` },
      ...favProps(rivalId),
      // Клик по карточке соперника — «выбрать другого»: открывается окно выбора, а сам
      // соперник ОСТАЁТСЯ на месте до того, как выбран новый. Убирать его сразу нельзя:
      // экран терял бы сравнение на полпути, и человек, ткнувший из любопытства, оставался
      // бы ни с чем (замечание Влада 23.09.2026).
      onSelect: () => setPickerOpen(true),
    };

  return (
    <>
      {/* Кнопки «Clear» в шапке нет: соперник меняется выбором нового — кликом по его
          карточке и поиском под ней, тем же жестом, что на странице `/h2h`. Отдельного
          «убрать» на экране нет вовсе: пустая правая сторона это состояние ДО первого
          выбора, а не то, в которое возвращаются. Два одинаковых действия рядом заставляли бы
          выбирать между ними на ровном месте.

          Справа — выход на полную страницу сравнения: тот же экран, но там сменяемы ОБЕ
          стороны. Пару и сезон уносим с собой, иначе переход стоил бы повторного выбора. */}
      <PanelHead
        title="Compare"
        hint={compare ? h2hScopeLabel(compare) : 'pick a swimmer to put your best times side by side'}
        right={(
          <a
            className="h2h-open"
            href={routes.h2h({ a: swimmerId, b: rivalId, season: season == null ? 'all' : season })}
          >
            Full page →
          </a>
        )}
      />
      <UI_H2HCompare left={left} right={right} compare={compare} state={state} picker={picker} />
    </>
  );
}
