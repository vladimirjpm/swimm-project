// Фаза 3.5 — нагрузочный k6-смоук paged API (/api/results, /api/results/filter-hints, /api/athletes/career).
// Профиль: list (голый список) + filtered (список с 3 фильтрами, свежий кэш-ключ на каждую
// комбинацию) + athlete (карточка спортсмена по реальному имени из данных).
// Запуск: k6 run server/loadtest/paged-smoke.js (BASE_URL=http://localhost:5079 по умолчанию).

import http from 'k6/http';
import { check, group } from 'k6';
import { Trend } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:5079';

// Свои Trend-метрики на группу — так пороги применяются именно к нужному шагу, а не ко всем
// http-запросам подряд.
const listTrend = new Trend('list_duration', true);
const filteredTrend = new Trend('filtered_duration', true);
const athleteTrend = new Trend('athlete_duration', true);

export const options = {
  stages: [
    { duration: '30s', target: 10 },
    { duration: '30s', target: 20 },
    { duration: '30s', target: 20 },
    { duration: '15s', target: 0 },
  ],
  thresholds: {
    http_req_failed: ['rate<0.01'],
    list_duration: ['p(95)<300'],
    filtered_duration: ['p(95)<300'],
    athlete_duration: ['p(95)<300'],
  },
};

// setup() выполняется один раз до старта VU — тянет реальные наборы фильтров и реальное имя
// спортсмена из живых данных (синтетика SYNTH), чтобы не гадать значения руками.
export function setup() {
  const styleRes = http.get(`${BASE}/api/results/filter-hints?field=style&limit=20`);
  const distanceRes = http.get(`${BASE}/api/results/filter-hints?field=distance&limit=20`);

  let styles = [];
  let distances = [];
  try { styles = JSON.parse(styleRes.body); } catch (e) { styles = []; }
  try { distances = JSON.parse(distanceRes.body); } catch (e) { distances = []; }
  if (!styles || styles.length === 0) styles = ['freestyle'];
  if (!distances || distances.length === 0) distances = ['100'];

  const genders = ['male', 'female'];

  // Реальное имя для карточки athlete — из первой страницы результатов последнего соревнования.
  const listRes = http.get(`${BASE}/api/results?competitionId=last&page=1&pageSize=5`);
  let athleteName = null;
  try {
    const body = JSON.parse(listRes.body);
    if (body && body.data && body.data.length > 0) {
      const row = body.data[0];
      athleteName = `${row.first_name} ${row.last_name}`;
    }
  } catch (e) {
    athleteName = null;
  }

  return { styles, distances, genders, athleteName };
}

export default function (data) {
  const styles = data.styles;
  const distances = data.distances;
  const genders = data.genders;

  group('list', function () {
    const res = http.get(`${BASE}/api/results?competitionId=last&page=1&pageSize=100`);
    listTrend.add(res.timings.duration);
    check(res, {
      'list: status 200': (r) => r.status === 200,
      'list: body not empty': (r) => r.body && r.body.length > 0,
    });
  });

  group('filtered', function () {
    // Ротация комбинации фильтров по VU/ITER — цель: каждая итерация каждого VU бьёт по свежему
    // кэш-ключу (промах), а не повторяет уже закэшированную комбинацию.
    const idx = (__VU * 1000 + __ITER) % (styles.length * distances.length * genders.length);
    const style = styles[idx % styles.length];
    const distance = distances[Math.floor(idx / styles.length) % distances.length];
    const gender = genders[Math.floor(idx / (styles.length * distances.length)) % genders.length];

    const url = `${BASE}/api/results?competitionId=last&styleName=${encodeURIComponent(style)}&distance=${encodeURIComponent(distance)}&gender=${encodeURIComponent(gender)}&page=1&pageSize=100`;
    const res = http.get(url);
    filteredTrend.add(res.timings.duration);
    check(res, {
      'filtered: status 200': (r) => r.status === 200,
      'filtered: body not empty': (r) => r.body && r.body.length > 0,
    });
  });

  group('athlete', function () {
    const name = data.athleteName || 'Unknown Swimmer';
    const res = http.get(`${BASE}/api/athletes/career?name=${encodeURIComponent(name)}`);
    athleteTrend.add(res.timings.duration);
    check(res, {
      'athlete: status 200': (r) => r.status === 200,
      'athlete: body not empty': (r) => r.body && r.body.length > 0,
    });
  });
}
