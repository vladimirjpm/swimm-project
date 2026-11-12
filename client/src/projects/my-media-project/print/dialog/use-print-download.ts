import { useCallback, useState } from 'react';
import { downloadBlob, renderPrintPng, type PrintSize } from '../print-export';

/**
 * Скачивание картинки (обложка, плакат): снимает узел в PNG и отдаёт файл. Держит то, что
 * диалог показывает вокруг кнопки: «идёт работа», ошибку и вес последнего сохранённого файла.
 */
export function usePrintDownload() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedBytes, setSavedBytes] = useState<number | null>(null);

  const download = useCallback(async (node: HTMLElement | null, size: PrintSize, fileName: string) => {
    if (!node) return;
    setBusy(true);
    setError(null);
    try {
      const blob = await renderPrintPng(node, size);
      downloadBlob(blob, `${fileName}.png`);
      setSavedBytes(blob.size);
    } catch {
      setError('Could not make the image. Try again, or try another browser.');
    } finally {
      setBusy(false);
    }
  }, []);

  /** Картинку поменяли — вес прежнего файла к ней больше не относится. */
  const forgetSaved = useCallback(() => setSavedBytes(null), []);

  return { busy, error, setError, savedBytes, forgetSaved, download };
}
