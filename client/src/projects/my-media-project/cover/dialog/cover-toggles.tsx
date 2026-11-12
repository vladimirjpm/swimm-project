import React, { useRef } from 'react';
import type { CoverOptions } from '../cover-data';

const CHECK_CLS = 'flex min-h-11 cursor-pointer items-center gap-2.5 text-[13px] font-bold text-[var(--t-text)]';
const BOX_CLS = 'h-[18px] w-[18px] accent-[var(--t-accent)]';
const SMALL_BTN = 'min-h-9 cursor-pointer rounded-[9px] border bg-transparent px-3 text-[12px] font-extrabold';

function Check({ label, checked, onChange }: { label: string; checked: boolean; onChange: (on: boolean) => void }) {
  return (
    <label className={CHECK_CLS}>
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} className={BOX_CLS} />
      {label}
    </label>
  );
}

/** Кнопки выбора фото: выбрать / сменить, убрать, и подпись о том, что файл никуда не уходит. */
function PhotoButtons({ hasPhoto, onPick, onRemove }: { hasPhoto: boolean; onPick: (file: File) => void; onRemove: () => void }) {
  const fileRef = useRef<HTMLInputElement>(null);
  const picked = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    // Тот же файл можно выбрать повторно — иначе `change` не придёт.
    e.target.value = '';
    if (file) onPick(file);
  };
  return (
    <>
      <button type="button" onClick={() => fileRef.current?.click()} className={`${SMALL_BTN} border-[var(--t-accent-border)] text-[var(--t-accent)]`}>
        {hasPhoto ? 'Change photo' : 'Choose photo'}
      </button>
      {hasPhoto && (
        <button type="button" onClick={onRemove} className={`${SMALL_BTN} border-[var(--t-border)] text-[var(--t-text-2)]`}>
          Remove
        </button>
      )}
      <span className="text-[11px] font-bold text-[var(--t-text-3)]">
        {hasPhoto ? 'The photo stays on this device.' : 'From this device; it is not uploaded.'}
      </span>
      <input ref={fileRef} type="file" accept="image/*" hidden onChange={picked} />
    </>
  );
}

/**
 * Два переключателя раскладок B и C (хендофф §2.5): `Show swimmer name` и `Add photo`, у
 * второго — выбор файла. Состояние держит диалог; сюда приходят значения и обработчики.
 */
export default function CoverToggles({ options, onChange, onPickPhoto }: {
  options: CoverOptions;
  onChange: (patch: Partial<CoverOptions>) => void;
  onPickPhoto: (file: File) => void;
}) {
  return (
    <div className="flex flex-col">
      <Check label="Show swimmer name" checked={options.showName} onChange={(showName) => onChange({ showName })} />
      <div className="flex flex-wrap items-center gap-x-3">
        <Check label="Add photo" checked={options.photoSlot} onChange={(photoSlot) => onChange({ photoSlot })} />
        {options.photoSlot && (
          <PhotoButtons hasPhoto={options.photo != null} onPick={onPickPhoto} onRemove={() => onChange({ photo: null })} />
        )}
      </div>
    </div>
  );
}
