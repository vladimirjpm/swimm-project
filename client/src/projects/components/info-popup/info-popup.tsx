import React from 'react';
import UI_Markdown, { splitDocument } from '../markdown/markdown';
import { contentLanguages, useContent } from '../../../content/content';

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
 *
 * Текст — ТОЛЬКО ключом контента (`src/content/<ключ>/{en,ru,he}.md`, решение Влада
 * 11.10.2026): заголовок `# …`, тело, сноска ниже строки `---`. Объектов `{ en, ru, he }` в
 * коде больше нет — длинный текст в tsx не вычитать и не разметить.
 */
export type InfoLang = 'en' | 'ru' | 'he';

interface InfoPopupProps {
  open: boolean;
  onClose: () => void;
  /** Ключ контента, например `popups/swim-quality-protocol`. */
  contentKey: string;
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

/** Тело попапа — отдельно, чтобы текст грузился только у ОТКРЫТОГО попапа (их на странице десятки). */
const InfoPopupText: React.FC<{ contentKey: string }> = ({ contentKey }) => {
  const [pickedLang, pickLang] = useInfoLang();
  const content = useContent(contentKey, pickedLang);
  const langs = contentLanguages(contentKey);
  const doc = content.markdown != null ? splitDocument(content.markdown) : null;
  // Иврит — RTL; выравнивание тоже, иначе текст читается «наизнанку».
  const isRtl = content.lang === 'he';

  return (
    <>
      <UI_LangTabs lang={content.lang} onPick={pickLang}
        available={{ en: langs.en ? 'x' : '', ru: langs.ru ? 'x' : '', he: langs.he ? 'x' : '' }} />

      <div dir={isRtl ? 'rtl' : 'ltr'} lang={content.lang} style={{ textAlign: isRtl ? 'right' : 'left' }}>
        {doc == null ? (
          <div className="h-24" aria-busy="true" />
        ) : (
          <>
            {doc.title && <div className="mb-2 pr-6 text-[15px] font-bold">{doc.title}</div>}
            <UI_Markdown source={doc.body} variant="popup" className="text-[13px]" />
            {doc.footnote && (
              <UI_Markdown source={doc.footnote} variant="popup"
                className="mt-3 text-[11px] text-[var(--theme-mode-text-muted)]" />
            )}
          </>
        )}
      </div>
    </>
  );
};

const UI_InfoPopup: React.FC<InfoPopupProps> = ({ open, onClose, contentKey }) => {

  React.useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  if (!open) return null;

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
        // font-normal: попап стоит в DOM внутри ячейки времени (font-bold) и наследовал бы жирный.
        className="relative w-[min(92vw,34rem)] max-h-[80vh] overflow-y-auto rounded-2xl border p-5 font-normal
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

        <InfoPopupText contentKey={contentKey} />
      </div>
    </div>
  );
};

export default UI_InfoPopup;
