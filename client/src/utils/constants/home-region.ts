/**
 * «Домашний» регион приложения — единственная точка, где клиент знает, чьи
 * национальные рекорды запрашивать и как подписывать колонки/карточки NR.
 * Код — alpha-3 World Aquatics, как везде в данных (см. docs/ARCHITECTURE.md,
 * решение 2026-07-13). Смена страны деплоя = правка этих двух констант
 * (+ RecordsImport:WorldAquaticsNationalCountryId на сервере).
 */
export const HOME_REGION = 'ISR';
export const HOME_REGION_LABEL = 'Israel';
