export type TrainingTableMode = 'groupByName' | 'groupBySet' | 'showTable';
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
  style_len:number;
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
}
