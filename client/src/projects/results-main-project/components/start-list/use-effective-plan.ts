import { useMemo } from 'react';
import { useFavoritesContext } from '../../../../hooks/favorites-context';
import { useStartListClubs, useStartListSwimmers } from './use-start-list';
import { defaultPlanFromFavorites, effectivePlan, isEmptyPlan } from './plan-model';
import type { StartListPlan } from './use-start-list-plan';

/**
 * Действующий состав плана и данные под него — ОДИН источник на оба экрана личного плана
 * (пикер и карточка) и на решение «какой экран показать первым».
 *
 * Зачем хук, а не расчёт внутри экранов: состав нужен раньше, чем открыт хоть один из них.
 * Таб решает, показывать ли сразу карточку, ещё до первого клика — а для этого надо знать,
 * есть ли кому в ней быть. Плюс пикер и карточка иначе тянули бы одни и те же запросы
 * дважды.
 *
 * Правило состава (решение Влада 29.08.2026): **свой план сильнее избранного, даже пустой**
 * — сохранённый пустой план значит «я всё снял сам». Избранные дают лишь ДЕФОЛТ, и только
 * те из них, кто на этом старте реально заявлен.
 */
export function useEffectivePlan(
  orgCompIds: number[],
  saved: StartListPlan | null,
  /**
   * Пловцы, которых надо ЗАГРУЗИТЬ, но которые в состав не входят — состав, приехавший
   * ссылкой (`?plan=`). Ссылку открывает чужой человек: этих пловцов нет ни в его
   * избранном, ни в его сохранённом плане, и без них карточка приходила пустой
   * («Nothing on this day»), а чип состава показывал `#6601` вместо имени.
   *
   * На сам состав это не влияет: правило «ссылка не перезаписывает план получателя»
   * держится тем, что `sharedPlan` показывается мимо `plan`, а в дефолт из избранного
   * попадают только избранные (`defaultPlanFromFavorites`).
   */
  alsoLoadSwimmerIds: readonly number[] = [],
) {
  const { favorites, primarySwimmerId } = useFavoritesContext();

  const favSwimmerIds = useMemo(
    () => favorites.filter((f) => f.swimmer_id != null).map((f) => f.swimmer_id as number),
    [favorites],
  );
  const favClubIds = useMemo(
    () => favorites.filter((f) => f.club_id != null).map((f) => f.club_id as number),
    [favorites],
  );

  // Карточки грузим и на избранных, и на тех, кого добавили поиском: и те и другие могут
  // оказаться в составе, а имя с днями нужно обоим экранам.
  const rowIds = useMemo(
    () => [...new Set([...favSwimmerIds, ...(saved?.swimmer_ids ?? []), ...alsoLoadSwimmerIds])],
    [favSwimmerIds.join(','), (saved?.swimmer_ids ?? []).join(','), [...alsoLoadSwimmerIds].join(',')],
  );

  const { data: swimmers, loading: swimmersLoading } = useStartListSwimmers(orgCompIds, rowIds);
  const { data: clubs, loading: clubsLoading } = useStartListClubs(orgCompIds);

  const plan = useMemo(
    () => effectivePlan(saved, defaultPlanFromFavorites(
      favSwimmerIds,
      favClubIds,
      new Set(Object.keys(swimmers).map(Number)),
      new Set((clubs ?? []).map((c) => c.club_id)),
    )),
    [saved, favSwimmerIds.join(','), favClubIds.join(','), swimmers, clubs],
  );

  return {
    /** Состав, который надо показывать: сохранённый план либо дефолт из избранного. */
    plan,
    /** Пусто — показывать карточку нечем, вход ведёт в пикер. */
    isEmpty: isEmptyPlan(plan),
    /** Карточки пловцов (избранные + добавленные) — имена, дни, заплывы. */
    swimmers,
    /** Клубы соревнования со счётчиками. */
    clubs: clubs ?? [],
    rowIds,
    favClubIds,
    primarySwimmerId,
    loading: swimmersLoading || clubsLoading,
  };
}
