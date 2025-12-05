import { PDF_CompetitionResult } from '../interfaces/pdf-results';
import { Result } from '../interfaces/results';

export default class HelperConvert {
  static convertPDFResultsToResults(pdfData: PDF_CompetitionResult[]): Result[] {
    return pdfData.flatMap((entry) => {
      const [day, month, year] = entry.date.split('/');
      const formattedDate = `${day.padStart(2, '0')}/${month.padStart(
        2,
        '0',
      )}/${year}`;
      const pool_type = entry.pool_type.replace('m', '');

      return entry.results.map((result) => {
        const containsLetters = /[a-zA-Z]/.test(result.time);
        const time_fail = containsLetters;
        const time = time_fail ? '' : result.time;

        return {
          /* group data */
          competition: entry.competition,
          age_group: entry.age_group,
          date: formattedDate,
          event: entry.event,
          event_style_name:
            entry.event_style_name.toLowerCase() === 'individual medley' ||
            entry.event_style_name.toLowerCase() === 'medley'
              ? 'individual_medley'
              : entry.event_style_name.toLowerCase(),
          event_style_len: entry.event_style_len,
          event_style_gender:
            entry.event_style_gender.toLowerCase() === 'female'
              ? 'female'
              : entry.event_style_gender.toLowerCase() === 'male'
              ? 'male'
              : entry.event_style_gender.toLowerCase(),
          event_style_age: entry.event_style_age,
          pool_type,

          /* individual data */
          country: result.country,
          position:
            typeof result.position === 'string'
              ? parseInt(result.position, 10)
              : result.position ?? null,
          heat: result.heat ?? 0,
          lane: result.lane ?? 0,
          last_name: result.last_name ?? '',
          first_name: result.first_name ?? '',

          last_name_en: result.last_name ?? '',
          first_name_en: result.first_name ?? '',
          club_en: result.club ?? '',

          birth_year: result.birth_year,

          club: result.club,
          time,
          time_fail,
          international_points: result.international_points ?? 0,
        } as Result;
      });
    });
  }
}
