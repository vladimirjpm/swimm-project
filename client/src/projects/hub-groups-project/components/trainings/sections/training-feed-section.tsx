import React from 'react';
import TrainingFeed, { NewTrainingButton } from '../feed/training-feed';
import { useTrainingsTab } from '../trainings-context';
import { TrainingsSection } from '../trainings-section';

/**
 * Секция «Trainings» — лента тренировок (одна вместо прежних «Trainings & videos» + ссылки на
 * журнал; ссылку на экран Training log убрали по Р1, сам экран живёт по прямому адресу).
 * Т4: сама лента — `trainings/feed/` (карточка, фильтры, «Sets & times»); здесь только рамка.
 *
 * Десктоп — карточка с заголовком и «+ New training» в углу; телефон — `bare`: сегмент
 * `Trainings` уже назвал вид, а кнопку тренера рисует сама лента (Т6 может забрать её в
 * нижнюю панель). Формы и лайтбокс (Т3) открываются через `openSheet` контекста.
 */
function TrainingFeedSection() {
  const { sessions, marks, isDesktop } = useTrainingsTab();
  const data = sessions.data;

  if (!data) {
    return (
      <TrainingsSection
        id="training-feed"
        title={isDesktop ? 'Trainings' : undefined}
        mark={isDesktop ? marks.trainings : null}
        bare={!isDesktop}
        status={sessions.loadError ? 'error' : 'loading'}
        error={sessions.loadError}
      />
    );
  }

  if (!isDesktop) {
    return (
      <TrainingsSection id="training-feed" bare>
        {/* Без карточки лента держит ту же боковую линию, что сегменты над ней (`deep-panel-row`). */}
        <div className="deep-panel-row">
          <TrainingFeed data={data} />
        </div>
      </TrainingsSection>
    );
  }

  return (
    <TrainingsSection
      id="training-feed"
      title="Trainings"
      mark={marks.trainings}
      sub={<>🔒 private — group members and admins{!data.can_add_media && ' · only the coach adds videos'}</>}
      actions={data.can_manage ? <NewTrainingButton height={36} /> : undefined}
    >
      <TrainingFeed data={data} />
    </TrainingsSection>
  );
}

export default TrainingFeedSection;
