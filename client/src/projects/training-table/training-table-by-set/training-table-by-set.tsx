import React from 'react';
import './training-table-by-set.css';
import Helper from '../../../utils/helpers/data-helper';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import UI_PaddlesIcon from '../../components/mix/paddles-icon/paddles-icon';
import { Result, TrainingGroup } from '../../../utils/interfaces/results';
import UI_PullBuoyIcon from '../../components/mix/pull-buoy-icon/pull-buoy-icon';
import UI_IntensityIcon from '../../components/mix/intensity-icon/intensity-icon';
import TrainingSetHeader from '../training-set-header/training-set-header';
import ExpectedTimeDiff from '../../components/mix/expected-time-diff/expected-time-diff';

interface TrainingTableBySetProps {
  results: Result[];
  selectedSource: { title?: string } & Record<string, any>;
  filters: Record<string, any>;
  updateFilter: (newFilter: Record<string, any>) => void;
  header: { firstResult?: Result; showEvent: boolean; showDate: boolean };
}

function TrainingTableBySet({
  results,
  selectedSource,
  filters,
  updateFilter,
  header,
}: TrainingTableBySetProps) {
  if (!results?.length) {
    return <div className="text-gray-500 italic">No data source selected.</div>;
  }

  // Группировка по сетам
  const groupedResults: TrainingGroup[] = Helper.groupTrainingBySet(results);

  // Флаги для отображения берем из header (вычисляются в родителе)
  const { showEvent, showDate } = header;

  return (
    <div className="training-table-by-set results w-full">
      {/* Глобальная шапка (title/date/club/large style icon) уже рендерится родителем через TrainingTableHeader */}

      <div className="max-h-100- overflow-y-auto border1 rounded shadow">
        {groupedResults.length === 0 && (
          <div className="text-center text-gray-500 py-4">No results match the current filters.</div>
        )}

        {groupedResults.map((group, gi) => {
          const altBg = gi % 2 === 0 ? 'bg-white' : 'bg-gray-100';
          const groupSet = group?.items[0]?.training?.set;
          const groupOrder = group?.items[0]?.training?.order;
          const groupTrainingName = group?.items[0]?.training?.trainingName;

          return (
            <ul key={`key_${gi}`} className={`mb-3 ${altBg} rounded border`}>
              <li>
                 {/* ===== Шапка блока ===== */}
                        <TrainingSetHeader
                          trainingName={groupTrainingName}
                          date={group.date}
                          interval={group.items[0]?.training?.interval}
                          pool_type={group.items[0]?.pool_type}
                          trainingId={group.items[0]?.training?.trainingId}
                        />
              </li>
              <li className="px-3 py-2 flex items-center justify-between border-b relative">
                <div className="text-lg font-bold cursor-pointer relative">
                  {/* Заголовок группы: Set.Order */}
                  <span className="ml-2 text-xl font-semibold">
                    Set {groupSet ?? '-'}.{groupOrder ?? '-'}
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
                  isMaster: !!res.is_masters,
                  event_style_age: res.event_style_age,
                });

                // Переход между order внутри одного сета — показать разделитель
                const prev = group.items[i - 1];
                const currOrder = Number(res?.training?.order ?? 0);
                const prevOrder = Number(prev?.training?.order ?? 0);
                const orderChanged = i > 0 && currOrder !== prevOrder;

                return (
                  <React.Fragment key={i}>
                    {orderChanged && (
                      <li
                        key={`divider-${i}`}
                        className="relative flex items-center justify-start my-2"
                      >
                        <div className="w-full border-t border-gray-400 absolute top-1/2"></div>
                        <span className="relative z-10 bg-white px-3 text-lg font-semibold">
                          Set {res.training?.set ?? '-'}.{res.training?.order ?? '-'}
                        </span>
                      </li>
                    )}

                    <li className="relative px-3 py-2 flex items-center gap-3">
                      {/* Имя спортсмена для groupBySet — всегда */}
                      <div
                        className="w-16 text-lg font-semibold cursor-pointer transition-colors duration-150 hover:text-blue-700"
                        onClick={() =>
                          updateFilter({
                            selected_name: `${res.first_name}${res.last_name ? ' ' + res.last_name : ''}`,
                          })
                        }
                        title={`Фильтровать по: ${res.first_name}${res.last_name ? ' ' + res.last_name : ''}`}
                      >
                        {res.first_name} {res.last_name ? `- ${res.last_name}` : ''}
                      </div>

                      {/* Иконка события при множественных стилях/длинах */}
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

                        <div className="flex flex-row lg:flex-col">
                          {res.training?.isPaddles && <UI_PaddlesIcon className="w-6 h-6" />}
                          {res.training?.isBuoy && <UI_PullBuoyIcon className="w-6 h-6" />}
                        </div>
                      </div>

                      {/* скорость */}
                      <div className="w-10 lg:w-12 text-2xl font-bold">
                        <UI_IntensityIcon intensity={res?.training?.intensity} className="text-2xl font-bold" />
                      </div>

                     
                      <div className="w-16 lg:w-24">
                        <div className="text-2xl font-bold">{res.time}</div>
                        <ExpectedTimeDiff time={res.time} expected_time={res.training?.expected_time} />
                      </div>

                      {/* Уровень (скрывать если Rating = None) */}
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

                          {/* Прогресс */}
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

                      {/* Дата (если разные даты) */}
                      {showDate && <div className="w-24 text-sm">{res.date}</div>}
                    </li>
                  </React.Fragment>
                );
              })}
            </ul>
          );
        })}
      </div>
    </div>
  );
}

export default TrainingTableBySet;
