import React from 'react';
import GroupLanes from '../../group-lanes';
import { useTrainingsTab } from '../trainings-context';
import { TrainingsSection } from '../trainings-section';

/**
 * Секция «Lanes» — план дорожек на дату: участнику опубликованный (доска, Т7), тренеру сразу
 * редактор (Р6). `GroupLanes` несёт навигацию по датам и своё состояние, поэтому рамка `bare`.
 * Из контекста берёт ширину (`isDesktop`), флаг несохранённых правок (`setLanesDirty` — каркас
 * спрашивает «Discard?» при смене вида) и перечитывание списка планов (`reloadLanePlans` — вид
 * Next тренера показывает статус плана).
 *
 * На телефоне — в `deep-panel-row`, как лента и Next: панель там без боковых паддингов, и без
 * обёртки стрелки дат, тулбар и дорожки стояли вплотную к краю экрана (найдено на Т10).
 */
function LanePlanSection() {
  const { group, manages, isDesktop, setLanesDirty, reloadLanePlans, markLanesShown } = useTrainingsTab();
  return (
    <TrainingsSection id="lane-plan" bare>
      <div className={isDesktop ? undefined : 'deep-panel-row'}>
        <GroupLanes
          groupId={group.id}
          manages={manages}
          desktop={isDesktop}
          onDirtyChange={setLanesDirty}
          onPlansChanged={reloadLanePlans}
          onPlanShown={markLanesShown}
        />
      </div>
    </TrainingsSection>
  );
}

export default LanePlanSection;
