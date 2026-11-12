import { competitionTileData } from '../../../utils/helpers/competition-source';
import HelperSwimmer from '../../../utils/helpers/helper-swimmer';
import { printDistance, printEmblem, topMark } from '../print/print-data';
import { isMedal, styleLabel, swimMarks } from '../swim-marks';
import type { MySwimDto } from '../use-my-swims';
import { posterTime, type PosterSource, type PosterSwimSource } from './poster-data';

// Мост от заплывов карточки My media к данным плаката. Своих правил здесь нет: медаль и метки —
// общие `isMedal` / `swimMarks` (те же, что у строки списка и обложки), плитка —
// `competitionTileData`, значок или кубок и одна метка — `print/print-data.ts`. Что из этого
// печатать — чистые функции `poster-data.ts`.

function swimSource(swim: MySwimDto): PosterSwimSource {
  return {
    id: swim.result_id,
    date: swim.date,
    place: swim.place,
    medal: isMedal(swim),
    styleName: swim.style,
    distanceLabel: printDistance(swim.distance),
    isRelay: swim.is_relay,
    time: swim.time,
    failed: swim.time_fail,
    mark: topMark(swimMarks(swim)),
  };
}

/** Заплывы пловца `swimmerId` среди заплывов карточки (эстафета принадлежит всем её участникам). */
export function swimmerSwims(swims: MySwimDto[], swimmerId: number): MySwimDto[] {
  return swims.filter((s) => HelperSwimmer.resultBelongsToSwimmer(s, swimmerId));
}

/** Входы плаката: соревнование — по первому заплыву карточки, заплывы — пловца. */
export function posterSourceForSwims(
  swims: MySwimDto[], swimmerName: string, clubName: string | null, firstDay: string,
): PosterSource {
  const first = swims[0];
  return {
    competitionName: first.competition_name,
    tile: competitionTileData({
      name: first.competition_name,
      date: first.competition_date,
      category: first.category,
      is_championship: first.is_championship,
    }),
    emblem: printEmblem(swims.find((s) => s.emblem_url)?.emblem_url ?? null, first.is_championship),
    firstDay,
    swimmerName,
    clubName,
    swims: swims.map(swimSource),
  };
}

/** Подпись заплыва в списке галочек диалога: `50m breast · 49.61`. */
export function swimChoiceLabel(swim: PosterSwimSource): string {
  return `${swim.distanceLabel} ${styleLabel(swim.styleName)} · ${posterTime(swim.time)}`;
}
