import { renderPrintPng, type PrintSize } from '../print/print-export';
import { COVER_H, COVER_W } from './cover-theme';

// Экспорт обложки — общий `print/print-export.ts`; здесь только её размер и лимит YouTube.

/** Файл обложки: ровно 1280×720. */
export const COVER_SIZE: PrintSize = { width: COVER_W, height: COVER_H, pixelRatio: 1 };

/** Лимит YouTube на обложку при загрузке С ТЕЛЕФОНА; с компьютера — 50 МБ (справка, 12.11.2026). */
export const YOUTUBE_MOBILE_LIMIT_BYTES = 2 * 1024 * 1024;

/** Снимает узел обложки в PNG 1280×720. */
export const renderCoverPng = (node: HTMLElement): Promise<Blob> => renderPrintPng(node, COVER_SIZE);
