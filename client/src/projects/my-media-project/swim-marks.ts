import Helper from '../../utils/helpers/data-helper';
import HelperResults from '../../utils/helpers/helper-results';
import type { MySwimDto } from './use-my-swims';

// Выводы о заплыве My media: медаль, метки достижений, короткая подпись стиля. Вынесены из
// `components/swim-list.tsx` (12.11.2026), когда они понадобились второму месту — обложке
// YouTube (`cover/`): строка списка, панель медиа и обложка обязаны сказать про один заплыв
// одно и то же, поэтому считают здесь, а не каждый у себя.

/**
 * Медаль рисуется НЕ из места: место есть и у предварительного заплыва, и у секции «כללי»,
 * а медалей за них не дают. Правило одно на продукт — `HelperResults.isMedalPlace`, тот же,
 * что у таблицы результатов и страницы пловца.
 */
export function isMedal(swim: MySwimDto): boolean {
  return HelperResults.isMedalPlace({
    place: swim.place,
    heatType: swim.heat_type,
    round: swim.round,
    timeFail: swim.time_fail,
    competitionIsAward: swim.is_award,
  });
}

/**
 * Выводы из времени заплыва — общие для строки, панели и обложки, чтобы метка в них не разошлась.
 *
 * Спорное время (И11): чип рисует `UI_SwimTime`, а НОСИТЕЛЬ — сама строка
 * (`swimFlaggedRowProps`: caution-лента слева плюс полный текст в title/aria-label).
 * Р76: PB/SB/рекорд — наши выводы из времени, у помеченной строки их нет. SB сервер уже не
 * ставит, PB ставит — гасим здесь. Бейдж рекорда — тем же способом, что в протоколе:
 * справочник рекордов + ось возраста из админ-настройки (`Helper.recordStepAge`). Эстафеты,
 * DSQ и помеченные ошибки протокола рекордов не носят — как в таблице результатов.
 */
export function swimMarks(swim: MySwimDto) {
  const quality = swim.suspect_reason ? { kind: 'protocol' as const, reason: swim.suspect_reason } : null;
  const isMastersResult = Helper.isResultMasters(swim.category === 'masters', swim.event_style_age);
  const record = swim.is_relay || swim.time_fail || swim.suspect_reason || !swim.gender
    ? null
    : Helper.recordMarkForTime({
      time: swim.time,
      gender: swim.gender,
      poolType: swim.pool_type,
      styleName: swim.style,
      distance: `${swim.distance}m`,
      age: Helper.recordStepAge({
        date: swim.competition_date,
        birth_year: swim.birth_year,
        event_style_age: swim.event_style_age,
      }),
      isMasters: isMastersResult,
    });
  return {
    quality,
    record,
    pb: swim.is_pb && !swim.suspect_reason,
    sb: swim.is_sb && !swim.suspect_reason,
  };
}

export type SwimMarks = ReturnType<typeof swimMarks>;

/** Style.Name из БД сырой (freestyle / individual_medley) — короткие лейблы дизайна. */
const STYLE_LABELS: Record<string, string> = {
  freestyle: 'free', backstroke: 'back', breaststroke: 'breast',
  butterfly: 'fly', individual_medley: 'IM', medley: 'IM',
};
export function styleLabel(style: string): string {
  return STYLE_LABELS[style.toLowerCase()] ?? style.replace(/_/g, ' ');
}
