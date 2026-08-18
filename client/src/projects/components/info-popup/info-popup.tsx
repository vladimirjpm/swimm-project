import React from 'react';

/**
 * Модальная «объяснялка» с языковыми вкладками — общий компонент для всего проекта.
 *
 * Зачем отдельно от `components/popup/popup.tsx`: тот — синглтон на Redux с реестром типов
 * контента (нормативы, рекорды, очки). Для короткого пояснения рядом с бейджем это дорого:
 * пришлось бы заводить тип в enum, ветку в мапе и класть текст в store. Здесь попап живёт
 * там же, где кнопка, которая его открывает.
 *
 * Язык: интерфейс сайта английский (правило проекта), поэтому **en — по умолчанию**.
 * Вкладки ru/he нужны там, где текст объясняет данные родителям и тренерам, а не
 * разработчику. Выбор запоминается на весь сайт (localStorage) — человек, переключившийся
 * на иврит в одном попапе, не хочет делать это снова в следующем.
 */
export type InfoLang = 'en' | 'ru' | 'he';

/** Текст на трёх языках. Пустая строка = вкладка отключена (нечего показывать). */
export interface InfoText {
  en: string;
  ru: string;
  he: string;
}

interface InfoPopupProps {
  open: boolean;
  onClose: () => void;
  /** Заголовок — тоже на трёх языках, чтобы шапка не спорила с телом. */
  title: InfoText;
  body: InfoText;
  /** Необязательная сноска мелким шрифтом (источник, ссылка на правило). */
  footnote?: InfoText;
}

const LANGS: { key: InfoLang; label: string }[] = [
  { key: 'en', label: 'EN' },
  { key: 'ru', label: 'RU' },
  { key: 'he', label: 'HE' },
];

const STORAGE_KEY = 'ui:info-popup:lang';

/** Запомненный язык на весь сайт; мусор и отсутствие → en. */
function readLang(): InfoLang {
  try {
    const v = localStorage.getItem(STORAGE_KEY);
    if (v === 'en' || v === 'ru' || v === 'he') return v;
  } catch {
    /* приватный режим — просто английский */
  }
  return 'en';
}

/**
 * Выбранный язык объяснялок — ОДИН на весь сайт. Вынесен из попапа, потому что тем же
 * механизмом пользуется попап «Points system»: человек, переключившийся на иврит в одном
 * месте, не должен переключаться снова в другом.
 */
export function useInfoLang(): [InfoLang, (next: InfoLang) => void] {
  const [lang, setLang] = React.useState<InfoLang>(readLang);

  const pickLang = React.useCallback((next: InfoLang) => {
    setLang(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      /* не критично */
    }
  }, []);

  return [lang, pickLang];
}

/** Вкладки EN/RU/HE. Язык без текста выключен — вкладка-пустышка обманывает читателя. */
export const UI_LangTabs: React.FC<{
  lang: InfoLang;
  onPick: (next: InfoLang) => void;
  available: Partial<Record<InfoLang, string | undefined>>;
}> = ({ lang, onPick, available }) => (
  <div className="mb-3 flex gap-1">
    {LANGS.map(({ key, label }) => {
      const empty = !available[key]?.trim();
      const active = key === lang;
      return (
        <button
          key={key}
          type="button"
          disabled={empty}
          onClick={() => onPick(key)}
          className={`rounded-md px-2.5 py-1 text-[11px] font-bold tracking-wide transition-colors
            ${active
              ? 'bg-[var(--theme-primary)] text-[var(--theme-mode-accent-text)]'
              : 'text-[var(--theme-mode-text-muted)] hover:text-[var(--theme-mode-text)]'}
            ${empty ? 'cursor-not-allowed opacity-40' : ''}`}
        >
          {label}
        </button>
      );
    })}
  </div>
);

const UI_InfoPopup: React.FC<InfoPopupProps> = ({ open, onClose, title, body, footnote }) => {
  const [lang, pickLang] = useInfoLang();

  React.useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  if (!open) return null;

  // Иврит — RTL; выравнивание тоже, иначе текст читается «наизнанку».
  const isRtl = lang === 'he';

  return (
    <div
      className="fixed inset-0 z-[140] flex items-center justify-center p-4"
      style={{
        background: 'var(--theme-mode-overlay)',
        backdropFilter: 'blur(3px)',
        WebkitBackdropFilter: 'blur(3px)',
      }}
      onClick={onClose}
      role="presentation"
    >
      <div
        role="dialog"
        aria-modal="true"
        className="relative w-[min(92vw,34rem)] max-h-[80vh] overflow-y-auto rounded-2xl border p-5
                   bg-[var(--theme-mode-surface)] text-[var(--theme-mode-text)]
                   border-[var(--theme-mode-modal-border)]"
        style={{ boxShadow: 'var(--theme-mode-modal-shadow)' }}
        onClick={(e) => e.stopPropagation()}
      >
        <button
          onClick={onClose}
          aria-label="Close"
          className="absolute top-3 right-3 text-[var(--theme-mode-text-muted)] hover:text-[var(--theme-mode-text)]"
        >
          ✕
        </button>

        <UI_LangTabs lang={lang} onPick={pickLang} available={body} />

        <div dir={isRtl ? 'rtl' : 'ltr'} style={{ textAlign: isRtl ? 'right' : 'left' }}>
          <div className="mb-2 pr-6 text-[15px] font-bold">{title[lang]}</div>
          <div className="whitespace-pre-line text-[13px] leading-relaxed">{body[lang]}</div>
          {footnote?.[lang] && (
            <div className="mt-3 text-[11px] text-[var(--theme-mode-text-muted)]">{footnote[lang]}</div>
          )}
        </div>
      </div>
    </div>
  );
};

export default UI_InfoPopup;
