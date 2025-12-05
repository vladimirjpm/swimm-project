export interface FilterData {
  pool_type: string[];
  gender: string[];
  style_list: SwimmingStyle[];
}

export interface SwimmingStyle {
  style_name: string;
  style_len: number[];
}
