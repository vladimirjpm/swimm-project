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

type SourceConfig = {
  name: string;
  file: string;
};

type Option = { value: string; label: string };

const makeUrl = (file: string) =>
  `${import.meta.env.BASE_URL}data/${file}`;

// All possible data sources (internal registry)
const allDataSources: DataSource[] = [
  {
    name: 'Dolphin Training',
    file: 'json/dolphin_data.json',
    load: async () => {
      const resp = await fetch(makeUrl('json/dolphin_data.json'));
      if (!resp.ok) {
        throw new Error('Failed to load dolphin_data.json');
      }
      const data = (await resp.json()) as Result[];
      return data;
    },
  },
  {
    name: 'Masters Weizgal Rehovot Dec 2025',
    file: 'json/masters-weizgal-rehovot-12-99-dec-2025.json',
    load: async () => {
      const resp = await fetch(
        makeUrl('json/masters-weizgal-rehovot-12-99-dec-2025.json'),
      );
      if (!resp.ok) {
        throw new Error(
          'Failed to load masters-weizgal-rehovot-12-99-dec-2025.json',
        );
      }
      const data = (await resp.json()) as Result[];
      return data;
    },
  },
  {
    name: 'Masters Arena Horef Jan 2026',
    file: 'json/masters-arena-horef-21-99-jan-2026.json',
    load: async () => {
      const resp = await fetch(
        makeUrl('json/masters-arena-horef-21-99-jan-2026.json'),
      );
      if (!resp.ok) {
        throw new Error(
          'Failed to load masters-arena-horef-21-99-jan-2026.json',
        );
      }
      const data = (await resp.json()) as Result[];
      return data;
    },
  },
  {
    name: 'Dolphin Masters',
    file: 'json/dolphin_masters_data.json',
    load: async () => {
      const resp = await fetch(makeUrl('json/dolphin_masters_data.json'));
      if (!resp.ok) {
        throw new Error('Failed to load dolphin_masters_data.json');
      }
      const data = (await resp.json()) as Result[];
      return data;
    },
  },
  {
    name: '2024 Isr Champ',
    file: 'json/competition-2024-summer-isr-championship.json',
    load: async () => {
      const resp = await fetch(
        makeUrl('json/competition-2024-summer-isr-championship.json'),
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
    file: 'json/competition-2025-hapoel-young-isr-championship.json',
    load: async () => {
      const resp = await fetch(
        makeUrl('json/competition-2025-hapoel-young-isr-championship.json'),
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
    file: 'json/competition-2025-summer-isr-championship-age_9-11.json',
    load: async () => {
      const resp = await fetch(
        makeUrl('json/competition-2025-summer-isr-championship-age_9-11.json'),
      );
      if (!resp.ok) {
        throw new Error(
          'Failed to load competition-2025-summer-isr-championship-age_9-11.json',
        );
      }
      const data = (await resp.json()) as Result[];
      return data;
    },
  },
  {
    name: '2025 Liga3 Weisgel 9-13',
    file: 'json/competition-2025-liga3-weisgel-rehovot-age_9-13.json',
    load: async () => {
      const resp = await fetch(
        makeUrl('json/competition-2025-liga3-weisgel-rehovot-age_9-13.json'),
      );
      if (!resp.ok) {
        throw new Error(
          'Failed to load competition-2025-liga3-weisgel-rehovot-age_9-13.json',
        );
      }
      const data = (await resp.json()) as Result[];
      return data;
    },
  },
  {
    name: '2025 Horef Isr Champ 14-17',
    file: 'json/competition-2025-horef-isr-championship-age_14-17.json',
    load: async () => {
      const resp = await fetch(
        makeUrl('json/competition-2025-horef-isr-championship-age_14-17.json'),
      );
      if (!resp.ok) {
        throw new Error(
          'Failed to load competition-2025-horef-isr-championship-age_14-17.json',
        );
      }
      const data = (await resp.json()) as Result[];
      return data;
    },
  },
  {
    name: '2025 Isr 11-13 Champ 4 day',
    file: 'json/competition-2025-summer-isr-championship-age_11-13.json',
    load: async () => {
      const resp = await fetch(
        makeUrl('json/competition-2025-summer-isr-championship-age_11-13.json'),
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
];

// Map by file for quick lookup
const allDataSourcesByFile: Record<string, DataSource> = Object.fromEntries(
  allDataSources.filter(ds => ds.file).map((ds) => [ds.file, ds]),
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

  // State for available sources loaded from config
  const [dataSources, setDataSources] = React.useState<DataSource[]>([]);
  const [configLoaded, setConfigLoaded] = React.useState(false);

  // Load sources-config.json on mount
  React.useEffect(() => {
    const loadConfig = async () => {
      try {
        // Check for custom config file from body attribute
        const customConfigFile = typeof document !== 'undefined'
          ? document.body.getAttribute('data-sources-config')
          : null;

        const normalizeConfigPath = (value: string) =>
          value.includes('/') ? value : `config/${value}`;

        const configFile = customConfigFile
          ? normalizeConfigPath(customConfigFile)
          : 'config/sources-config.json';
        const resp = await fetch(makeUrl(configFile));
        
        if (!resp.ok) {
          // If no config found, use all sources
          console.warn(`${configFile} not found, using all sources`);
          setDataSources(allDataSources);
          setConfigLoaded(true);
          return;
        }
        const config = await resp.json() as { sources: SourceConfig[] };
        
        // Filter allDataSources by config
        const allowedFiles = new Set(config.sources.map(s => s.file));
        const filtered = allDataSources.filter(ds => ds.file && allowedFiles.has(ds.file));
        
        // Sort by config order
        const fileOrder = config.sources.map(s => s.file);
        filtered.sort((a, b) => fileOrder.indexOf(a.file!) - fileOrder.indexOf(b.file!));
        
        setDataSources(filtered);
        setConfigLoaded(true);
      } catch (e) {
        console.error('Error loading sources config', e);
        setDataSources(allDataSources);
        setConfigLoaded(true);
      }
    };
    loadConfig();
  }, []);

  // Map by name for selection lookup
  const byName: Record<string, DataSource> = React.useMemo(
    () => Object.fromEntries(dataSources.map((ds) => [ds.name, ds])),
    [dataSources]
  );

  const options: Option[] = React.useMemo(
    () => dataSources.map((ds) => ({ value: ds.name, label: ds.name })),
    [dataSources],
  );

  const selectedOptions: Option[] = React.useMemo(() => {
    const title = selectedSource?.title || '';
    if (!title) return [];
    const parts = title
      .split(' + ')
      .map((s) => s.trim())
      .filter(Boolean);
    return options.filter((o) => parts.includes(o.value.trim()));
  }, [selectedSource?.title, options]);

  // detect single-file mode (hide DDL and only load this file)
  const singleFileAttr = React.useMemo(
    () => (typeof document !== 'undefined' ? document.body.getAttribute('load-single-data') : null),
    [],
  );
  const hideDDL = !!singleFileAttr;

  // detect default-file attribute for debugging
  const defaultFileAttr = React.useMemo(
    () => (typeof document !== 'undefined' ? document.body.getAttribute('load-default-data') : null),
    [],
  );

  // On mount: check for `load-default-data` attribute on <body> and auto-load the corresponding file
  React.useEffect(() => {
    // Wait for config to load before auto-loading
    if (!configLoaded) return;
    
    try {
      const singleFile = typeof document !== 'undefined' ? document.body.getAttribute('load-single-data') : null;
      if (singleFile) {
        // Use allDataSourcesByFile to allow loading any file, not just from config
        const ds = allDataSourcesByFile[singleFile];
        if (ds) {
          (async () => {
            await loadPicked([ds]);
          })();
          return;
        }
      }

      const defaultFile = typeof document !== 'undefined' ? document.body.getAttribute('load-default-data') : null;
      if (!defaultFile) return;

      // find dataSources that match the filename (from config-filtered list)
      const picked = dataSources.filter((ds) => ds.file === defaultFile);
      if (!picked.length) {
        // Fallback to all sources if not in config
        const ds = allDataSourcesByFile[defaultFile];
        if (ds) {
          (async () => {
            await loadPicked([ds]);
          })();
        }
        return;
      }

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
  }, [configLoaded, dataSources]);

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
            {'\n'}defaultFile: {defaultFileAttr || '(not set)'}
            {'\n'}singleFile: {singleFileAttr || '(not set)'}
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
