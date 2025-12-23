import React from 'react';
import './training-table-by-name.css';
import Helper from '../../../utils/helpers/data-helper';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import UI_PaddlesIcon from '../../components/mix/paddles-icon/paddles-icon';
import { Result, TrainingGroup } from '../../../utils/interfaces/results';
import UI_PullBuoyIcon from '../../components/mix/pull-buoy-icon/pull-buoy-icon';
import UI_IntensityIcon from '../../components/mix/intensity-icon/intensity-icon';
import TrainingSetHeader from '../training-set-header/training-set-header';
import ExpectedTimeDiff from '../../components/mix/expected-time-diff/expected-time-diff';

interface TrainingTableByNameProps {
  results: Result[];
  selectedSource: { title?: string } & Record<string, any>;
  filters: Record<string, any>;
  updateFilter: (newFilter: Record<string, any>) => void;
  header: { firstResult?: Result; showEvent: boolean; showDate: boolean };
}

function TrainingTableByName({
  results,
  selectedSource,
  filters,
  updateFilter,
  header,
}: TrainingTableByNameProps) {
  if (!results?.length) {
    return <div className="text-gray-500 italic">No data source selected.</div>;
  }

  // Группировка по имени
  const groupedResults: TrainingGroup[] = Helper.groupTrainingByName(results);

  // Флаги отображения получаем из header (вычисляются в родителе)
  const { showEvent, showDate } = header;

  // Подготовим элементы списков, чтобы рендерить общую шапку по дате (только первый блок для каждой даты)
  const renderedGroups: JSX.Element[] = [];
  (() => {
    let lastDate: string | null = null;
    for (let gi = 0; gi < groupedResults.length; gi++) {
      const group = groupedResults[gi];
      const altBg = gi % 2 === 0 ? 'bg-white' : 'bg-gray-100';
      const groupTrainingName = group?.items[0]?.training?.trainingName;
      const showHeader = !!group.date && group.date !== lastDate;
      if (showHeader) lastDate = group.date || null;

      renderedGroups.push(
        <ul key={`key_${gi}`} className={`mb-3 ${altBg} rounded border`}>
          {showHeader && (
            <li>
              <TrainingSetHeader
                trainingName={groupTrainingName}
                date={group.date}
                interval={group.items[0]?.training?.interval}
                pool_type={group.items[0]?.pool_type}
                trainingId={group.items[0]?.training?.trainingId}
              />
            </li>
          )}

          <li className="px-3 py-2 flex items-center justify-between border-b relative">
            <div
              className="text-lg font-bold cursor-pointer relative transition-colors duration-150 hover:text-blue-700"
              onClick={() => updateFilter({ selected_name: group.title })}
              title={`Фильтровать по: ${group.title}`}
            >
              <span>
                {group.title}&nbsp;
                {group.items[0]?.event_style_age && (
                  <span className="text-xs font-semibold text-gray-600">
                    {group.items[0].event_style_age}
                  </span>
                )}
              </span>
            </div>
          </li>

          {/* Строки тренировок */}
          {group.items.map((res: Result, i: number) => {
                const levelInfo = Helper.getNormativeLevelInfo({
                  gender: res.event_style_gender === 'female' ? 'female' : 'male',
                  poolType: res.pool_type === '25' ? '25m_pool' : '50m_pool',
                  styleName: res.event_style_name,
                  distance: `${res.event_style_len}m`,
                  time: Helper.parseTimeToSeconds(res.time),
                  isMaster: String(res.is_masters) === 'true' || String(res.is_masters) === '1',
                  event_style_age: res.event_style_age,
                });

            return (
              <li key={i} className="relative px-3 py-2 flex items-center gap-3">
                <div className="w-6 relative">
                  <span className="text-xl font-bold">{res.training?.set ?? '-'}</span>
                  <span>.{res.training?.order ?? '-'}</span>
                </div>

                <div className="flex flex-col lg:flex-row">
                  {showEvent && (
                    <div className="w-16 lg:w-32">
                      <UI_SwimmStyleIcon
                        styleName={res.event_style_name}
                        styleLen={res.event_style_len}
                        styleType="icon-len"
                        className="font-bold text-2xl"
                      />
                    </div>
                  )}

                  <div className="w-6 flex flex-row lg:flex-col">
                    {res.training?.isPaddles && <UI_PaddlesIcon className="w-6 h-6" />}
                    {res.training?.isBuoy && <UI_PullBuoyIcon className="w-6 h-6" />}
                  </div>
                </div>

                <div className="w-10 lg:w-12 text-2xl font-bold">
                  <UI_IntensityIcon intensity={res?.training?.intensity} className="text-2xl font-bold" />
                </div>

                <div className="w-16 lg:w-24">
                  <div className="text-2xl font-bold">{res.time}</div>
                  <ExpectedTimeDiff time={res.time} expected_time={res.training?.expected_time} />
                </div>

                {filters?.rating_mode !== 'no' && (
                 <>
                  <div className="w-10 md:w-16">
                    <UI_NormativeLevelIcon
                      levelName={levelInfo.currentLevel}
                      styleType="style-1"
                      styleSize="size-2"
                      styleName={res.event_style_name}
                      styleLen={res.event_style_len}
                      poolType={res.pool_type}
                      isMasters={res.is_masters}
                      normativeAgeGroup={levelInfo.normativeAgeGroup}
                    />
                  </div>

                  <div className="hidden md:block w-24">
                    <div>
                      <span className="text-sm font-bold">{res.time}</span> →{' '}
                      <span className="text-xs">{levelInfo.nextTime}</span>
                    </div>
                    <div className="w-full bg-gray-200 rounded-full mt-1">
                      <div
                        className="bg-blue-600 text-xs font-medium text-blue-100 text-center p-0.5 leading-none rounded-full"
                        style={{ width: `${levelInfo.progressToNextLevel}%` }}
                      >
                        {levelInfo.progressToNextLevel}%
                      </div>
                    </div>
                  </div>
                 </>
                )}

                {showDate && <div className="w-24 text-sm">{res.date}</div>}
              </li>
            );
          })}
        </ul>
      );
    }
  })();

  return (
    <div className="training-table-by-name results w-full">
      {/* Глобальная шапка (title/date/club/large style icon) уже рендерится в родителе через TrainingTableHeader */}

      {/* ===== РЕНДЕР ГРУПП КАК <ul> ===== */}
      <div className="max-h-100- overflow-y-auto border1 rounded shadow">
        {groupedResults.length === 0 && (
          <div className="text-center text-gray-500 py-4">No results match the current filters.</div>
        )}

        {renderedGroups}
      </div>
    </div>
  );
}

export default TrainingTableByName;
