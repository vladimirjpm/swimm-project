import React from 'react';

/**
 * Плашка пловца в редакторе дорожек — цветная пилюля с одним именем (решение Влада 09.11.2026,
 * по образцу списка «кто идёт» его приложения): фон — бледный тон цвета ТЕКУЩЕГО уровня, рамка —
 * тот же цвет, имя тёмным текстом темы; тёзкам подпись дописывает редактор (`laneShortNames`).
 * Полное имя — в `title`, чтобы тренер мог проверить, кто это.
 * - телефон: тап по пилюле выбирает пловца, строка «Move … to» рисуется в карточке дорожки;
 *   `draggable` на телефоне не ставим — он мешает прокрутке;
 * - десктоп: то же плюс перетаскивание мышью, бросок на другую пилюлю вставляет ПЕРЕД ней.
 * Выбранная — рамка `--deep-ow` в 2px.
 */
function LaneChip({
  label, fullName, color, subLevel = null, leftGroup, selected, desktop, onSelect, dragProps, dropActive,
}: {
  /** Короткая подпись: имя (у тёзок — с буквой фамилии). */
  label: string;
  /** Полное имя — подсказка и aria. */
  fullName: string;
  /** Цвет ТЕКУЩЕГО уровня пловца; null — без уровня. */
  color: string | null;
  /** ТЕКУЩИЙ подуровень (1 — верхний): мелкая цифра после имени — видно, кто перешёл с уровня выше. */
  subLevel?: number | null;
  /** Ушёл из группы (план — снимок): имя приглушено. */
  leftGroup: boolean;
  selected: boolean;
  desktop: boolean;
  onSelect: () => void;
  /** Только десктоп: `draggable` + обработчики drag/drop. */
  dragProps?: React.HTMLAttributes<HTMLElement>;
  /** Над плашкой держат другую — вставим перед ней. */
  dropActive?: boolean;
}) {
  // Цвет уровня — данные тренера, не тема: подмешиваем его к фону карточки темы, так пилюля
  // бледная в светлой и приглушённая в тёмной, а текст остаётся текстом темы.
  const background = color
    ? `color-mix(in srgb, ${color} 30%, var(--deep-card-bg))`
    : 'var(--deep-card-bg)';
  const borderColor = selected ? 'var(--deep-ow)' : color ?? 'var(--deep-card-border)';
  const outline = dropActive ? '2px solid var(--deep-accent)' : undefined;

  return (
    <button
      type="button"
      {...(desktop ? dragProps : undefined)}
      onClick={onSelect}
      aria-pressed={selected}
      aria-label={`Move ${fullName}`}
      title={fullName}
      className={`inline-flex h-[30px] max-w-full items-center gap-1 rounded-full px-3 text-left ${
        desktop ? 'cursor-grab active:cursor-grabbing' : 'cursor-pointer'
      }`}
      style={{
        background,
        border: `${selected ? 2 : 1}px solid ${borderColor}`,
        outline,
        opacity: leftGroup ? 0.55 : undefined,
      }}
    >
      <span dir="auto" className="min-w-0 truncate text-[13px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
        <bdi>{label}</bdi>
      </span>
      {subLevel != null && (
        <span className="hp-mono flex-none text-[10px] font-extrabold" style={{ color: 'var(--t-text-3)' }} title={`Sub-level ${subLevel}`}>
          {subLevel}
        </span>
      )}
    </button>
  );
}

export default LaneChip;
