import React from 'react';
import PhotoToggle, { Check } from '../../print/dialog/photo-controls';
import type { CoverOptions } from '../cover-data';

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
      <PhotoToggle options={options} onChange={onChange} onPickPhoto={onPickPhoto} />
    </div>
  );
}
