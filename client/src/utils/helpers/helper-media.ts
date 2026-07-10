/**
 * Хелперы медиа-ссылок (галерея группы, медиа тренировок, лайтбокс):
 * извлечение id роликов и превью-картинок. Единственная копия регекспов —
 * используется в groups.tsx, training-by-session.tsx и UI_SwimmerGallery.
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
   * URL превью-картинки для тайла галереи: image — сам URL, youtube — статичный
   * кадр с img.youtube.com; для vimeo/other/album превью нет (null → иконка-заглушка).
   */
  static resolveThumbUrl(mediaType: string, sourceType: string, url: string): string | null {
    if (mediaType === 'image') return url;
    if (mediaType === 'video' && sourceType === 'youtube') {
      const id = HelperMedia.extractYoutubeId(url);
      return id ? `https://img.youtube.com/vi/${id}/hqdefault.jpg` : null;
    }
    return null;
  }
}
