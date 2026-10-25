import React from 'react';
import { useAuth } from '../../../../hooks/useAuth';
import { toggleCheer, useCheer } from '../../../../hooks/useCheers';
import { useOptionalLoginModal } from '../../login-modal/login-modal-context';

interface UI_CheerButtonProps {
  /** Id строки результата. Нет (статический источник) — кнопки нет. */
  resultId?: number | null;
  /** Неявка (NS/DNS, `HelperResults.isNoStart`): старта не было — поздравлять не с чем. */
  noStart?: boolean;
  className?: string;
}

/**
 * 🎉 «поздравить с заплывом» (этап 1, решение Влада 25.10.2026). Стоит в строке протокола и в
 * общей строке заплыва `SwimRow` (страница пловца, группа).
 *
 * - Поздравлять может только вошедший; гость по клику получает окно входа.
 * - Число рядом — только когда поздравления есть: пустой счётчик не рисуется вовсе.
 * - Своё 🎉 — цветное, чужое/пустое — серое; повторный клик снимает.
 * - Имён поздравивших нет нигде: публично только число (пловцы — в основном дети).
 *
 * Счётчики спрашивает общее хранилище пачкой (`useCheers`) — вызывающему id собирать не нужно.
 * Клик не всплывает: строка бывает ссылкой или раскрывается по клику.
 */
const UI_CheerButton: React.FC<UI_CheerButtonProps> = ({ resultId, noStart = false, className = '' }) => {
  const eligible = resultId != null && !noStart;
  const state = useCheer(eligible ? resultId : null);
  const { isAuthenticated } = useAuth();
  const login = useOptionalLoginModal();

  if (!eligible) return null;

  const title = !isAuthenticated
    ? 'Sign in to congratulate'
    : state.mine
      ? 'You congratulated this swim — click to undo'
      : 'Congratulate';

  const onClick = (e: React.MouseEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (!isAuthenticated) {
      login?.openLoginModal();
      return;
    }
    void toggleCheer(resultId!);
  };

  return (
    <button
      type="button"
      onClick={onClick}
      title={state.count > 0 ? `${title} · ${state.count} congratulated` : title}
      aria-label={title}
      aria-pressed={state.mine}
      className={`inline-flex shrink-0 items-center gap-[3px] leading-none hover:scale-110 transition-transform ${className}`}
    >
      <span
        aria-hidden="true"
        className="text-[14px]"
        style={state.mine ? undefined : { filter: 'grayscale(1)', opacity: 0.55 }}
      >
        🎉
      </span>
      {state.count > 0 && (
        <span
          className="text-[10.5px] font-extrabold tabular-nums"
          style={{ color: 'var(--theme-mode-text-secondary)' }}
        >
          {state.count}
        </span>
      )}
    </button>
  );
};

export default UI_CheerButton;
