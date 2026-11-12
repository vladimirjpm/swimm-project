import { useCallback, useState } from 'react';
import { downloadBlob, renderCoverPng } from '../cover-export';

/**
 * Скачивание обложки: снимает узел в PNG и отдаёт файл. Держит то, что диалог показывает
 * вокруг кнопки: «идёт работа», ошибку и вес последнего сохранённого файла.
 */
export function useCoverDownload() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedBytes, setSavedBytes] = useState<number | null>(null);

  const download = useCallback(async (node: HTMLElement | null, fileName: string) => {
    if (!node) return;
    setBusy(true);
    setError(null);
    try {
      const blob = await renderCoverPng(node);
      downloadBlob(blob, `${fileName}.png`);
      setSavedBytes(blob.size);
    } catch {
      setError('Could not make the image. Try again, or try another browser.');
    } finally {
      setBusy(false);
    }
  }, []);

  /** Обложку поменяли — вес прежнего файла к ней больше не относится. */
  const forgetSaved = useCallback(() => setSavedBytes(null), []);

  return { busy, error, setError, savedBytes, forgetSaved, download };
}
