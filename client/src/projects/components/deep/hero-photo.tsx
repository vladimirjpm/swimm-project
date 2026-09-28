import React, { useState } from 'react';
import UI_SwimmerGallery from '../mix/swimmer-gallery/swimmer-gallery';
import { HelperMedia } from '../../../utils/helpers';

/**
 * Место под фото шапки сущности (колонка `aside` у `DeepHeroBand`) — одно на клуб и группу.
 *
 * ОДНА рамка, внутри либо фото, либо заглушка (решение Влада 28.09.2026): место ведёт себя
 * одинаково — справа на десктопе, сверху от края до края на телефоне, — пустое оно или нет.
 *
 * Фото — кнопка: клик открывает его крупно в том же лайтбоксе, что у тайлов таба Media
 * (`UI_SwimmerGallery` в контролируемом режиме). Ссылку Google Drive (страница просмотрщика)
 * переводит в картинку `HelperMedia` — и здесь, и в лайтбоксе.
 *
 * Нет ссылки — заглушка, а не схлопнутая колонка (решение Влада 09.09.2026,
 * docs/plans/entity-page-shell-plan.md §3.9, подтверждено 28.09.2026 вопреки хендоффу
 * group-club-changes, где блока нет): шапка не прыгает между сущностями, и админу видно,
 * куда класть картинку.
 *
 * Мобильное фото (хендофф group-club-changes, HERO-PHOTO.md): уже 960px показывается
 * `mobileUrl` в полную высоту 292px (4:3 на 390px); его нет — десктопное фото ужимается в
 * полосу 180px с обрезкой по центру. Переключение — `<picture>` и классы, без JS-медиазапросов.
 * На десктопе мобильное фото не используется никогда. Заглушка на телефоне — той же полосой.
 */

interface Props {
  url: string | null | undefined;
  /** Отдельное фото для телефона (4:3); null — на телефоне полоса из десктопного. */
  mobileUrl?: string | null;
  /** Подпись заглушки: «No club photo yet» / «No group photo yet». */
  placeholder: string;
}

/** Порог тот же, что у колонок `DeepHeroBand`: уже него фото встаёт над именем. */
const MOBILE_MEDIA = '(max-width: 959px)';

function DeepHeroPhoto({ url, mobileUrl, placeholder }: Props) {
  const [open, setOpen] = useState(false);
  // Полное мобильное фото — только когда есть и десктопное: без него на десктопе заглушка,
  // а на телефоне фото, и это была бы ровно та «прыгающая шапка», от которой заглушка.
  const fullMobile = !!url && !!mobileUrl;

  return (
    <div
      // На телефоне место под фото от края до края — без скругления и рамки.
      className={`flex w-full items-center justify-center overflow-hidden rounded-2xl border max-sm:rounded-none max-sm:border-0 min-[960px]:h-full min-[960px]:min-h-[300px] ${
        fullMobile ? 'max-[959px]:h-[292px]' : 'max-[959px]:h-[180px]'
      }${url ? '' : ' border-dashed'}`}
      style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg-row)' }}
    >
      {url ? (
        <>
          <button
            type="button"
            onClick={() => setOpen(true)}
            aria-label="Open photo"
            className="block h-full w-full cursor-zoom-in border-0 bg-transparent p-0"
          >
            <picture>
              {fullMobile && (
                <source media={MOBILE_MEDIA} srcSet={HelperMedia.directImageUrl(mobileUrl!)} />
              )}
              <img
                src={HelperMedia.directImageUrl(url)}
                referrerPolicy="no-referrer"
                alt=""
                className="block h-full w-full object-cover object-center min-[960px]:min-h-[300px]"
              />
            </picture>
          </button>
          <UI_SwimmerGallery
            gallery={[{ type: 'image', url }]}
            popupSize="xl"
            openIndex={open ? 0 : null}
            onClose={() => setOpen(false)}
          />
        </>
      ) : (
        <span className="flex flex-col items-center gap-2" aria-hidden="true">
          <span className="text-[28px]">🏊</span>
          <span className="text-[11.5px] font-extrabold" style={{ color: 'var(--deep-text-ghost)' }}>
            {placeholder}
          </span>
        </span>
      )}
    </div>
  );
}

export default DeepHeroPhoto;
