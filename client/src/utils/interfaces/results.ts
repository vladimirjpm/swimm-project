export interface ResultWrap {
    title?:string;
    results?: Result[];
    is_masters?: boolean;
    is_award?: boolean;
    /** paged-режим: { eventId } | { competitionId } источника — нужен для Show more и рефетча по фильтрам. */
    sourceParams?: Record<string, string>;
    /** paged-режим: даты дней источника (dd/MM/yyyy, по возрастанию) — опции фильтра по дню. */
    day_dates?: string[];
  }
  
  export interface Result {
    /** DB id заплыва — есть только у данных с /api/results, на статике отсутствует. */
    id?: number;
    /*group data*/
    country: string;
    competition: string;
    is_masters?:boolean;
    is_award?:boolean;
    /** per-competition: показывать объединённую таблицу (с серверного /api/results) */
    show_combine_all_results?: boolean;
    age_group: string;
    date: string;
    /* многодневные соревнования (с серверного /api/results; на статике отсутствуют) */
    event_id?: number | null;
    event_name?: string | null;
    day_number?: number | null;
    sub_name?: string | null;
    event: string;
    event_style_name: string;
    event_style_len: string;
    event_style_gender: string;
    event_style_age: string;
    /**
     * Категория заплыва из протокола: 'open' | 'para' | 'mix' | возрастная ('17', '25-29').
     * null/undefined — данные импортированы до появления поля.
     *
     * В отличие от event_style_age НЕ производна от года рождения: в открытом заплыве
     * плывут разные возрасты, а паралимпийская программа идёт отдельно от основной.
     */
    event_category?: string | null;
    pool_type: '25' | '50' | '25m' | '50m';
    /*individual data*/
    swimmer_id?: number;
    position: number | null;
    position_age_group: number | null;
    heat: number;
    lane: number;
    last_name: string;
    first_name: string;    
    last_name_en: string;
    first_name_en: string;
    birth_year: number;
    /** Id клуба — для ссылки на /clubs/{id}. Опционален: у статических источников его нет. */
    club_id?: number;
    club: string;
    club_en: string;
    time: string;
    time_split: string;
    time_fail: boolean;
    time_fail_note: string | null;
    international_points: number;
    note?:string;

    /**
     * Клубные очки за заплыв — считает СЕРВЕР по правилу соревнования (Э6).
     * Клиент их только показывает: он не видит привязку соревнования к правилу и,
     * подбирая правило по дате, расходился с зачётом. Может отсутствовать у старых
     * статических источников — тогда работает локальный фоллбек (ClubPointsHelper).
     */
    club_points?: number;

    /** Клубные очки по объединённому месту дисциплины («Combine All Results»).
     *  null/undefined — соревнование объединённый зачёт не считает. */
    combined_club_points?: number | null;

    /** Место в объединённом зачёте дисциплины по всему событию (считает сервер). */
    combined_place?: number | null;

    /** Протокольное место, сохранённое при включённом тоггле «Combine All Results»:
     *  в position тогда лежит объединённое (см. applyCombinedPositions). */
    position_original?: number | null;

    /** Этот заплыв — лучший у пловца в дисциплине за событие (считает сервер). */
    is_best_result?: boolean | null;

    /* training-specific data */
    training?: TrainingInfo;

     /* === ДОБАВЛЕНО ДЛЯ RELAY (опционально) === */

    /** если true — это эстафета */
    is_relay?: boolean;
    
    /** название команды / клуба */
    relay_team_name?: string;
    /** имена пловцов клуба */
    relay_swimmers_name?: string;
    /** состав ног (RelayMembers) — матчить пловца к эстафете по нему, не по swimmer_id
     *  владельца строки (docs/relays.md); null/отсутствует у личных заплывов */
    member_swimmer_ids?: number[] | null;

    /** состав команды */
    relay_swimmers?: RelaySwimmer[];

    /* === ДОБАВЛЕНО ДЛЯ ГАЛЕРЕИ (опционально) === */
    gallery?: GalleryItem[];
  }
  export interface RelaySwimmer {
  order: number; // 1..4
  last_name: string;
  first_name: string;
  birth_year?: number;
  club?: string;
  split_time?: string;
}
export interface GalleryItem {
  type: 'image' | 'video';

  sourceType?: 'youtube' | 'vimeo' | 'other';
  url?: string;
  code?: string;  
}

// Канонический wire-контракт медиа группы/тренировки (HubGroupMediaDto, snake_case).
// Единственное объявление — hub-groups-project/types.ts и хук управления его реиспользуют.
export interface HubGroupMediaItem {
  id: number;
  media_type: 'image' | 'video' | 'album';
  source_type: 'youtube' | 'vimeo' | 'album' | 'other';
  url: string;
  caption?: string | null;
}

/** Алиас для контекста тренировок (TrainingInfo.media) — форма та же. */
export type TrainingMediaItem = HubGroupMediaItem;

  export interface TrainingInfo {
  trainingId: number;            // уникальный ID тренировки
  sessionId?: number;             // настоящий PK Sys_TrainingSessions.Id (для медиа тренировки)
  trainingName: string;
  set: number;                   // номер сета
  order: number;                 // порядок в сете
  interval?: number;             // интервал (секунд)
  intensity?: 'v1' | 'v2' | 'v3' | 'v4' | 'v5'| 'S'; // уровень интенсивности
  expected_time?: string;        // ожидаемое время

  /* biometrics (optional) */
  hrStart?: number;              // пульс в начале (bpm)
  hrEnd?: number;                // пульс в конце (bpm)

  isFins: boolean;               // использовались ласты
  isSnorkel: boolean;            // использовалась трубка
  isPaddles: boolean;            // использовались лопатки
  isBuoy: boolean;               // использовалась колобашка
  isBoard: boolean;              // использовалась доска

  /** Медиа этой тренировочной сессии — одинаково у всех строк одной тренировки. */
  media?: TrainingMediaItem[];
}

export type TrainingGroup = {
  title: string;       // заголовок группы (имя или "Set X" или "All results")
  date: string;        // дата группы (может быть '')
  items: Result[];     // элементы в группе
  // опционально мета:
  name?: string;       // имя спортсмена (для groupByName)
  set?: number;        // номер сета (для groupBySet)
};
