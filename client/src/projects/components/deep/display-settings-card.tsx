import React, { useState } from 'react';
import { HelperMedia } from '../../../utils/helpers';

/**
 * Настройки отображения страницы коллектива — содержимое таба `Admin` у клуба и у группы.
 *
 * Одна карточка на оба варианта: клуб и группа — два вида одного (коллектив пловцов), и
 * управление у них одинаковое. Разное только право решать, и его проверяет СЕРВЕР
 * (`PUT /api/display-settings/{type}/{id}`): у клуба — админ сайта, у группы — владелец,
 * админ группы или админ сайта. Клиент лишь не показывает таб тем, кому он не положен.
 *
 * Семантика запроса — полная замена: форма маленькая и всегда предзаполнена текущим
 * состоянием, поэтому шлём её целиком, а не патч.
 *
 * Под полем URL — проверка ссылки глазами посетителя (`PhotoUrlCheck` внизу файла).
 *
 * ⚠ После сохранения страница ПЕРЕЗАГРУЖАЕТСЯ. Это не лень: настройки меняют корпус страницы
 * (правая колонка шапки появляется или исчезает), а серверный ответ кэшируется — сервер
 * сбрасывает кэш при записи, и честный способ увидеть результат целиком это перечитать
 * страницу. Точечное обновление показывало бы состояние, которого ещё нет у соседа.
 */

/** Токен antiforgery: свой кэш на модуль — как у остальных мутирующих клиентов проекта. */
let cachedToken: string | null = null;

async function apiPut(url: string, body: unknown): Promise<boolean> {
  if (!cachedToken) {
    try {
      const r = await fetch('/api/antiforgery/token', { credentials: 'include' });
      cachedToken = r.ok ? (await r.json()).token ?? null : null;
    } catch {
      cachedToken = null;
    }
  }

  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  if (cachedToken) headers['X-XSRF-TOKEN'] = cachedToken;

  const res = await fetch(url, {
    method: 'PUT', credentials: 'include', headers, body: JSON.stringify(body),
  });
  if (!res.ok) cachedToken = null;
  return res.ok;
}

/** Элемент ленты медиа для пикера «взять фото из медиа». */
export interface DisplayMediaItem {
  id: number;
  url: string;
  media_type: string;
  source_type: string;
  caption?: string | null;
}

interface Props {
  entity: 'club' | 'group';
  entityId: number;
  /** СЫРОЙ url обложки (не разрешённый hero_image_url) — форма правит именно его. */
  coverImageUrl?: string | null;
  showHeroImage: boolean;
  heroMediaId?: number | null;
  /**
   * Лента медиа сущности; пикер берёт из неё только КАРТИНКИ. Пусто (или одни видео) —
   * пикера нет вовсе: у клуба медиа-ленты не существует
   * (docs/plans/entity-page-shell-plan.md §3.10), и рисовать пустой выбор нечестно.
   */
  media?: DisplayMediaItem[];
}

function DeepDisplaySettingsCard({
  entity, entityId, coverImageUrl, showHeroImage, heroMediaId, media = [],
}: Props) {
  // Выбирать можно только КАРТИНКИ: ссылка на видео ушла бы в <img src> битой. Превью с
  // YouTube клиент считать умеет, но сервер — нет, а решает указатель именно он.
  const photos = media.filter((m) => m.media_type === 'image');
  const [show, setShow] = useState(showHeroImage);
  const [url, setUrl] = useState(coverImageUrl ?? '');
  const [mediaId, setMediaId] = useState<number | null>(heroMediaId ?? null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const dirty =
    show !== showHeroImage
    || url.trim() !== (coverImageUrl ?? '').trim()
    || mediaId !== (heroMediaId ?? null);

  const save = async () => {
    setBusy(true);
    setError(null);
    const ok = await apiPut(`/api/display-settings/${entity}/${entityId}`, {
      showHeroImage: show,
      heroMediaId: mediaId,
      coverImageUrl: url.trim() ? url.trim() : null,
    });
    setBusy(false);
    if (ok) window.location.reload();
    else setError('Could not save. You may not have rights on this page.');
  };

  return (
    <section className="deep-card mb-4" aria-label="Page display">
      <div className="deep-card-title">Page display</div>
      <div className="deep-card-sub mt-1">hero photo of this page</div>

      <label className="mt-4 flex cursor-pointer items-center gap-2.5">
        <input
          type="checkbox"
          checked={show}
          onChange={(e) => setShow(e.target.checked)}
          className="h-4 w-4 cursor-pointer"
        />
        <span className="text-[13px] font-extrabold" style={{ color: 'var(--deep-text)' }}>
          Show hero photo
        </span>
      </label>
      <p className="mt-1 text-[11.5px] font-bold" style={{ color: 'var(--deep-text-ghost)' }}>
        Off — the right column collapses and the header goes full width. On without a photo —
        a placeholder is shown.
      </p>

      <label className="mt-4 block">
        <span className="text-[11.5px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--deep-text-mute)' }}>
          Photo URL
        </span>
        <input
          type="url"
          value={url}
          onChange={(e) => setUrl(e.target.value)}
          placeholder="https://…"
          disabled={mediaId != null}
          className="mt-1 w-full rounded-[10px] border px-3 py-2 text-[13px] font-bold disabled:opacity-40"
          style={{
            borderColor: 'var(--deep-card-border)',
            background: 'var(--deep-card-bg-row)',
            color: 'var(--deep-text)',
          }}
        />
      </label>

      {mediaId == null && url.trim() !== '' && <PhotoUrlCheck url={url} />}

      {photos.length > 0 && (
        <div className="mt-4">
          <div className="text-[11.5px] font-extrabold uppercase tracking-wide" style={{ color: 'var(--deep-text-mute)' }}>
            …or take it from media
          </div>
          <div className="mt-2 grid grid-cols-4 gap-2 sm:grid-cols-6">
            {photos.map((m) => {
              const thumb = HelperMedia.resolveThumbUrl(m.media_type, m.source_type, m.url);
              const active = mediaId === m.id;
              return (
                <button
                  key={m.id}
                  type="button"
                  onClick={() => setMediaId(active ? null : m.id)}
                  title={m.caption ?? undefined}
                  className="aspect-square cursor-pointer overflow-hidden rounded-[10px] border p-0"
                  style={{
                    borderColor: active ? 'var(--deep-accent)' : 'var(--deep-card-border)',
                    borderWidth: active ? 2 : 1,
                    background: 'var(--deep-card-bg-row)',
                  }}
                >
                  {thumb
                    ? <img loading="lazy" src={thumb} referrerPolicy="no-referrer" alt="" className="h-full w-full object-cover" />
                    : <span className="text-[20px]">🎬</span>}
                </button>
              );
            })}
          </div>
          <p className="mt-2 text-[11.5px] font-bold" style={{ color: 'var(--deep-text-ghost)' }}>
            {mediaId != null
              ? 'Taken from media — click the selected one again to go back to the URL.'
              : 'Nothing selected — the URL above is used.'}
          </p>
        </div>
      )}

      {error && (
        <p className="mt-3 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }}>{error}</p>
      )}

      <button
        type="button"
        disabled={busy || !dirty}
        onClick={save}
        className="deep-cta mt-4 px-4 py-2 text-[13px] disabled:opacity-40"
      >
        {busy ? 'Saving…' : 'Save'}
      </button>
    </section>
  );
}

/**
 * Проверка ссылки на фото — глазами ПОСЕТИТЕЛЯ, а не админа. Повод: ссылка «Поделиться» из
 * Google Drive отдаёт код 200, но это страница просмотрщика, и фото на витрине битое
 * (11.09.2026). Сам перевод такой ссылки в картинку делает `HelperMedia.directImageUrl`.
 *
 * Картинку грузит браузер. Серверной проверки нет сознательно: сервер, скачивающий любой
 * адрес по просьбе админа группы, — это SSRF. Хосты Google грузим анонимно
 * (`crossOrigin="anonymous"` = без кук): админ залогинен в Google, и ЗАКРЫТЫЙ файл Drive у
 * него показался бы целым, а у посетителя — нет. Google отдаёт `Access-Control-Allow-Origin: *`,
 * поэтому открытый файл в этом режиме грузится. Остальным хостам анонимный режим не включаем:
 * без CORS-заголовка он уронил бы и рабочую картинку.
 *
 * Это предупреждение, а не запрет: сохранить можно и битую ссылку — хост мог прилечь на
 * минуту, а доступ в Drive человек откроет потом (для того и «Check again»).
 */
function PhotoUrlCheck({ url }: { url: string }) {
  const [attempt, setAttempt] = useState(0);
  // Результат помнит, К ЧЕМУ он относится: сменилась ссылка или нажали «Check again» — старый
  // ответ не подходит, и состояние само становится «checking», без эффекта-сброса (эффект
  // гонялся бы с onLoad закэшированной картинки).
  const [checked, setChecked] = useState<{ key: string; ok: boolean } | null>(null);

  const trimmed = url.trim();
  if (!/^https?:\/\//i.test(trimmed)) {
    return (
      <p className="mt-2 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }}>
        The link must start with https://
      </p>
    );
  }

  const src = HelperMedia.directImageUrl(trimmed);
  const isDrive = HelperMedia.isGoogleDriveUrl(trimmed);
  const anonymous = isDrive || /^https?:\/\/[^/]*\.googleusercontent\.com\//i.test(src);
  const key = `${attempt}|${src}`;
  const state: 'checking' | 'ok' | 'broken' =
    checked?.key !== key ? 'checking' : checked.ok ? 'ok' : 'broken';

  const shareSteps = <b>Share → General access → Anyone with the link</b>;
  let hint: React.ReactNode = null;
  if (isDrive && HelperMedia.extractGoogleDriveFileId(trimmed) == null) {
    hint = 'This is a Google Drive folder or document, not an image. Open the image itself in Drive and copy its link.';
  } else if (isDrive && state === 'broken') {
    hint = <>Google Drive: open the file → {shareSteps}. Then press Check again.</>;
  } else if (isDrive) {
    hint = <>Google Drive: keep the file shared — {shareSteps}. Otherwise only you will see it.</>;
  } else if (state === 'broken') {
    hint = 'Paste a direct link to the image file (it usually ends in .jpg or .png), not a link to a page that shows it.';
  }

  const statusColor =
    state === 'ok' ? 'var(--deep-accent)' : state === 'broken' ? 'var(--deep-danger)' : 'var(--deep-text-mute)';

  return (
    <div className="mt-3 flex items-start gap-3" aria-live="polite">
      <div
        className="relative h-[72px] w-[128px] shrink-0 overflow-hidden rounded-[10px] border"
        style={{ borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg-row)' }}
      >
        <img
          key={key}
          src={src}
          crossOrigin={anonymous ? 'anonymous' : undefined}
          referrerPolicy="no-referrer"
          alt=""
          onLoad={() => setChecked({ key, ok: true })}
          onError={() => setChecked({ key, ok: false })}
          className={`h-full w-full object-cover ${state === 'ok' ? '' : 'invisible'}`}
        />
        {state !== 'ok' && (
          <span
            className="absolute inset-0 flex items-center justify-center text-[18px] font-black"
            style={{ color: state === 'broken' ? 'var(--deep-danger)' : 'var(--deep-text-ghost)' }}
            aria-hidden="true"
          >
            {state === 'broken' ? '✕' : '…'}
          </span>
        )}
      </div>

      <div className="min-w-0 flex-1">
        <div className="text-[12.5px] font-extrabold" style={{ color: statusColor }}>
          {state === 'checking' && 'Checking the link…'}
          {state === 'ok' && '✓ Visitors will see this photo'}
          {state === 'broken' && "✕ Visitors won't see this photo — the link doesn't open as an image"}
        </div>
        {hint && (
          <p
            className="mt-1 text-[11.5px] font-bold"
            style={{ color: state === 'broken' ? 'var(--deep-text)' : 'var(--deep-text-mute)' }}
          >
            {hint}
          </p>
        )}
        {state === 'broken' && (
          <button
            type="button"
            onClick={() => setAttempt((n) => n + 1)}
            className="mt-2 cursor-pointer rounded-[9px] border px-3 py-1.5 text-[12px] font-extrabold hover:brightness-110"
            style={{
              background: 'var(--deep-accent-chip)',
              borderColor: 'var(--deep-accent-border)',
              color: 'var(--deep-accent)',
            }}
          >
            Check again
          </button>
        )}
      </div>
    </div>
  );
}

export default DeepDisplaySettingsCard;
