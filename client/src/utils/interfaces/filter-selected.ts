export type TrainingTableMode = 'groupByName' | 'groupBySet' | 'showTable';
export interface FilterSelected {
  selected_name: string;
  date: string;
  date_str: string
  pool_type: string;
  age: string;
  club: string;
  gender: string;
  style_name: string;
  style_len:number;
   training_table: {
    mode: TrainingTableMode; // 'groupByName' | 'groupBySet | 'showTable'
  };
  // Optional rating mode used by the training filters (no/regular/masters)
  rating_mode?: 'no' |  'masters';
}
