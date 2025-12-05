export interface PDF_CompetitionResult {
    competition: string;
    age_group: string;
    date: string;
    event: string;
    event_style_name: string;
    event_style_len: string;
    event_style_gender: string;
    event_style_age: string;
    pool_type: string;
    results: PDF_Result[];
  }
  
  export interface PDF_Result {
    country: string;
    position: number | string | null;
    heat: number;
    lane: number;
    last_name: string;
    first_name: string;
    birth_year: number;
    club: string;
    time: string;
    international_points: number;
  }