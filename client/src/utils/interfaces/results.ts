export interface ResultWrap {
    title?:string;
    results?: Result[];
  }
  
  export interface Result {
    /*group data*/
    country: string;
    competition: string;
    is_masters?:boolean;
    age_group: string;
    date: string;
    event: string;
    event_style_name: string;
    event_style_len: string;
    event_style_gender: string;
    event_style_age: string;
    pool_type: '25' | '50';
    /*individual data*/
    position: number | null;
    heat: number;
    lane: number;
    last_name: string;
    first_name: string;    
    last_name_en: string;
    first_name_en: string;
    birth_year: number;
    club: string;
    club_en: string;
    time: string;
    time_fail: boolean;
    international_points: number;
    note?:string;

    /* training-specific data */
    training?: TrainingInfo;
  }

  export interface TrainingInfo {
  trainingId: number;            // уникальный ID тренировки
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
}

export type TrainingGroup = {
  title: string;       // заголовок группы (имя или "Set X" или "All results")
  date: string;        // дата группы (может быть '')
  items: Result[];     // элементы в группе
  // опционально мета:
  name?: string;       // имя спортсмена (для groupByName)
  set?: number;        // номер сета (для groupBySet)
};
