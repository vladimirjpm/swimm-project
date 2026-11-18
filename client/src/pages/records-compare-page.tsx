import { parseRecordsCompareQuery, routes } from '../utils/routes';

/**
 * `/records/compare` — бывшая отдельная страница сравнения двух стран. С 18.11.2026 это таб
 * H2H страницы `/records` (просьба Влада), а этот адрес только переадресует туда, сохраняя
 * пару и разрез: на него ведут старые ссылки, закладки и плитка «H2H records» из макетов.
 *
 * `replace`, а не `assign`: промежуточный адрес не должен оставаться в истории, иначе «назад»
 * снова приводил бы сюда и тут же уводил вперёд.
 */
window.location.replace(routes.recordsCompare(parseRecordsCompareQuery()));

export {};
