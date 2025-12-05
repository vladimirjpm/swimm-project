import React from 'react';
import Select, { MultiValue, SingleValue } from 'react-select';
import './filter-data-source-ddl.css';
import {
  rootActions,
  useAppDispatch,
  useAppSelector,
} from '../../../store/store';
import HelperConvert from '../../../utils/helpers/data-helper-convert';
import { Result } from '../../../utils/interfaces/results';

type DataSource = {
  name: string;
  load: () => Promise<Result[]>;
  /** optional filename used by this DataSource (e.g. 'dolphin_data.json') */
  file?: string;
};

type Option = { value: string; label: string };

const makeUrl = (file: string) =>
  `${import.meta.env.BASE_URL}data/${file}`;

const dataSources: DataSource[] = [
  {
    name: 'Dolphin Training',
    file: 'dolphin_data.json',
    load: async () => {
      const resp = await fetch(makeUrl('dolphin_data.json'));
      if (!resp.ok) {
        throw new Error('Failed to load dolphin_data.json');
      }
      const data = (await resp.json()) as Result[];
      return data;
    },
  },
  {
    name: 'Dolphin Masters',
    file: 'dolphin_masters_data.json',
    load: async () => {
      const resp = await fetch(makeUrl('dolphin_masters_data.json'));
      if (!resp.ok) {
        throw new Error('Failed to load dolphin_masters_data.json');
      }
      const data = (await resp.json()) as Result[];
      return data;
    },
  },
  {
    name: '2024 Isr Champ',
    file: 'competition-2024-summer-isr-championship.json',
    load: async () => {
      const resp = await fetch(
        makeUrl('competition-2024-summer-isr-championship.json'),
      );
      if (!resp.ok) {
        throw new Error(
          'Failed to load competition-2024-summer-isr-championship.json',
        );
      }
      const raw = await resp.json();
      return HelperConvert.convertPDFResultsToResults(raw);
    },
  },
  {
    name: '2025 Hapoel Young Champ',
    file: 'competition-2025-hapoel-young-isr-championship.json',
    load: async () => {
      const resp = await fetch(
        makeUrl('competition-2025-hapoel-young-isr-championship.json'),
      );
      if (!resp.ok) {
        throw new Error(
          'Failed to load competition-2025-hapoel-young-isr-championship.json',
        );
      }
      const raw = await resp.json();
      return HelperConvert.convertPDFResultsToResults(raw);
    },
  },
  {
    name: '2025 Isr 9-11 Champ 2 day',
    file: 'competition-2025-summer-isr-championship-age_9-11.json',
    load: async () => {
      const resp = await fetch(
        makeUrl('competition-2025-summer-isr-championship-age_9-11.json'),
      );
      if (!resp.ok) {
        throw new Error(
          'Failed to load competition-2025-summer-isr-championship-age_9-11.json',
        );
      }
      const raw = await resp.json();
      return HelperConvert.convertPDFResultsToResults(raw);
    },
  },
  {
    name: '2025 Isr 11-13 Champ 4 day',
    file: 'competition-2025-summer-isr-championship-age_11-13.json',
    load: async () => {
      const resp = await fetch(
        makeUrl('competition-2025-summer-isr-championship-age_11-13.json'),
      );
      if (!resp.ok) {
        throw new Error(
          'Failed to load competition-2025-summer-isr-championship-age_11-13.json',
        );
      }
      const data = (await resp.json()) as Result[];
      return data;
    },
  },
   {
    name: 'Filter Data',
    load: async () => window.filter_data,
  },
  {
    name: 'Normative Records',
    load: async () => window.normative_records,
  },
  {
    name: 'Normative',
    load: async () => window.normative,
  },
];

const byName: Record<string, DataSource> = Object.fromEntries(
  dataSources.map((ds) => [ds.name, ds]),
);

const parseToISO = (d: string) => {
  const [day, month, year] = d.split('/');
  return `${year}-${month.padStart(2, '0')}-${day.padStart(2, '0')}`;
};

const getMaxDateAndIso = (items: Array<Pick<Result, 'date'>>) => {
  const dates = items.map((i) => i.date).filter(Boolean) as string[];
  if (!dates.length) return null;

  const maxDate = dates.reduce(
    (max, curr) =>
      new Date(parseToISO(curr)) > new Date(parseToISO(max)) ? curr : max,
    dates[0],
  );

  const [d, m, y] = maxDate.split('/');
  return { maxDate, iso: `${y}-${m.padStart(2, '0')}-${d.padStart(2, '0')}` };
};

const DataSourceDDL: React.FC = () => {
  const dispatch = useAppDispatch();
  const selectedSource = useAppSelector((s) => s.dataSourceSelected);
  const filters = useAppSelector((s) => s.filterSelected);
  const isDebug = useAppSelector((s) => s.isDebug);

  const options: Option[] = React.useMemo(
    () => dataSources.map((ds) => ({ value: ds.name, label: ds.name })),
    [],
  );

  const selectedOptions: Option[] = React.useMemo(() => {
    const title = selectedSource?.title || '';
    if (!title) return [];
    const parts = title
      .split(' + ')
      .map((s) => s.trim())
      .filter(Boolean);
    return options.filter((o) => parts.includes(o.value));
  }, [selectedSource?.title, options]);

  // detect single-file mode (hide DDL and only load this file)
  const singleFileAttr = React.useMemo(
    () => (typeof document !== 'undefined' ? document.body.getAttribute('load-single-data') : null),
    [],
  );
  const hideDDL = !!singleFileAttr;

  // On mount: check for `load-default-data` attribute on <body> and auto-load the corresponding file
  React.useEffect(() => {
    try {
      const singleFile = typeof document !== 'undefined' ? document.body.getAttribute('load-single-data') : null;
      if (singleFile) {
        const picked = dataSources.filter((ds) => ds.file === singleFile);
        if (picked.length) {
          // hide DDL UI by dispatching selection or by setting store via loadPicked
          (async () => {
            await loadPicked(picked);
          })();
          return;
        }
      }

      const defaultFile = typeof document !== 'undefined' ? document.body.getAttribute('load-default-data') : null;
      if (!defaultFile) return;

      // find dataSources that match the filename
      const picked = dataSources.filter((ds) => ds.file === defaultFile);
      if (!picked.length) return;

      // if store already has this selection, skip
      const title = selectedSource?.title || '';
      if (title && title.includes(picked[0].name)) return;

      // auto-load
      (async () => {
        await loadPicked(picked);
      })();
    } catch (e) {
      // ignore in non-browser contexts
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleChange = async (
    sel: MultiValue<Option> | SingleValue<Option>,
  ) => {
    const arr = Array.isArray(sel) ? sel : sel ? [sel] : [];

    if (!arr.length) {
      dispatch(
        rootActions.updateState({
          dataSourceSelected: { results: [], title: '' },
          filterSelected: {
            ...filters,
            date: '',
            date_str: '',
          },
        }),
      );
      return;
    }

    const picked = arr
      .map((o) => byName[o.value] as DataSource | undefined)
      .filter((x): x is DataSource => Boolean(x));

    await loadPicked(picked);
  };

  // helper extracted from handleChange so we can also auto-load default dataset on mount
  const loadPicked = async (picked: DataSource[]) => {
    if (!picked.length) return;

    const combinedName = picked.map((p) => p.name).join(' + ');

    let datasets: Result[][];
    try {
      datasets = await Promise.all(picked.map((p) => p.load()));
    } catch (e) {
      console.error('Error loading data source', e);
      return;
    }

    const combinedData: Result[] = datasets.flat();

    const maxInfo = getMaxDateAndIso(combinedData);
    if (!maxInfo) {
      dispatch(
        rootActions.updateState({
          dataSourceSelected: { results: combinedData, title: combinedName },
        }),
      );
      return;
    }

    const { maxDate, iso } = maxInfo;

    dispatch(
      rootActions.updateState({
        dataSourceSelected: {
          results: combinedData,
          title: combinedName,
        },
        filterSelected: {
          ...filters,
          date: maxDate,
          date_str: iso,
        },
      }),
    );
  };

  if (hideDDL) return null;

  return (
    <div className="dv-data-source p-4 bg-gray-100 rounded-lg">
      {isDebug && (
        <div className="mb-2">
          <pre className="bg-gray-200 p-2 text-xs">
            Debug: {selectedSource?.title} / {selectedSource?.results?.length}
          </pre>
        </div>
      )}

      <div className="mb-2 flex flex-col lg:flex-row items-start gap-3">
        <div className="flex lg:flex-col items-center lg:items-start">
          <span className="text-lg font-bold">Select Data Source</span>
          <span className="pl-4 lg:pl-0 text-sm text-gray-600">
            {selectedSource?.results?.length || 0}
          </span>
        </div>

        <div className="min-w-[280px] w-full max-w-xl">
          <Select
            options={options}
            value={selectedOptions}
            onChange={handleChange}
            isMulti
            isClearable
            placeholder="Choose one or more data sources..."
            classNamePrefix="ds"
          />
        </div>
      </div>
    </div>
  );
};

export default DataSourceDDL;
