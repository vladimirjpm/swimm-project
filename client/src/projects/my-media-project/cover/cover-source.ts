import HelperResults from '../../../utils/helpers/helper-results';
import { competitionTileData } from '../../../utils/helpers/competition-source';
import type { MySwimDto } from '../use-my-swims';
import { isMedal, styleLabel, swimMarks } from '../swim-marks';
import { printDistance, printEmblem, topMark } from '../print/print-data';
import { buildCoverData, coverFileName, type CoverData, type CoverLayout } from './cover-data';

// Мост от заплыва My media к данным обложки. Своих правил здесь нет: медаль и метки — общие
// `isMedal` / `swimMarks` (те же, что у строки списка), плитка — `competitionTileData`, пометка
// снятого — `HelperResults.failLabel`. Значок или кубок, одна метка и
// дистанция — общие с плакатом `print/print-data.ts`; что из этого печатать — чистая `buildCoverData`.

export function coverDataForSwim(
  swim: MySwimDto, swimmerName: string, firstDay: string, lastDay: string,
): CoverData {
  return buildCoverData({
    competitionName: swim.competition_name,
    tile: competitionTileData({
      name: swim.competition_name,
      date: swim.competition_date,
      category: swim.category,
      is_championship: swim.is_championship,
    }),
    emblem: printEmblem(swim.emblem_url ?? null, swim.is_championship),
    date: swim.date,
    firstDay,
    lastDay,
    place: swim.place,
    medal: isMedal(swim),
    styleName: swim.style,
    distanceLabel: printDistance(swim.distance),
    time: swim.time,
    failLabel: swim.time_fail ? String(HelperResults.failLabel(swim)) : null,
    mark: topMark(swimMarks(swim)),
    swimmerName,
    clubName: swim.club_name,
  });
}

export function coverFileNameForSwim(swim: MySwimDto, layout: CoverLayout): string {
  return coverFileName(swim.date, swim.distance, styleLabel(swim.style), layout);
}
