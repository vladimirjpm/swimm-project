// Фото пловца для обложки: файл с устройства → data-URL. Файл остаётся в браузере и на сервер
// не уходит (решение Влада 12.11.2026).

/** Сторона, до которой уменьшается фото: больше обложке не нужно (слот около 500px). */
const PHOTO_MAX_SIDE = 1600;
const PHOTO_JPEG_QUALITY = 0.9;

/**
 * Уменьшаем сразу: снимок с телефона — это 4000px и 5 МБ, а в обложку он вшивается целиком и
 * раздул бы каждый рендер. Бросает ошибку, если браузер не смог прочитать файл как картинку.
 */
export async function readCoverPhoto(file: File): Promise<string> {
  const bitmap = await createImageBitmap(file);
  const scale = Math.min(1, PHOTO_MAX_SIDE / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Canvas is not available');
  ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  return canvas.toDataURL('image/jpeg', PHOTO_JPEG_QUALITY);
}
