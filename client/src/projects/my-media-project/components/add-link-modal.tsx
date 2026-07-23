import React, { useEffect, useState } from 'react';
import {
  AddMediaInput,
  fetchSwimmerCompetitionsBrief,
  fetchSwimmerResultsBrief,
  SwimmerCompetitionBrief,
  SwimmerResultBrief,
} from '../use-all-my-media';
import { chipClass, segmentClass } from './status-styles';

export interface AddLinkSwimmerOption {
  id: number;
  name: string;
  hint: string;
}

interface Props {
  swimmers: AddLinkSwimmerOption[];
  onClose: () => void;
  onSave: (input: AddMediaInput) => Promise<boolean>;
  /** «Link to a swim» на существующей карточке: URL уже известен, пловец один — стартуем сразу с шага 3. */
  initialUrl?: string;
  initialStep?: 1 | 2 | 3;
  initialSwimmerId?: number;
  /**
   * Single-step режим (v3): цель уже известна («+ Add video» на строке заплыва /
   * «+ Photo/Video» на соревновании) — только URL, шаги 2–3 пропускаются.
   * fixedResultId имеет приоритет; contextLabel — карточка контекста над инпутом.
   */
  fixedResultId?: number;
  fixedCompetitionId?: number;
  contextLabel?: React.ReactNode;
}

type Detected = 'YOUTUBE' | 'VIMEO' | 'PHOTO' | 'OTHER';

function detectType(url: string): Detected {
  if (/youtu\.?be/i.test(url)) return 'YOUTUBE';
  if (/vimeo/i.test(url)) return 'VIMEO';
  if (/\.(jpe?g|png|webp|gif|heic)(\?|#|$)/i.test(url) || /imgur|googleusercontent|photos\.app/i.test(url)) return 'PHOTO';
  return 'OTHER';
}

/** Add link — модал (desktop)/bottom sheet (mobile), 3 шага (README §3). */
function AddLinkModal({
  swimmers, onClose, onSave, initialUrl, initialStep, initialSwimmerId,
  fixedResultId, fixedCompetitionId, contextLabel,
}: Props) {
  const singleStep = fixedResultId != null || fixedCompetitionId != null;
  const [step, setStep] = useState<1 | 2 | 3>(initialStep ?? 1);
  const [url, setUrl] = useState(initialUrl ?? '');
  const [otherKind, setOtherKind] = useState<'video' | 'photo'>('video');
  const [swimmerId, setSwimmerId] = useState<number | null>(initialSwimmerId ?? null);
  const [competitions, setCompetitions] = useState<SwimmerCompetitionBrief[]>([]);
  const [competitionId, setCompetitionId] = useState<number | null>(null);
  const [results, setResults] = useState<SwimmerResultBrief[]>([]);
  const [resultId, setResultId] = useState<number | null>(null);
  const [saving, setSaving] = useState(false);

  const detected = detectType(url.trim());
  const isOther = detected === 'OTHER';
  const kind: 'video' | 'photo' = detected === 'PHOTO' ? 'photo' : isOther ? otherKind : 'video';

  useEffect(() => {
    if (step !== 3 || swimmerId == null) return;
    let alive = true;
    fetchSwimmerCompetitionsBrief(swimmerId).then((list) => { if (alive) setCompetitions(list); });
    return () => { alive = false; };
  }, [step, swimmerId]);

  useEffect(() => {
    if (competitionId == null || swimmerId == null) { setResults([]); return; }
    let alive = true;
    fetchSwimmerResultsBrief(swimmerId, competitionId).then((list) => { if (alive) setResults(list); });
    return () => { alive = false; };
  }, [competitionId, swimmerId]);

  const mediaTypeSourceType = (): { media_type: 'image' | 'video'; source_type: 'youtube' | 'vimeo' | 'other' } => {
    if (detected === 'YOUTUBE') return { media_type: 'video', source_type: 'youtube' };
    if (detected === 'VIMEO') return { media_type: 'video', source_type: 'vimeo' };
    return { media_type: kind === 'photo' ? 'image' : 'video', source_type: 'other' };
  };

  const save = async (attach: 'result' | 'competition' | 'none') => {
    if (swimmerId == null || saving) return;
    setSaving(true);
    const { media_type, source_type } = mediaTypeSourceType();
    const ok = await onSave({
      swimmer_id: swimmerId,
      media_type,
      source_type,
      url: url.trim(),
      result_id: singleStep ? fixedResultId ?? null : attach === 'result' ? resultId : null,
      competition_id: singleStep
        ? (fixedResultId != null ? null : fixedCompetitionId ?? null)
        : attach === 'competition' ? competitionId : null,
    });
    setSaving(false);
    if (ok) onClose();
  };

  const canNext = step === 1 ? url.trim().length > 0 : step === 2 ? swimmerId != null : true;

  const stepDots = [1, 2, 3].map((n) => (
    <span
      key={n}
      className="inline-block h-[7px] rounded-[4px] transition-all"
      style={{ width: n === step ? 18 : 7, background: n <= step ? '#7dd3fc' : 'rgba(125,211,252,0.25)' }}
    />
  ));

  return (
    <div
      className="fixed inset-0 z-[100] flex items-center justify-center bg-[rgba(2,10,24,0.72)] backdrop-blur-[4px] sm:items-center"
      onClick={onClose}
    >
      <div
        className="max-h-[calc(100vh-60px)] w-[540px] max-w-[calc(100vw-40px)] overflow-y-auto rounded-[20px] border border-[rgba(125,211,252,0.3)] bg-[linear-gradient(180deg,#0e2138,#081527)] p-[22px_24px] font-sans text-[#f3f8fd] shadow-[0_40px_90px_rgba(0,0,0,0.6)]"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center gap-3">
          <h2 className="m-0 text-[19px] font-black tracking-[-0.02em]">{singleStep ? 'Add media' : 'Add link'}</h2>
          {!singleStep && <div className="ml-1.5 flex gap-[5px]">{stepDots}</div>}
          {!singleStep && <span className="hp-mono ml-auto text-[11px] font-extrabold text-[rgba(125,211,252,0.6)]">step {step} / 3</span>}
          <button type="button" onClick={onClose} className={`cursor-pointer border-none bg-transparent p-1 text-[16px] text-[rgba(203,224,240,0.6)] ${singleStep ? 'ml-auto' : ''}`}>✕</button>
        </div>

        {singleStep && contextLabel && (
          <div className="mt-3 rounded-[12px] border border-[rgba(125,211,252,0.25)] bg-[rgba(2,10,24,0.35)] p-[10px_14px] text-[13px]">
            {contextLabel}
          </div>
        )}

        {step === 1 && (
          <div className="mt-[18px] flex flex-col gap-[14px]">
            <label className="text-[12px] font-extrabold uppercase tracking-[0.14em] text-[#7dd3fc]">Paste a link</label>
            <input
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://www.youtube.com/watch?v=…"
              className="hp-mono w-full rounded-[10px] border border-[rgba(125,211,252,0.35)] bg-[rgba(2,10,24,0.5)] p-[12px_14px] text-[13px] text-[#f3f8fd] outline-none"
            />
            {url.trim().length > 0 && (
              <>
                <div className="flex items-center gap-[10px]">
                  <span className="hp-mono whitespace-nowrap rounded-[6px] border border-[rgba(56,239,143,0.45)] bg-[rgba(56,239,143,0.08)] px-[9px] py-[3px] text-[10.5px] font-extrabold text-[#38ef8f]">
                    detected · {detected}
                  </span>
                  {isOther && (
                    <div className="inline-flex overflow-hidden rounded-[8px] border border-[rgba(125,211,252,0.35)]">
                      <button type="button" onClick={() => setOtherKind('video')} className={segmentClass(otherKind === 'video', false)}>Video</button>
                      <button type="button" onClick={() => setOtherKind('photo')} className={segmentClass(otherKind === 'photo', true)}>Photo</button>
                    </div>
                  )}
                </div>
                <div className="flex aspect-video items-center justify-center rounded-[12px] border border-[rgba(125,211,252,0.2)] bg-[linear-gradient(140deg,#12314f,#0a1c33)]">
                  {kind === 'photo' && /^https?:\/\//i.test(url) ? (
                    <img src={url} alt="" className="h-full w-full rounded-[12px] object-cover" />
                  ) : (
                    <span className="flex h-12 w-12 items-center justify-center rounded-full border border-[rgba(125,211,252,0.4)] bg-[rgba(2,10,24,0.65)] text-[16px] text-[#7dd3fc]">
                      {kind === 'photo' ? '🖼' : '▶'}
                    </span>
                  )}
                </div>
              </>
            )}
            <div className="mt-1 flex justify-end gap-2">
              <button type="button" onClick={onClose} className="hp-mono rounded-[10px] border border-[rgba(125,211,252,0.3)] bg-transparent px-4 py-[9px] text-[12px] font-extrabold text-[rgba(125,211,252,0.7)]">Cancel</button>
              <button
                type="button"
                disabled={!canNext || (singleStep && saving)}
                onClick={() => (singleStep ? save('none') : setStep(2))}
                className="hp-mono rounded-[10px] border-none px-[18px] py-[9px] text-[12px] font-extrabold disabled:cursor-default"
                style={{ background: canNext ? '#38ef8f' : 'rgba(56,239,143,0.25)', color: canNext ? '#04101f' : 'rgba(4,16,31,0.6)' }}
              >
                {singleStep ? 'Save' : 'Next →'}
              </button>
            </div>
          </div>
        )}

        {step === 2 && (
          <div className="mt-[18px] flex flex-col gap-[14px]">
            <label className="text-[12px] font-extrabold uppercase tracking-[0.14em] text-[#7dd3fc]">Whose swim is it?</label>
            <div className="flex flex-col gap-2">
              {swimmers.map((s) => {
                const active = swimmerId === s.id;
                return (
                  <button
                    key={s.id}
                    type="button"
                    onClick={() => setSwimmerId(s.id)}
                    className="flex min-h-[48px] w-full items-center gap-3 rounded-[12px] p-[10px_14px] text-left font-sans text-[#f3f8fd]"
                    style={{
                      border: `1px solid ${active ? '#7dd3fc' : 'rgba(125,211,252,0.25)'}`,
                      background: active ? 'rgba(125,211,252,0.12)' : 'rgba(2,10,24,0.35)',
                    }}
                  >
                    <span className="flex h-[34px] w-[34px] shrink-0 items-center justify-center rounded-full bg-[#2c3d52] text-[12px] font-black text-[#bfe0f5]">
                      {s.name.trim().charAt(0).toUpperCase()}
                    </span>
                    <span className="text-[14px] font-extrabold">{s.name}</span>
                    <span className="ml-auto text-[11.5px] text-[rgba(203,224,240,0.5)]">{s.hint}</span>
                  </button>
                );
              })}
              {swimmers.length === 0 && (
                <p className="text-[13px] text-[rgba(203,224,240,0.6)]">No swimmers yet — add a favorite first.</p>
              )}
            </div>
            <div className="mt-1 flex justify-between gap-2">
              <button type="button" onClick={() => setStep(1)} className="hp-mono rounded-[10px] border border-[rgba(125,211,252,0.3)] bg-transparent px-4 py-[9px] text-[12px] font-extrabold text-[rgba(125,211,252,0.7)]">← Back</button>
              <button
                type="button"
                disabled={!canNext}
                onClick={() => setStep(3)}
                className="hp-mono rounded-[10px] border-none px-[18px] py-[9px] text-[12px] font-extrabold disabled:cursor-default"
                style={{ background: canNext ? '#38ef8f' : 'rgba(56,239,143,0.25)', color: canNext ? '#04101f' : 'rgba(4,16,31,0.6)' }}
              >
                Next →
              </button>
            </div>
          </div>
        )}

        {step === 3 && (
          <div className="mt-[18px] flex flex-col gap-[14px]">
            <label className="text-[12px] font-extrabold uppercase tracking-[0.14em] text-[#7dd3fc]">
              Link to a swim <span className="text-[normal] font-bold normal-case tracking-normal text-[rgba(203,224,240,0.45)]">· optional</span>
            </label>
            <div className="flex flex-wrap gap-[6px]">
              {competitions.map((c) => (
                <button
                  key={c.id}
                  type="button"
                  onClick={() => { setCompetitionId(c.id); setResultId(null); }}
                  className={chipClass(competitionId === c.id)}
                >
                  <span dir="rtl">{c.name}</span>
                  <span className="opacity-60">{c.date}</span>
                </button>
              ))}
              {competitions.length === 0 && (
                <p className="text-[12.5px] text-[rgba(203,224,240,0.5)]">No competitions found for this swimmer.</p>
              )}
            </div>
            {competitionId != null && (
              <div className="flex flex-col gap-[6px]">
                {results.map((s) => {
                  const active = resultId === s.result_id;
                  return (
                    <button
                      key={s.result_id}
                      type="button"
                      onClick={() => setResultId(s.result_id)}
                      className="flex w-full items-center rounded-[12px] p-[10px_14px] text-left font-sans text-[#f3f8fd]"
                      style={{
                        border: `1px solid ${active ? '#7dd3fc' : 'rgba(125,211,252,0.25)'}`,
                        background: active ? 'rgba(125,211,252,0.12)' : 'rgba(2,10,24,0.35)',
                      }}
                    >
                      <span className="text-[13px] font-extrabold">{s.distance}m {s.style}</span>
                      <span className="hp-mono ml-auto text-[12px] font-extrabold text-[#7dd3fc]">{s.time}</span>
                    </button>
                  );
                })}
              </div>
            )}
            <div className="mt-1 flex justify-between gap-2">
              <button type="button" onClick={() => setStep(2)} className="hp-mono rounded-[10px] border border-[rgba(125,211,252,0.3)] bg-transparent px-4 py-[9px] text-[12px] font-extrabold text-[rgba(125,211,252,0.7)]">← Back</button>
              <div className="flex flex-wrap justify-end gap-2">
                <button
                  type="button"
                  disabled={saving}
                  onClick={() => save('none')}
                  className="hp-mono rounded-[10px] border border-[rgba(125,211,252,0.35)] bg-transparent px-4 py-[9px] text-[12px] font-extrabold text-[#7dd3fc] disabled:opacity-50"
                >
                  Skip — save as general
                </button>
                {competitionId != null && resultId == null && (
                  <button
                    type="button"
                    disabled={saving}
                    onClick={() => save('competition')}
                    className="hp-mono rounded-[10px] border border-dashed border-[rgba(56,239,143,0.5)] bg-transparent px-4 py-[9px] text-[12px] font-extrabold text-[#38ef8f] disabled:opacity-50"
                  >
                    📎 Whole competition
                  </button>
                )}
                <button
                  type="button"
                  disabled={resultId == null || saving}
                  onClick={() => save('result')}
                  className="hp-mono rounded-[10px] border-none px-[18px] py-[9px] text-[12px] font-extrabold disabled:cursor-default"
                  style={{ background: resultId != null ? '#38ef8f' : 'rgba(56,239,143,0.25)', color: resultId != null ? '#04101f' : 'rgba(4,16,31,0.6)' }}
                >
                  Save
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

export default AddLinkModal;
