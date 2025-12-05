import React from 'react';
import './training-show-full-table.css';
import { Result } from '../../../utils/interfaces/results';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_PaddlesIcon from '../../components/mix/paddles-icon/paddles-icon';
import UI_PullBuoyIcon from '../../components/mix/pull-buoy-icon/pull-buoy-icon';
import Helper from '../../../utils/helpers/data-helper';
import UI_IntensityIcon from '../../components/mix/intensity-icon/intensity-icon';
import TrainingSetHeader from '../training-set-header/training-set-header';
import ExpectedTimeDiff from '../../components/mix/expected-time-diff/expected-time-diff';

// ===== Пивот-структура для таблицы =====
export type PivotRow = {
  set: number;
  order: number;
  intensity?: string;
  // Уникальные для этой строки (set/order): если все ячейки совпадают
  len?: string;
  style?: string;
  // Если у всех ячеек одинаковые isPaddles/isBuoy — выносим в общую колонку
  isPaddles?: boolean;
  isBuoy?: boolean;
  cells: Record<
    string,
    | {
        time: string;
        style: string;
        len: string;
        isPaddles?: boolean;
        isBuoy?: boolean;
        expected_time?: string;
      }
    | undefined
  >;
};

export type PivotBlock = {
  trainingId: number;
  trainingName?: string;
  date: string;
  pool_type: string | undefined;
  interval: number | undefined;
  intensity: string | undefined;
  // Общие для всего блока (если весь блок однороден)
  len?: string;
  style?: string;
  swimmers: string[];
  nameMap: Record<string, string>;
  rows: PivotRow[];
};

function buildPivot(results: Result[]): PivotBlock[] {
  const byTrain = new Map<number, Result[]>();
  for (const r of results) {
    if (!r.training) continue;
    const tid = r.training.trainingId!;
    if (!byTrain.has(tid)) byTrain.set(tid, []);
    byTrain.get(tid)!.push(r);
  }

  const blocks: PivotBlock[] = [];

  for (const [trainingId, items] of byTrain) {
    if (!items.length) continue;

    // Метаданные блока
    const sample = items[0];
    const date = sample.date;
    const pool_type = sample.pool_type;
    const interval = sample.training?.interval;
    const intensity = sample.training?.intensity;
    const trainingName = sample.training?.trainingName;

    // Общие len/style для всего блока (если однородно)
    const blockLens = new Set(items.map((r) => r.event_style_len).filter(Boolean));
    const blockLen = blockLens.size === 1 ? [...blockLens][0] : undefined;

    const blockStyles = new Set(items.map((r) => r.event_style_name).filter(Boolean));
    const blockStyle = blockStyles.size === 1 ? [...blockStyles][0] : undefined;

    // Список имён (первое появление каждого имени)
    const swimmers: string[] = [];
    const seen = new Set<string>();
    const nameMap: Record<string, string> = {};

    for (const r of items) {
      if (!seen.has(r.first_name)) {
        seen.add(r.first_name);
        swimmers.push(r.first_name);
        nameMap[r.first_name] = `${r.first_name}${r.last_name ? ' ' + r.last_name : ''}`;
      }
    }

    // Собираем уникальные пары (set, order)
    type K = { set: number; order: number };
    const keySet = new Set<string>();
    const keys: K[] = [];
    for (const r of items) {
      const k = `${r.training!.set}|${r.training!.order}`;
      if (!keySet.has(k)) {
        keySet.add(k);
        keys.push({ set: r.training!.set, order: r.training!.order });
      }
    }

    keys.sort((a, b) => a.set - b.set || a.order - b.order);

    const rows: PivotRow[] = keys.map(({ set, order }) => {
      // Поднабор результатов только для этой строки
      const rowItems = items.filter(
        (rr) => rr.training?.set === set && rr.training?.order === order
      );

      // Уникальные для строки стиль/длина
      const rowStyles = new Set(
        rowItems.map((rr) => rr.event_style_name).filter(Boolean) as string[]
      );
      const rowLens = new Set(
        rowItems.map((rr) => rr.event_style_len).filter(Boolean) as string[]
      );

      const rowStyle = rowStyles.size === 1 ? [...rowStyles][0] : undefined;
      const rowLen = rowLens.size === 1 ? [...rowLens][0] : undefined;

      // Определяем, есть ли isPaddles/isBuoy у всех ячеек строки
      const allPaddles = rowItems.length > 0 && rowItems.every((rr) => rr.training?.isPaddles === true);
      const allBuoy = rowItems.length > 0 && rowItems.every((rr) => rr.training?.isBuoy === true);

      const sampleRow = rowItems[0];
      const row: PivotRow = {
        set,
        order,
        intensity: sampleRow?.training?.intensity,
        style: rowStyle,
        len: rowLen,
        isPaddles: allPaddles || undefined,
        isBuoy: allBuoy || undefined,
        cells: {},
      };

      for (const name of swimmers) {
        const found = rowItems.find((r) => r.first_name === name);
        if (found) {
          row.cells[name] = {
            time: found.time ?? '',
            style: found.event_style_name ?? '',
            len: found.event_style_len ?? '',
            isPaddles: found.training?.isPaddles ?? false,
            isBuoy: found.training?.isBuoy ?? false,
            expected_time: found.training?.expected_time,
          };
        } else {
          row.cells[name] = undefined;
        }
      }
      return row;
    });

    // Последняя строка для оценки "кто быстрее" при сортировке имён (лево→право)
    const lastRow = rows[rows.length - 1];

    const swimmersSorted = [...swimmers].sort((a, b) => {
      const ta = Helper.parseTimeToSeconds(lastRow.cells[a]?.time!);
      const tb = Helper.parseTimeToSeconds(lastRow.cells[b]?.time!);
      if (ta === null && tb === null) return 0;
      if (ta === null) return 1;
      if (tb === null) return -1;
      return ta - tb; // меньше = быстрее = левее
    });

    blocks.push({
      trainingId,
      trainingName,
      date,
      pool_type,
      interval,
      intensity,
      swimmers: swimmersSorted,
      nameMap,
      rows,
      len: blockLen,
      style: blockStyle,
    });
  }

  blocks.sort((a, b) => a.trainingId - b.trainingId);
  return blocks;
}

interface TrainingShowFullTableProps {
  results: Result[];
  selectedSource: { title?: string } & Record<string, any>;
  filters: Record<string, any>;
  updateFilter: (newFilter: Record<string, any>) => void;
  header: { firstResult?: Result; showEvent: boolean; showDate: boolean };
}

function TrainingShowFullTable({
  results,
  selectedSource, // оставлено для совместимости пропсов
  filters,
  updateFilter,
  header, // заголовок/шапку рендерит родитель
}: TrainingShowFullTableProps) {
  if (!results?.length) {
    return <div className="text-gray-500 italic">No data source selected.</div>;
  }

  // Построить пивот-таблицы по trainingId
  const blocks = buildPivot(results);

  const fmtRowLabel = (
    set: number,
    order: number,
    intensity?: string,
    lenUnique?: string,
    styleUnique?: string,
    fallbackLenFromFirstCell?: string,
    isPaddles?: boolean,
    isBuoy?: boolean
  ) => (
    <div className="flex items-center gap-3">
      <div className="flex items-baseline gap-1">
        <span className="text-2xl font-bold">{set}</span>
        <span className="text-xl">.{order}</span>
      </div>

      <div className='w-16 text-center'>
      {/* Если у строки единая длина — показываем бэдж; иначе берём из первой ячейки как подсказку */}
      {((lenUnique || fallbackLenFromFirstCell) && !styleUnique) && (
        <span className="text-red-700 text-lg font-semiboldtext-red-700">
          {(lenUnique || fallbackLenFromFirstCell) + 'm'}
        </span>
      )}

      {/* Если у строки единый стиль — компактная иконка со встроенной длиной */}
      {styleUnique && (
        <div className="w-fit">
          <UI_SwimmStyleIcon
            styleName={styleUnique}
            styleLen={lenUnique ?? fallbackLenFromFirstCell ?? ''}
            styleType="icon-len"
            className="w-14"
          />
        </div>
      )}
      
      </div>

      <UI_IntensityIcon intensity={intensity} />

      {/* Если у всей строки isPaddles/isBuoy — показываем общие иконки */}
      {(isPaddles || isBuoy) && (
        <div className="flex items-center gap-1">
          {isPaddles && <UI_PaddlesIcon className="w-6 h-6" />}
          {isBuoy && <UI_PullBuoyIcon className="w-6 h-6" />}
        </div>
      )}
    </div>
  );

  return (
  <div className="training-show-full-table results">
    {blocks.map((b) => (
      <div key={b.trainingId} className="pt-4 md:pt-10 mb-8">
        {/* ===== Шапка блока ===== */}
        <TrainingSetHeader
          trainingName={b.trainingName}
          date={b.date}
          interval={b.interval}
          pool_type={b.pool_type}
          trainingId={b.trainingId}
          header={header}
        />

        {/* ===== Таблица: внутри контейнера скролл по X, экран не раздувается ===== */}
        <div className="relative w-full max-w-full overflow-x-auto">
          <table
            className="border-collapse"
            style={{ minWidth: 720 }} // <= гарантируем горизонтальный скролл на мобиле
          >
            <thead>
              <tr>
                {/* sticky первая ячейка шапки */}
                <th
                  className="px-2 py-1 border text-left bg-white sticky z-30"
                  style={{ left: 0, width: '13rem' }} // ~ w-52
                >
                  Set • Rep • Style • V
                </th>
                {b.swimmers.map((name) => (
                  <th
                    key={name}
                    className="px-2 py-1 border text-left cursor-pointer duration-150 hover:bg-blue-100 hover:text-blue-700 bg-white"
                    onClick={() =>
                      updateFilter({ selected_name: b.nameMap?.[name] || name })
                    }
                  >
                    {b.nameMap?.[name] || name}
                  </th>
                ))}
              </tr>
            </thead>

            <tbody>
              {b.rows.map((row) => (
                <tr key={`${row.set}-${row.order}`}>
                  {/* sticky первая колонка */}
                  <td
                    className="px-2 py-1 border text-left bg-white sticky z-20"
                    style={{ left: 0, width: '13rem' }} // ~ w-52
                  >
                    {fmtRowLabel(
                      row.set,
                      row.order,
                      row.intensity,
                      row.len,
                      row.style,
                      row.cells[b.swimmers[0]]?.len,
                      row.isPaddles,
                      row.isBuoy
                    )}
                  </td>

                  {b.swimmers.map((name) => {
                    const cell = row.cells[name];
                    return (
                      <td key={name} className="align-top px-2 py-1 border bg-white">
                        {cell ? (
                          <div className="flex flex-col leading-tight items-start gap-1">
                            <div className="text-xl font-semibold">{cell.time}</div>
                            <ExpectedTimeDiff time={cell.time} expected_time={cell.expected_time} />

                            {!row.style && (
                              <div className="w-fit mx-auto">
                                <UI_SwimmStyleIcon
                                  styleName={cell.style}
                                  styleLen={cell.len}
                                  styleType="icon-notext"
                                  className="w-12"
                                />
                              </div>
                            )}

                            {/* Показываем иконки только если они не вынесены в общую колонку */}
                            {((cell.isPaddles && !row.isPaddles) || (cell.isBuoy && !row.isBuoy)) && (
                              <div className="mt-1 flex items-center gap-1">
                                {cell.isPaddles && !row.isPaddles && <UI_PaddlesIcon className="w-6 h-6" />}
                                {cell.isBuoy && !row.isBuoy && <UI_PullBuoyIcon className="w-6 h-6" />}
                              </div>
                            )}
                          </div>
                        ) : (
                          <span className="text-gray-300">—</span>
                        )}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    ))}

    {!blocks.length && (
      <div className="text-gray-500 italic">Нет результатов по текущим фильтрам.</div>
    )}
  </div>
);

}

export default TrainingShowFullTable;
