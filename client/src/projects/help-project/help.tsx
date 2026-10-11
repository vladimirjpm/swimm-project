import React, { useEffect } from 'react';
import '../home-project/home.css';
import '../components/deep/deep-theme.css';
import { useDeepThemeClass } from '../components/deep/use-deep-theme-class';
import AppTopbar from '../components/app-topbar/app-topbar';
import UI_ModeToggle from '../components/mix/mode-toggle/mode-toggle';
import { UI_LangTabs, useInfoLang } from '../components/info-popup/info-popup';
import UI_Markdown, { splitTitle } from '../components/markdown/markdown';
import { contentLanguages, useContent } from '../../content/content';
import { parseRoute, routes } from '../../utils/routes';

/**
 * Страница справки `/help/{key}` — текст из `src/content/help/{key}/{en,ru,he}.md`
 * (решение Влада 11.10.2026: длинные тексты — markdown-файлами на трёх языках).
 *
 * Язык — тот же общий выбор, что у попапов-объяснялок (`useInfoLang`, localStorage): кто
 * переключился на иврит в попапе, видит справку на иврите сразу. Нет перевода — английский,
 * вкладка погашена. Иврит — справа налево.
 *
 * Первая справка — памятка «Do you need a group?» (`/help/groups`), ссылка с `/groups`.
 */

/** Куда ведёт «назад» у справки раздела; у справки без раздела — на главную. */
const BACK: Record<string, { label: string; href: string }> = {
  groups: { label: 'Groups', href: routes.groupsList() },
};

function Help() {
  const deep = useDeepThemeClass();
  const key = parseRoute().helpKey ?? '';
  const contentKey = `help/${key}`;
  const [pickedLang, pickLang] = useInfoLang();
  const content = useContent(contentKey, pickedLang);
  const langs = contentLanguages(contentKey);
  const { title, body } = content.markdown != null
    ? splitTitle(content.markdown)
    : { title: null, body: '' };
  const back = BACK[key];
  const isRtl = content.lang === 'he';

  useEffect(() => {
    if (title) document.title = title;
  }, [title]);

  return (
    <div className={`${deep} relative min-h-screen overflow-x-clip pb-24 text-[var(--t-text)]`}
      style={{ background: 'var(--deep-hero-grad)' }}>
      <AppTopbar active={key === 'groups' ? 'groups' : undefined} />
      <main className="mx-auto w-full max-w-[760px] px-4 pt-24">
        {back && (
          <a href={back.href} className="flex items-center gap-1 text-[13px] text-[var(--t-text-3)] hover:text-[var(--t-text-2)]">
            <span aria-hidden="true">‹</span> {back.label}
          </a>
        )}

        {content.missing ? (
          <div className="deep-card mt-6">
            <h1 className="deep-card-title mb-2">Page not found</h1>
            <p className="m-0 text-[13px] text-[var(--t-text-2)]">There is no help page at this address.</p>
          </div>
        ) : (
          <article className="mt-4 sm:rounded-[14px] sm:border sm:border-[var(--t-border)] sm:bg-[var(--t-card)] sm:p-8">
            <UI_LangTabs lang={content.lang} onPick={pickLang}
              available={{ en: langs.en ? 'x' : '', ru: langs.ru ? 'x' : '', he: langs.he ? 'x' : '' }} />
            <div dir={isRtl ? 'rtl' : 'ltr'} lang={content.lang} className={isRtl ? 'text-right' : 'text-left'}>
              {title && <h1 className="mt-2 mb-4 text-[26px] font-medium text-[var(--t-text)]">{title}</h1>}
              {content.markdown == null
                ? <div className="h-40" aria-busy="true" />
                : <UI_Markdown source={body} />}
            </div>
          </article>
        )}
      </main>
      <UI_ModeToggle />
    </div>
  );
}

export default Help;
