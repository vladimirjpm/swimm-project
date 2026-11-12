import React, { useEffect, useMemo, useRef, useState } from 'react';
import { DeepSheet, SheetButton, SheetError, SheetField, SheetFooter } from '../../components/deep/deep-sheet';
import type { MySwimDto } from '../use-my-swims';
import Cover from './cover';
import type { CoverLayout, CoverOptions } from './cover-data';
import { YOUTUBE_MOBILE_LIMIT_BYTES } from './cover-export';
import { ensureCoverFonts } from './cover-fonts';
import { readCoverPhoto } from './cover-photo';
import { coverDataForSwim, coverFileNameForSwim } from './cover-source';
import { COVER_H, COVER_W } from './cover-theme';
import CoverPreview from './dialog/cover-preview';
import CoverToggles from './dialog/cover-toggles';
import LayoutPicker from './dialog/layout-picker';
import { useCoverDownload } from './dialog/use-cover-download';

// Диалог «YouTube cover» (хендофф `my-media-handoff-nov2026`, задача 2). В макете диалога нет —
// состав согласован с Владом 12.11.2026: выбор раскладки A / B / C, для B и C чекбоксы
// `Show swimmer name` и `Add photo`, предпросмотр, Download. Оболочка — общий `DeepSheet`.
//
// Здесь только состояние и сборка: части диалога — в `dialog/`, обложка — `cover.tsx`,
// файл — `cover-export.ts`. Модуль ленивый (`cover-entry.tsx`).

/** Ширина модала на десктопе: шире обычных 480 — в теле предпросмотр картинки. */
const DIALOG_WIDTH = 720;

/** Фото необязательное и по умолчанию выключено (решение Влада 12.11.2026). */
const DEFAULT_OPTIONS: CoverOptions = { showName: true, photoSlot: false, photo: null };

const megabytes = (bytes: number) => `${(bytes / (1024 * 1024)).toFixed(1)} MB`;

/** Строка после скачивания: вес файла и предупреждение о лимите YouTube для телефона. */
function SavedNote({ bytes }: { bytes: number }) {
  return (
    <p className="m-0 text-[12px] font-bold text-[var(--t-text-2)]" role="status">
      Saved · {megabytes(bytes)}
      {bytes > YOUTUBE_MOBILE_LIMIT_BYTES && ' — over 2 MB: YouTube takes it from a computer, but not from the phone app.'}
    </p>
  );
}

export default function CoverDialog({ swim, swimmerName, firstDay, lastDay, onClose }: {
  swim: MySwimDto;
  swimmerName: string;
  firstDay: string;
  lastDay: string;
  onClose: () => void;
}) {
  const [layout, setLayout] = useState<CoverLayout>('A');
  const [options, setOptions] = useState<CoverOptions>(DEFAULT_OPTIONS);
  const [fontsReady, setFontsReady] = useState(false);
  const coverRef = useRef<HTMLDivElement>(null);
  const { busy, error, setError, savedBytes, forgetSaved, download } = useCoverDownload();

  const data = useMemo(
    () => coverDataForSwim(swim, swimmerName, firstDay, lastDay),
    [swim, swimmerName, firstDay, lastDay],
  );

  useEffect(() => {
    let alive = true;
    ensureCoverFonts().then(
      () => { if (alive) setFontsReady(true); },
      () => { if (alive) setError('Could not load the cover fonts. Check the connection and open the dialog again.'); },
    );
    return () => { alive = false; };
  }, [setError]);

  const pickLayout = (next: CoverLayout) => { setLayout(next); forgetSaved(); };
  const patchOptions = (patch: Partial<CoverOptions>) => { setOptions((o) => ({ ...o, ...patch })); forgetSaved(); };
  const pickPhoto = async (file: File) => {
    try {
      patchOptions({ photo: await readCoverPhoto(file) });
      setError(null);
    } catch {
      setError('This file is not a picture the browser can read. Try a JPG or PNG.');
    }
  };
  const save = () => {
    if (!busy && fontsReady) download(coverRef.current, coverFileNameForSwim(swim, layout));
  };

  return (
    <DeepSheet
      title="YouTube cover"
      sub={`${COVER_W} × ${COVER_H} PNG · made in your browser, nothing is uploaded`}
      width={DIALOG_WIDTH}
      busy={busy}
      onClose={onClose}
      onSubmit={save}
      footer={(
        <div className="flex flex-col gap-2.5">
          <SheetError error={error} />
          {savedBytes != null && !error && <SavedNote bytes={savedBytes} />}
          <SheetFooter>
            <SheetButton kind="cancel" onClick={onClose} disabled={busy}>Close</SheetButton>
            <SheetButton disabled={busy || !fontsReady}>{busy ? 'Making the image…' : 'Download PNG'}</SheetButton>
          </SheetFooter>
        </div>
      )}
    >
      <SheetField label="Layout" group>
        <LayoutPicker value={layout} onChange={pickLayout} />
      </SheetField>
      <CoverPreview ready={fontsReady} placeholder={error ? '' : 'Preparing the preview…'}>
        <Cover ref={coverRef} layout={layout} data={data} options={options} />
      </CoverPreview>
      {/* У раскладки A переключателей нет: имя есть всегда, фото нет. */}
      {layout !== 'A' && <CoverToggles options={options} onChange={patchOptions} onPickPhoto={pickPhoto} />}
    </DeepSheet>
  );
}
