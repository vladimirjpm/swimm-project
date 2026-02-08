/**
 * Хелпер для работы со временем
 */
export default class HelperTime {
  /**
   * Парсит строку времени в секунды
   * @example "1:23.45" -> 83.45
   */
  static parseTimeToSeconds(time: string | number): number {
    if (time === null || time === undefined) return Infinity;
    if (typeof time === 'number' && isFinite(time)) return time;
    if (typeof time !== 'string') return Infinity;

    const parts = time.trim().split(':');

    if (parts.length === 2) {
      const [minPart, secPart] = parts;
      const minutes = parseInt(minPart, 10);
      const seconds = parseFloat(secPart.replace(',', '.'));
      return isNaN(minutes) || isNaN(seconds) ? Infinity : minutes * 60 + seconds;
    }

    if (parts.length === 1) {
      const seconds = parseFloat(parts[0].replace(',', '.'));
      return isNaN(seconds) ? Infinity : seconds;
    }

    return Infinity;
  }

  /**
   * Форматирует секунды в строку времени
   * @example 83.45 -> "1:23.45"
   */
  static formatSecondsToTimeString(totalSeconds: number): string {
    if (!isFinite(totalSeconds)) return '—';

    const minutes = Math.floor(totalSeconds / 60);
    const seconds = Math.floor(totalSeconds % 60);
    const hundredths = Math.round((totalSeconds - minutes * 60 - seconds) * 100);

    if (minutes > 0) {
      return `${minutes}:${seconds.toString().padStart(2, '0')}.${hundredths.toString().padStart(2, '0')}`;
    }
    return `${seconds}.${hundredths.toString().padStart(2, '0')}`;
  }
}
