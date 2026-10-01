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
 */
function LanePlanSection() {
  const { group, manages, isDesktop, setLanesDirty, reloadLanePlans, markLanesShown } = useTrainingsTab();
  return (
    <TrainingsSection id="lane-plan" bare>
      <GroupLanes
        groupId={group.id}
        manages={manages}
        desktop={isDesktop}
        onDirtyChange={setLanesDirty}
        onPlansChanged={reloadLanePlans}
        onPlanShown={markLanesShown}
      />
    </TrainingsSection>
  );
}

export default LanePlanSection;
