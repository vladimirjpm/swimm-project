import React from 'react';
import type { TrainingRsvp } from '../../../types';
import { useTrainingsTab } from '../trainings-context';
import { SegmentBar } from '../trainings-segments';
import { previewLaneNo } from './lane-preview';

/**
 * Ряд дорожек режима «👁 View» управляющего (решение Влада 15.11.2026): выбрал дорожку — ниже экран
 * пловца этой дорожки («Your lane», его workout со временами уровня). Вид — общий `SegmentBar`
 * (тот же, что «Adv · Int · Dev · Beg» в превью workout). Участнику и без дорожек — ничего.
 */
function LanePreviewPicker({ r }: { r: TrainingRsvp }) {
  const { lanePreview, previewLane, setPreviewLane } = useTrainingsTab();
  const view = r.lane_view;
  if (!lanePreview || !view || view.source === 'water' || view.lanes.length === 0) return null;
  const current = previewLaneNo(view.lanes, previewLane);
  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <div className="text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--t-text-3)' }}>
        Viewing as a swimmer of lane
      </div>
      <SegmentBar
        label="Lane to view as"
        coach
        value={String(current)}
        onChange={(no) => setPreviewLane(Number(no))}
        options={view.lanes.map((l) => ({ id: String(l.lane_no), label: String(l.lane_no) }))}
      />
    </div>
  );
}

export default LanePreviewPicker;
