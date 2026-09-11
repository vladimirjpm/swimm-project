/**
 * Хелперы медиа-ссылок (галерея группы, медиа тренировок, лайтбокс, фото шапки):
 * извлечение id роликов, превью-картинок и прямого адреса картинки. Единственная копия
 * регекспов — любая картинка ПО ССЫЛКЕ идёт в `<img src>` через `directImageUrl`.
 */
export default class HelperMedia {
  /** Извлечь id ролика YouTube из любой формы ссылки (watch / shorts / embed / youtu.be). */
  static extractYoutubeId(url?: string | null): string | null {
    if (!url) return null;
    const match = url.match(
      /(?:youtube\.com\/(?:watch\?v=|shorts\/|embed\/)|youtu\.be\/)([a-zA-Z0-9_-]+)/,
    );
    return match ? match[1] : null;
  }

  /** Извлечь числовой id ролика Vimeo из ссылки. */
  static extractVimeoId(url?: string | null): string | null {
    if (!url) return null;
    const match = url.match(/vimeo\.com\/(\d+)/);
    return match ? match[1] : null;
  }

  /**
   * Id файла Google Drive из ссылки «Поделиться» (`…/file/d/{id}/view`, в т.ч. `/file/u/0/d/`)
   * и из старых форм `open?id=` / `uc?id=` / `drive.usercontent…/download?id=`.
   * Папки и документы (`/drive/folders/`, `/document/d/`) — не файл-картинка, для них null.
   */
  static extractGoogleDriveFileId(url?: string | null): string | null {
    if (!url) return null;
    const match =
      url.match(/^https?:\/\/(?:drive|docs)\.google\.com\/file\/(?:u\/\d+\/)?d\/([\w-]+)/i)
      ?? url.match(/^https?:\/\/(?:drive|docs|drive\.usercontent)\.google\.com\/(?:open|uc|download)\?(?:[^#]*&)?id=([\w-]+)/i);
    return match ? match[1] : null;
  }

  /** Любая ссылка на Google Drive — для подсказки «откройте доступ по ссылке». */
  static isGoogleDriveUrl(url?: string | null): boolean {
    return !!url && /^https?:\/\/(?:drive|docs|drive\.usercontent)\.google\.com\//i.test(url.trim());
  }

  /**
   * Адрес для `<img src>`. Ссылка «Поделиться» из Google Drive ведёт на СТРАНИЦУ просмотрщика:
   * это HTML с кодом 200, и `<img>` его не покажет (у Влада так не показалось фото группы,
   * 11.09.2026). Такую ссылку переводим на отдачу самого файла — `lh3.googleusercontent.com/d/{id}`;
   * остальные адреса возвращаем как есть.
   *
   * В базе ссылка лежит сырой, какой её вставил человек, а переводится при показе: схема
   * прямых адресов Drive неофициальная, и если Google её сменит, чинить надо здесь, а не базу.
   * ⚠ Файл должен быть открыт «Anyone with the link» — иначе вместо картинки Google отдаёт
   * страницу входа, и посетитель видит битое фото, а сам хозяин (он залогинен) может видеть целое.
   * ⚠ `<img>` с такой ссылкой — ТОЛЬКО с `referrerPolicy="no-referrer"`: lh3 с заголовком
   * Referer через раз отвечает 429 HTML (замерено curl'ом 11.09.2026 — с `Referer: localhost`
   * 429/200 вперемешку, без него стабильно 200). Поэтому атрибут стоит у всех картинок по
   * ссылке, включая плитки из `resolveThumbUrl`; для YouTube-кадров и прочих хостов он безвреден.
   */
  static directImageUrl(url: string): string {
    const driveId = HelperMedia.extractGoogleDriveFileId(url.trim());
    return driveId ? `https://lh3.googleusercontent.com/d/${driveId}` : url;
  }

  /**
   * URL превью-картинки для тайла галереи: image — сам URL (Drive-ссылка → прямой адрес),
   * youtube — статичный кадр с img.youtube.com; для vimeo/other/album превью нет
   * (null → иконка-заглушка).
   */
  static resolveThumbUrl(mediaType: string, sourceType: string, url: string): string | null {
    if (mediaType === 'image') return HelperMedia.directImageUrl(url);
    if (mediaType === 'video' && sourceType === 'youtube') {
      const id = HelperMedia.extractYoutubeId(url);
      return id ? `https://img.youtube.com/vi/${id}/hqdefault.jpg` : null;
    }
    return null;
  }
}
