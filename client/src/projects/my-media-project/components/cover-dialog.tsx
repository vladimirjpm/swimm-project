import React, { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { DeepSheet, SheetButton, SheetError, SheetField, SheetFooter } from '../../components/deep/deep-sheet';
import Cover, { COVER_H, COVER_W, type CoverData, type CoverLayout, type CoverOptions } from '../cover/cover-layouts';
import { ensureCoverFonts } from '../cover/cover-fonts';
import { downloadBlob, renderCoverPng, YOUTUBE_MOBILE_LIMIT_BYTES } from '../cover/cover-export';

// Диалог «YouTube cover» (хендофф `my-media-handoff-nov2026`, задача 2). В макете диалога нет —
// состав согласован с Владом 12.11.2026: выбор раскладки A / B / C, для B и C чекбоксы
// `Show swimmer name` и `Add photo`, предпросмотр, Download. Оболочка — общий `DeepSheet`.
//
// Файл делается в браузере и сразу скачивается: на сервер не уходит ни обложка, ни фото
// пловца. Модуль ленивый (`React.lazy` в `swim-list.tsx`): шрифты обложки и `html-to-image`
// страница грузит только по нажатию кнопки.

const LAYOUTS: { key: CoverLayout; label: string; hint: string }[] = [
  { key: 'A', label: 'A', hint: 'Result as one big line' },
  { key: 'B', label: 'B', hint: 'Result on the left, photo on the right' },
  { key: 'C', label: 'C', hint: 'Competition column, result stacked' },
];

/** Сторона, до которой уменьшается выбранное фото: больше обложке не нужно (слот ~500px). */
const PHOTO_MAX_SIDE = 1600;

/**
 * Фото пловца → data-URL. Файл остаётся в браузере. Уменьшаем сразу: снимок с телефона — это
 * 4000px и 5 МБ, а в обложку он вшивается целиком и раздул бы каждый рендер.
 */
async function readPhoto(file: File): Promise<string> {
  const bitmap = await createImageBitmap(file);
  const scale = Math.min(1, PHOTO_MAX_SIDE / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('no canvas');
  ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  return canvas.toDataURL('image/jpeg', 0.9);
}

const megabytes = (bytes: number) => `${(bytes / (1024 * 1024)).toFixed(1)} MB`;

const CHECK_CLS = 'flex min-h-11 cursor-pointer items-center gap-2.5 text-[13px] font-bold text-[var(--t-text)]';

function CoverDialog({ data, fileName, onClose }: {
  data: CoverData;
  /** Имя файла без расширения. */
  fileName: string;
  onClose: () => void;
}) {
  const [layout, setLayout] = useState<CoverLayout>('A');
  const [showName, setShowName] = useState(true);
  // Фото необязательное и по умолчанию выключено (решение Влада 12.11.2026).
  const [photoSlot, setPhotoSlot] = useState(false);
  const [photo, setPhoto] = useState<string | null>(null);
  const [fontsReady, setFontsReady] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedSize, setSavedSize] = useState<number | null>(null);

  const coverRef = useRef<HTMLDivElement>(null);
  const frameRef = useRef<HTMLDivElement>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const [scale, setScale] = useState(0);

  useEffect(() => {
    let alive = true;
    ensureCoverFonts().then(
      () => { if (alive) setFontsReady(true); },
      () => { if (alive) setError('Could not load the cover fonts. Check the connection and open the dialog again.'); },
    );
    return () => { alive = false; };
  }, []);

  // Обложка всегда 1280×720; предпросмотр — она же, уменьшенная transform-ом под ширину рамки.
  useLayoutEffect(() => {
    const frame = frameRef.current;
    if (!frame) return undefined;
    const fit = () => setScale(frame.clientWidth / COVER_W);
    fit();
    const observer = new ResizeObserver(fit);
    observer.observe(frame);
    return () => observer.disconnect();
  }, []);

  const hasOptions = layout !== 'A';
  const options: CoverOptions = { showName, photoSlot, photo };

  const pickPhoto = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    // Тот же файл можно выбрать повторно — иначе `change` не придёт.
    e.target.value = '';
    if (!file) return;
    try {
      setPhoto(await readPhoto(file));
      setError(null);
    } catch {
      setError('This file is not a picture the browser can read. Try a JPG or PNG.');
    }
  };

  const download = async () => {
    const node = coverRef.current;
    if (!node || busy || !fontsReady) return;
    setBusy(true);
    setError(null);
    try {
      const blob = await renderCoverPng(node);
      downloadBlob(blob, `${fileName}-${layout}.png`);
      setSavedSize(blob.size);
    } catch {
      setError('Could not make the image. Try again, or try another browser.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <DeepSheet
      title="YouTube cover"
      sub={`${COVER_W} × ${COVER_H} PNG · made in your browser, nothing is uploaded`}
      width={720}
      busy={busy}
      onClose={onClose}
      onSubmit={download}
      footer={(
        <div className="flex flex-col gap-2.5">
          <SheetError error={error} />
          {savedSize != null && !error && (
            <p className="m-0 text-[12px] font-bold text-[var(--t-text-2)]" role="status">
              Saved · {megabytes(savedSize)}
              {savedSize > YOUTUBE_MOBILE_LIMIT_BYTES
                && ' — over 2 MB: YouTube takes it from a computer, but not from the phone app.'}
            </p>
          )}
          <SheetFooter>
            <SheetButton kind="cancel" onClick={onClose} disabled={busy}>Close</SheetButton>
            <SheetButton disabled={busy || !fontsReady}>{busy ? 'Making the image…' : 'Download PNG'}</SheetButton>
          </SheetFooter>
        </div>
      )}
    >
      <SheetField label="Layout" group>
        <div className="grid grid-cols-3 gap-2" role="radiogroup" aria-label="Cover layout">
          {LAYOUTS.map((l) => {
            const on = l.key === layout;
            return (
              <button
                key={l.key}
                type="button"
                role="radio"
                aria-checked={on}
                title={l.hint}
                onClick={() => { setLayout(l.key); setSavedSize(null); }}
                className="flex min-h-11 cursor-pointer flex-col items-center justify-center rounded-[10px] border px-2 py-1.5"
                style={on
                  ? { borderColor: 'var(--t-accent)', background: 'var(--t-accent-soft)', color: 'var(--t-accent)' }
                  : { borderColor: 'var(--t-border)', background: 'transparent', color: 'var(--t-text-2)' }}
              >
                <span className="text-[14px] font-black leading-tight">{l.label}</span>
                <span className="hidden text-[10.5px] font-bold leading-tight sm:block">{l.hint}</span>
              </button>
            );
          })}
        </div>
      </SheetField>

      {/* Рамка предпросмотра держит пропорцию 16:9; внутри — настоящая обложка 1280×720. */}
      <div
        ref={frameRef}
        className="relative w-full shrink-0 overflow-hidden rounded-[10px] border border-[var(--t-border)]"
        style={{ aspectRatio: `${COVER_W} / ${COVER_H}`, background: '#0b1b3f' }}
      >
        {fontsReady && scale > 0 ? (
          <div style={{ width: COVER_W, height: COVER_H, transform: `scale(${scale})`, transformOrigin: '0 0' }}>
            <Cover ref={coverRef} layout={layout} data={data} options={options} />
          </div>
        ) : (
          <div className="absolute inset-0 flex items-center justify-center text-[12px] font-bold text-white/70">
            {error ? '' : 'Preparing the preview…'}
          </div>
        )}
      </div>

      {hasOptions && (
        <div className="flex flex-col">
          <label className={CHECK_CLS}>
            <input
              type="checkbox"
              checked={showName}
              onChange={(e) => { setShowName(e.target.checked); setSavedSize(null); }}
              className="h-[18px] w-[18px] accent-[var(--t-accent)]"
            />
            Show swimmer name
          </label>
          <div className="flex flex-wrap items-center gap-x-3">
            <label className={CHECK_CLS}>
              <input
                type="checkbox"
                checked={photoSlot}
                onChange={(e) => { setPhotoSlot(e.target.checked); setSavedSize(null); }}
                className="h-[18px] w-[18px] accent-[var(--t-accent)]"
              />
              Add photo
            </label>
            {photoSlot && (
              <>
                <button
                  type="button"
                  onClick={() => fileRef.current?.click()}
                  className="min-h-9 cursor-pointer rounded-[9px] border border-[var(--t-accent-border)] bg-transparent px-3 text-[12px] font-extrabold text-[var(--t-accent)]"
                >
                  {photo ? 'Change photo' : 'Choose photo'}
                </button>
                {photo && (
                  <button
                    type="button"
                    onClick={() => { setPhoto(null); setSavedSize(null); }}
                    className="min-h-9 cursor-pointer rounded-[9px] border border-[var(--t-border)] bg-transparent px-3 text-[12px] font-extrabold text-[var(--t-text-2)]"
                  >
                    Remove
                  </button>
                )}
                <span className="text-[11px] font-bold text-[var(--t-text-3)]">
                  {photo ? 'The photo stays on this device.' : 'From this device; it is not uploaded.'}
                </span>
              </>
            )}
          </div>
          <input ref={fileRef} type="file" accept="image/*" hidden onChange={pickPhoto} />
        </div>
      )}
    </DeepSheet>
  );
}

export default CoverDialog;
