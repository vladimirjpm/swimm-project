import React, { useState } from 'react';
import { contentLanguages, useContent } from '../../../content/content';
import { DeepSheet, SheetButton, SheetError, SheetFooter } from '../../components/deep/deep-sheet';
import { UI_LangTabs, useInfoLang } from '../../components/info-popup/info-popup';
import UI_Markdown, { splitDocument } from '../../components/markdown/markdown';
import type { SaveResult } from '../my-groups-types';

/**
 * «Что значит вступить в группу» — шаг перед вступлением и перед заявкой (решение Влада
 * 14.11.2026): человек нажимал «Join», не понимая, что группа — это команда, которая тренируется
 * вместе, а не подписка на результаты (так в группу Дельфина попал посторонний).
 *
 * Объяснение — на трёх языках, текстом из `src/content/popups/group-join/{en,ru,he}.md` (правило
 * проекта: длинный текст — файлом). Язык — общий выбор объяснялок сайта (`useInfoLang`), иврит —
 * RTL. Кнопки остаются английскими, как весь интерфейс.
 *
 * Оболочка — `DeepSheet` (шторка на телефоне, модал на десктопе; класс темы на портале ставит он).
 * Само вступление делает вызывающий (`onJoin`): отказ сервера показывается здесь же, успех
 * закрывает диалог.
 */

const CONTENT_KEY = 'popups/group-join';

function GroupJoinDialog({
  approval, onJoin, onClose,
}: {
  /** Вступление по заявке: кнопка «Send request», а не «Join group». */
  approval: boolean;
  onJoin: () => Promise<SaveResult>;
  onClose: () => void;
}) {
  const [pickedLang, pickLang] = useInfoLang();
  const content = useContent(CONTENT_KEY, pickedLang);
  const langs = contentLanguages(CONTENT_KEY);
  const doc = content.markdown != null ? splitDocument(content.markdown) : null;
  const isRtl = content.lang === 'he';
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const join = async () => {
    setBusy(true);
    setError(null);
    const result = await onJoin();
    setBusy(false);
    if (result.success) onClose();
    else setError(result.error ?? 'Something went wrong. Please try again.');
  };

  return (
    <DeepSheet
      title={approval ? 'Request to join this group?' : 'Join this group?'}
      sub={approval ? 'The coach approves each request' : undefined}
      tone="accent"
      busy={busy}
      onClose={onClose}
      onSubmit={() => { void join(); }}
      footer={(
        <div className="flex flex-col gap-3.5">
          <SheetError error={error} />
          <SheetFooter>
            <SheetButton kind="cancel" onClick={onClose} disabled={busy}>Cancel</SheetButton>
            <SheetButton tone="accent" disabled={busy}>
              {busy ? 'Sending…' : approval ? 'Send request' : 'Join group'}
            </SheetButton>
          </SheetFooter>
        </div>
      )}
    >
      <div>
        <UI_LangTabs
          lang={content.lang}
          onPick={pickLang}
          available={{ en: langs.en ? 'x' : '', ru: langs.ru ? 'x' : '', he: langs.he ? 'x' : '' }}
        />
        <div dir={isRtl ? 'rtl' : 'ltr'} lang={content.lang} style={{ textAlign: isRtl ? 'right' : 'left' }}>
          {doc == null ? (
            <div className="h-40" aria-busy="true" />
          ) : (
            <>
              {doc.title && <div className="mb-2 text-[15px] font-bold">{doc.title}</div>}
              <UI_Markdown source={doc.body} variant="popup" className="text-[13px]" />
              {doc.footnote && (
                <UI_Markdown source={doc.footnote} variant="popup" className="mt-3 text-[11.5px] opacity-70" />
              )}
            </>
          )}
        </div>
      </div>
    </DeepSheet>
  );
}

export default GroupJoinDialog;
