// Фаза 3.5 (строгий вариант) — нагрузочный бенчмарк по ВСЕЙ синтетике (~3.01M строк).
//
// В отличие от paged-smoke.js (тот бьёт competitionId=last — маленькое последнее соревнование),
// здесь запросы НЕ ограничены источником: каждая группа целенаправленно нагружает всю таблицу
// Results, а ключи кэша варьируются на каждой итерации, чтобы мерить именно КЭШ-ПРОМАХ на 3M.
//
//   global-list     — глобальный список без фильтра (COUNT+ORDER BY по всей таблице).
//                     Кэш-ключ один → после первого запроса это hit; группа документирует
//                     холодную стоимость (см. README), а не устойчивый промах.
//   global-filtered — список с ротацией style×distance×gender×poolType по большому набору
//                     комбинаций → устойчивые свежие ключи = промахи по подмножествам 3M.
//   global-athlete  — карточка спортсмена по ротации реальных имён → скан по имени по всей
//                     таблице (у имени нет индекса) на каждый свежий ключ.
//
// Запуск: k6 run server/loadtest/full-scan-smoke.js  (BASE_URL=http://localhost:5079 по умолч.).

import http from 'k6/http';
import { check, group } from 'k6';
import { Trend } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:5079';

const listTrend = new Trend('global_list_duration', true);
const filteredTrend = new Trend('global_filtered_duration', true);
const athleteTrend = new Trend('global_athlete_duration', true);

export const options = {
  stages: [
    { duration: '20s', target: 10 }, // прогрев планов PG + OS page cache
    { duration: '40s', target: 20 },
    { duration: '40s', target: 20 },
    { duration: '15s', target: 0 },
  ],
  // ВНИМАНИЕ: это ДИАГНОСТИЧЕСКИЙ профиль, не green-gate (в отличие от paged-smoke.js).
  // Пороги — характеристики ИЗМЕРЕННОГО baseline (2 прогона, 20 VU, 3.01M строк), а НЕ
  // целевой бюджет. Цель по-прежнему p95<300, но НЕ достигнута на несоскоуп­ленном пути —
  // см. README раздел «Обрыв на несоскоупленных запросах». Зелёный тут = «не хуже
  // известного baseline»; красный = регрессия сверх него. Настоящее «зелёное» по 300мс
  // даёт paged-smoke.js (реальное соскоупленное usage — 6мс).
  thresholds: {
    http_req_failed: ['rate<0.01'],
    global_list_duration: ['p(95)<300'],           // кэш-ключ один → hit; реально быстрый
    global_filtered_duration: ['p(95)<18000'],     // baseline ~14с (!), цель 300 — НЕ достигнута
    global_athlete_duration: ['p(95)<6000'],       // холодный скан по имени; тёплый — мс
  },
};

export function setup() {
  // Наборы значений фильтров — из filter-hints (реальные значения синтетики).
  const styleRes = http.get(`${BASE}/api/results/filter-hints?field=style&limit=20`);
  const distRes = http.get(`${BASE}/api/results/filter-hints?field=distance&limit=20`);
  let styles = [];
  let distances = [];
  try { styles = JSON.parse(styleRes.body); } catch (e) { styles = []; }
  try { distances = JSON.parse(distRes.body); } catch (e) { distances = []; }
  if (!styles.length) styles = ['freestyle', 'backstroke', 'butterfly', 'breaststroke'];
  if (!distances.length) distances = ['50', '100', '200'];

  const genders = ['male', 'female'];
  const pools = ['25m', '50m'];

  // ~200+ реальных имён для ротации athlete — из большой страницы результатов.
  const namesRes = http.get(`${BASE}/api/results?page=1&pageSize=500`);
  let names = [];
  try {
    const body = JSON.parse(namesRes.body);
    if (body && body.data) {
      const set = {};
      for (const r of body.data) set[`${r.first_name} ${r.last_name}`] = true;
      names = Object.keys(set);
    }
  } catch (e) { names = []; }
  if (!names.length) names = ['Tami SELA'];

  return { styles, distances, genders, pools, names };
}

export default function (data) {
  const { styles, distances, genders, pools, names } = data;
  const seq = __VU * 100000 + __ITER;

  group('global-list', function () {
    const res = http.get(`${BASE}/api/results?page=1&pageSize=100`);
    listTrend.add(res.timings.duration);
    check(res, {
      'list: 200': (r) => r.status === 200,
      'list: not empty': (r) => r.body && r.body.length > 0,
    });
  });

  group('global-filtered', function () {
    // Свежая комбинация на каждую итерацию каждого VU (по всему пространству combos).
    const style = styles[seq % styles.length];
    const distance = distances[Math.floor(seq / styles.length) % distances.length];
    const gender = genders[Math.floor(seq / (styles.length * distances.length)) % genders.length];
    const pool = pools[Math.floor(seq / (styles.length * distances.length * genders.length)) % pools.length];
    const url = `${BASE}/api/results?styleName=${encodeURIComponent(style)}`
      + `&distance=${encodeURIComponent(distance)}&gender=${gender}&poolType=${pool}`
      + `&page=1&pageSize=100`;
    const res = http.get(url);
    filteredTrend.add(res.timings.duration);
    check(res, { 'filtered: 200': (r) => r.status === 200 });
  });

  group('global-athlete', function () {
    // Ротация имён → свежий кэш-ключ = скан по имени по всей таблице каждый раз.
    const name = names[seq % names.length];
    const res = http.get(`${BASE}/api/athletes/career?name=${encodeURIComponent(name)}`);
    athleteTrend.add(res.timings.duration);
    check(res, { 'athlete: 200': (r) => r.status === 200 });
  });
}
