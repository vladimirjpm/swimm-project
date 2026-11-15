import React, { useEffect, useState } from 'react';

/**
 * Поле таблицы workout, которое пишет значение при уходе из поля (blur / Enter), а не на каждый
 * символ (CF6): «4x100» и «3× 2:10» разбираются целиком — «4x1» на полпути не должен стать
 * дистанцией, а заполнение по шагу — сработать на «1:5». Пока поле в фокусе, правка — своя строка;
 * снаружи значение сменилось (Undo, вставка) — поле показывает новое.
 */
function CommitInput({
  value, onCommit, className, style, ...rest
}: {
  value: string;
  /** Новое значение строкой; разобрать и, если нельзя, оставить прежнее — дело вызывающего. */
  onCommit: (raw: string) => void;
} & Omit<React.InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange'>) {
  const [draft, setDraft] = useState<string | null>(null);
  useEffect(() => { setDraft(null); }, [value]);

  const commit = () => {
    if (draft != null && draft !== value) onCommit(draft);
    setDraft(null);
  };

  return (
    <input
      {...rest}
      value={draft ?? value}
      onChange={(e) => setDraft(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
      className={className}
      style={style}
    />
  );
}

export default CommitInput;
