import React from 'react';
import { MODULE_LABEL, type ModuleKey } from './module-defs';

// Плитка модуля (9d): штрих цвета · медальон-итог · метка + имя лидера + подпись · ›.
// Активная плитка (aria-selected) — заливка, полоса 4px, залитый медальон, язычок-ромб
// (вправо на десктопе, вниз в мобайле-аккордеоне) — всё в overview2.css.

interface Props {
  module: ModuleKey;
  active: boolean;
  onSelect(module: ModuleKey): void;
  /** Плитка видна, но не открывается (ненаградное соревнование → High Point не разыгрывается).
   *  Не прячем совсем: пропавший модуль читается как «данные не загрузились». */
  disabled?: boolean;
  /** Причина недоступности — тултип и aria-description. */
  disabledReason?: string;
  /** Содержимое медальона: число, возраст «8–11», эмодзи или инициалы лого. */
  medal: React.ReactNode;
  /** Медальон — лого клуба (полосатый плейсхолдер вместо чипа). */
  medalIsLogo?: boolean;
  /** Имя лидера / главный итог. */
  name: React.ReactNode;
  /** Подпись (вторая строка). */
  sub?: React.ReactNode;
}

export default function ModuleTile({
  module, active, onSelect, medal, medalIsLogo, name, sub, disabled, disabledReason,
}: Props) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      aria-disabled={disabled || undefined}
      disabled={disabled}
      title={disabled ? disabledReason : undefined}
      className={`ov2-module ov2-module--${module} ov2-tile${disabled ? ' ov2-tile--disabled' : ''}`}
      onClick={() => { if (!disabled) onSelect(module); }}
    >
      <span className="ov2-tile__bar" />
      <span className={`ov2-tile__medal${medalIsLogo ? ' ov2-logo' : ''}`}>{medal}</span>
      <span className="flex min-w-0 flex-1 flex-col gap-px">
        <span className="ov2-tile__label">{MODULE_LABEL[module]}</span>
        <span className="ov2-tile__name" dir="auto">{name}</span>
        {sub != null && <span className="ov2-tile__sub" dir="auto">{sub}</span>}
      </span>
      <span className="ov2-tile__chevron">›</span>
    </button>
  );
}
