export type TrainingTableMode = 'bySession' | 'groupByName' | 'groupBySet' | 'showTable';
export type ActivityType = 'training' | 'competition';
export interface FilterSelected {
  selected_name: string;
  date: string;
  date_str: string
  pool_type: string;
  age: string;
  age_to?: string;
  club: string;
  gender: string;
  style_name: string;
  /** Дистанция дисциплины. Строка — не поблажка, а факт: `filter_data` отдаёт дистанции
   *  строками, включая эстафетные «4X50», и стор всегда хранил именно их. Тип, обещавший
   *  только число, эту правду скрывал; `Number('4X50')` даёт NaN. */
  style_len: number | string;
   training_table: {
    mode: TrainingTableMode; // 'groupByName' | 'groupBySet | 'showTable'
  };
  // Optional rating mode used by the training filters (no/regular/masters)
  rating_mode?: 'no' |  'masters';
  // Activity type: training or competition
  activity_type?: ActivityType;
  // Position filter: 'all' | 'top' | 'podium'
  position_filter?: 'all' | 'top' | 'podium';
  // Level filter: 'all' or a specific normative level name (e.g. 'I_youth', 'KMS')
  level_filter?: string;
  // Event date filter: 'all' or a specific date string (e.g. '15/02/2026')
  event_date?: string;
  /**
   * Категория (программа) заплыва: 'all' или значение event_category результата
   * ('open' | 'para' | 'mix-18-99' | '17' | '25-29'…).
   *
   * Нужен потому, что в одной дисциплине бывает несколько первых мест — это разные
   * программы одного соревнования (у Маккабиады 50 вольным у мужчин: «Men», «U17 Boys»,
   * «Men Para»). Фильтр даёт посмотреть зачёт одной программы.
   */
  event_category?: string;
  /** Показывать предварительные заплывы (heat_type === 'prelim'). По умолчанию скрыты:
   *  официальный вид — финалы; тумблер живёт в фильтре Date. */
  show_prelims?: boolean;
  // Recalculate positions across all days (best time per swimmer per event)
  is_recalculated?: boolean;
  /**
   * Диплинк «заплывы одного пловца в этом протоколе» (`?swimmerId=` в URL,
   * `routes.competitionSwims`). От `swimmer_scope` отличается тем, что пловец задан явно
   * и потому работает и гостю: скоуп берёт своих/избранных, а этот — того, на кого
   * привели со страницы пловца или из H2H. Матчинг — `HelperSwimmer.resultBelongsToSwimmer`
   * (эстафеты попадают по составу ног). null/undefined — фильтр выключен.
   */
  swimmer_id?: number | null;
  // Scope by viewer's swimmers (?filter= в URL, персональная полоса шапки соревнования):
  // 'my' — primary favorite, 'favorites' — все избранные. Только залогиненному.
  swimmer_scope?: 'all' | 'my' | 'favorites';
}
