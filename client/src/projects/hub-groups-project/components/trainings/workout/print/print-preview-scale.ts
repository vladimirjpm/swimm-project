import { A4_HEIGHT, A4_WIDTH } from './print-fit';
import type { PrintOrientation } from './print-mode';

/** Размер листа в CSS-пикселях: книжный A4 или альбомный. */
export function sheetSize(orientation: PrintOrientation): { width: number; height: number } {
  return orientation === 'landscape'
    ? { width: A4_HEIGHT, height: A4_WIDTH }
    : { width: A4_WIDTH, height: A4_HEIGHT };
}

/**
 * Масштаб листа в окне предпросмотра: целиком в доступную область, но не крупнее натуральной
 * величины. Область ещё не замерена (0) — натуральная величина, чтобы не прятать лист.
 */
export function previewScale(availW: number, availH: number, orientation: PrintOrientation): number {
  if (!(availW > 0 && availH > 0)) return 1; // 0 и NaN (не замерено)
  const { width, height } = sheetSize(orientation);
  return Math.max(0.1, Math.min(1, availW / width, availH / height));
}
