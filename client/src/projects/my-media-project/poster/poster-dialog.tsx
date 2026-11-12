import React, { useEffect, useMemo, useRef, useState } from 'react';
import { DeepSheet, SheetButton, SheetError, SheetField, SheetFooter } from '../../components/deep/deep-sheet';
import LayoutPicker, { type LayoutChoice } from '../print/dialog/layout-picker';
import PhotoToggle, { Check, type PhotoOptions } from '../print/dialog/photo-controls';
import PrintPreview from '../print/dialog/print-preview';
import { usePrintDownload } from '../print/dialog/use-print-download';
import { ensurePrintFonts } from '../print/print-fonts';
import { readPrintPhoto } from '../print/print-photo';
import type { MySwimDto } from '../use-my-swims';
import SwimPicker from './dialog/swim-picker';
import { usePosterSelection } from './dialog/use-poster-selection';
import Poster, { POSTER_HAS_PHOTO } from './poster';
import { buildPosterData, posterFileName, posterSwims, type PosterLayout } from './poster-data';
import { posterSourceForSwims, swimmerSwims } from './poster-source';
import { POSTER_H, POSTER_W, posterSize } from './poster-theme';

// Диалог «Poster» (хендофф `my-media-handoff-nov2026`, задача 2). В макете диалога нет — состав
// согласован с Владом 12.11.2026: выбор раскладки A / B / C, предпросмотр, галочки заплывов,
// необязательное фото (A и C), `Print quality`, Download. Оболочка — общий `DeepSheet`.
//
// Здесь только состояние и сборка: части диалога — в `dialog/` и общем `print/dialog/`,
// плакат — `poster.tsx`, файл — `print/print-export.ts`. Модуль ленивый (`poster-entry.tsx`).

/** Ширина модала на десктопе — как у диалога обложки. */
const DIALOG_WIDTH = 720;
/** Потолок ширины предпросмотра: плакат высокий, во всю ширину диалога он занял бы экран. */
const PREVIEW_MAX_WIDTH = 340;

const LAYOUTS: LayoutChoice<PosterLayout>[] = [
  { key: 'A', hint: 'Photo on the left, results as a list' },
  { key: 'B', hint: 'Medal count and a results table' },
  { key: 'C', hint: 'Wide photo, results in a band' },
];

/** Фото необязательное и по умолчанию выключено (решение Влада 12.11.2026). */
const NO_PHOTO: PhotoOptions = { photoSlot: false, photo: null };

const megabytes = (bytes: number) => `${(bytes / (1024 * 1024)).toFixed(1)} MB`;

export default function PosterDialog({ swims, swimmerId, swimmerName, clubName, firstDay, onClose }: {
  /** Заплывы карточки соревнования; на плакат идут заплывы пловца `swimmerId`. */
  swims: MySwimDto[];
  swimmerId: number;
  swimmerName: string;
  clubName: string | null;
  /** Первый день соревнования, yyyy-MM-dd. */
  firstDay: string;
  onClose: () => void;
}) {
  const [layout, setLayout] = useState<PosterLayout>('A');
  const [photo, setPhoto] = useState<PhotoOptions>(NO_PHOTO);
  const [printQuality, setPrintQuality] = useState(false);
  const [fontsReady, setFontsReady] = useState(false);
  const posterRef = useRef<HTMLDivElement>(null);
  const { busy, error, setError, savedBytes, forgetSaved, download } = usePrintDownload();

  const source = useMemo(
    () => posterSourceForSwims(swimmerSwims(swims, swimmerId), swimmerName, clubName, firstDay),
    [swims, swimmerId, swimmerName, clubName, firstDay],
  );
  const choices = useMemo(() => posterSwims(source.swims), [source]);
  const { selected, toggle, limit, full } = usePosterSelection(choices, layout);
  const data = useMemo(() => buildPosterData(source, selected), [source, selected]);
  const size = posterSize(printQuality);

  useEffect(() => {
    let alive = true;
    ensurePrintFonts().then(
      () => { if (alive) setFontsReady(true); },
      () => { if (alive) setError('Could not load the poster fonts. Check the connection and open the dialog again.'); },
    );
    return () => { alive = false; };
  }, [setError]);

  /** Любая правка плаката: вес прежнего файла к нему больше не относится. */
  const changed = <T,>(apply: (value: T) => void) => (value: T) => { apply(value); forgetSaved(); };
  const pickPhoto = async (file: File) => {
    try {
      setPhoto({ photoSlot: true, photo: await readPrintPhoto(file) });
      forgetSaved();
      setError(null);
    } catch {
      setError('This file is not a picture the browser can read. Try a JPG or PNG.');
    }
  };
  const canSave = fontsReady && !busy && selected.size > 0;
  const save = () => {
    if (canSave) download(posterRef.current, size, posterFileName(firstDay, layout));
  };

  return (
    <DeepSheet
      title="Poster"
      sub={`${POSTER_W * (size.pixelRatio ?? 1)} × ${POSTER_H * (size.pixelRatio ?? 1)} PNG · made in your browser, nothing is uploaded`}
      width={DIALOG_WIDTH}
      busy={busy}
      onClose={onClose}
      onSubmit={save}
      footer={(
        <div className="flex flex-col gap-2.5">
          <SheetError error={error} />
          {savedBytes != null && !error && (
            <p className="m-0 text-[12px] font-bold text-[var(--t-text-2)]" role="status">Saved · {megabytes(savedBytes)}</p>
          )}
          <SheetFooter>
            <SheetButton kind="cancel" onClick={onClose} disabled={busy}>Close</SheetButton>
            <SheetButton disabled={!canSave}>{busy ? 'Making the image…' : 'Download PNG'}</SheetButton>
          </SheetFooter>
        </div>
      )}
    >
      <SheetField label="Layout" group>
        <LayoutPicker label="Poster layout" choices={LAYOUTS} value={layout} onChange={changed(setLayout)} />
      </SheetField>
      <PrintPreview width={POSTER_W} height={POSTER_H} maxWidth={PREVIEW_MAX_WIDTH} ready={fontsReady} placeholder={error ? '' : 'Preparing the preview…'}>
        <Poster ref={posterRef} layout={layout} data={data} options={POSTER_HAS_PHOTO[layout] ? photo : NO_PHOTO} />
      </PrintPreview>
      <SheetField label="Swims on the poster" group>
        <SwimPicker swims={choices} selected={selected} full={full} limit={limit} onToggle={changed(toggle)} />
      </SheetField>
      <div className="flex flex-col">
        {POSTER_HAS_PHOTO[layout] && (
          <PhotoToggle options={photo} onChange={changed((patch) => setPhoto((p) => ({ ...p, ...patch })))} onPickPhoto={pickPhoto} />
        )}
        <Check label="Print quality (2828 × 4000, larger file)" checked={printQuality} onChange={changed(setPrintQuality)} />
      </div>
    </DeepSheet>
  );
}
