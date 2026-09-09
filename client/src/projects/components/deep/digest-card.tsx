import React from 'react';

/**
 * Карточка дайджеста — витрина соседнего таба на табе `Overview`.
 *
 * Одна на все варианты сущности (клуб, пловец, группа): дайджест у них разный по содержимому,
 * но одинаковый по форме — заголовок, подпись, и ссылка «туда, где это целиком». Ссылка
 * обязательна по смыслу: карточка показывает СРЕЗ, и человек должен видеть, где остальное.
 *
 * Переход делает `nav.go` каркаса — он же поднимает страницу к верху «папки»
 * (`entity-page-types.ts`), поэтому кнопка здесь и не знает, куда её нажали.
 *
 * `count` — цифра среза («20 RECORDS»): ставится тем же бейджем, что у карточек времён клуба,
 * чтобы дайджест не заводил третий диалект счётчиков.
 */

interface Props {
  title: string;
  subtitle?: string;
  /** Подпись счётчика справа от заголовка («20 RECORDS»). Без `count` не рисуется. */
  count?: number | null;
  countLabel?: string;
  /** Текст ссылки-перехода («All 20 records →»). Без `onMore` не рисуется. */
  moreLabel?: string;
  onMore?: () => void;
  /** Что показать вместо тела, когда среза нет вовсе. */
  emptyText?: string;
  isEmpty?: boolean;
  children: React.ReactNode;
}

function DeepDigestCard({
  title, subtitle, count, countLabel, moreLabel, onMore, emptyText, isEmpty, children,
}: Props) {
  return (
    <section className="deep-card mb-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="deep-card-title">{title}</div>
          {subtitle && <div className="deep-card-sub mt-1">{subtitle}</div>}
        </div>

        {count != null && (
          <span className="deep-count-badge">
            <b>{count}</b> {countLabel}
          </span>
        )}
      </div>

      {isEmpty ? (
        <div className="mt-4 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          {emptyText ?? 'Nothing here yet.'}
        </div>
      ) : (
        <div className="mt-4">{children}</div>
      )}

      {moreLabel && onMore && (
        <button
          type="button"
          onClick={onMore}
          className="mt-3 cursor-pointer border-none bg-transparent p-0 text-[12px] font-extrabold"
          style={{ color: 'var(--deep-accent)' }}
        >
          {moreLabel}
        </button>
      )}
    </section>
  );
}

export default DeepDigestCard;
