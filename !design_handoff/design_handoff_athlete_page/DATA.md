# Контракт данных

Одна страница — один эндпоинт на блок-группу, чтобы табы грузились лениво.

## `GET /api/swimmers/{id}` — hero + список сезонов

```jsonc
{
  "id": 1207,
  "name": "ליאור שילה",            // выводится RTL как есть
  "nameLatin": "Lior Shilo",        // опционально, для транслита
  "birthYear": 2014,
  "age": 12,                        // «12 year (2014)»
  "countryCode": "IL",
  "photoUrl": null,                 // null → плейсхолдер-заглушка
  "club": { "id": 23, "name": "הפועל בית שמש", "logoUrl": "…" },
  "ageGroup": { "code": "Y", "label": "Y 11–14" },
  "programs": ["pool", "open_water"],       // чипы рядом с группой
  "recordsHeld": 2,                          // бейдж «🏆 2 records»; 0 → не рендерить
  "isOwner": false,
  "isFollowed": false,
  "isFavorite": true,
  "seasons": [{ "code": "25/26", "label": "2025/26", "isCurrent": true }, …]
}
```

## `GET /api/swimmers/{id}/summary?season=25/26` — KPI-плитки и шапка панели

```jsonc
{
  "points": 9029,
  "medals": { "gold": 17, "silver": 2, "bronze": 0 },
  "level": { "code": "2y", "progress": 0.58 },   // progress 0..1 → длина дуги
  "swims": 31,
  "events": 11
}
```
`level.progress` рисуется `UI_NormativeLevelIcon`; `medals` — `MedalWithTooltip`
(нулевые номиналы показываем приглушёнными, а не прячем).

## `GET /api/swimmers/{id}/best-times?season=25/26` — таб Results

Одна запись = одна дистанция, лучший результат за выбранный сезон (`season=all` — за карьеру).

```jsonc
[{
  "eventId": 204,
  "distance": 1600,
  "stroke": "freestyle",
  "course": "open_water",                 // pool25 | pool50 | open_water
  "timeMs": 1759110,                      // выводится ТОЛЬКО через UI_SwimTime
  "isBest": true,
  "isFlagged": false,                     // true → строка через swimFlaggedRowProps
  "flagReason": null,                     // текст поповера у чипа «⚠ Under review»
  "place": 1,
  "ageAtSwim": 12,
  "points": 612,
  "level": { "code": "2y", "progress": 0.68 },
  "splits": [432400, 881800, 1325200],
  "date": "2026-04-27",
  "competition": {
    "id": 64,
    "name": "אליפות ישראל — מים פתוחים, אילת 2026",
    "isChampionship": true,               // true → значок 🏆 перед названием
    "venue": "Eilat"
  },
  "resultId": 88201
}]
```

Если `isFlagged: true` — очки не начисляются: `points: null`, в строке `Points: —`,
дуга уровня пустая.

## `GET /api/swimmers/{id}/progress?eventId=204` — таб Progress

История всех заплывов выбранной связки стиль+дистанция, по возрастанию даты.

```jsonc
{
  "event": { "eventId": 204, "distance": 1600, "stroke": "freestyle", "course": "open_water" },
  "points": [{
    "date": "2026-04-27",
    "timeMs": 1759110,
    "isPb": true,
    "isFlagged": false,
    "points": 612,
    "place": 1,
    "competition": { "id": 64, "name": "…", "isChampionship": true }
  }, …]
}
```

## `GET /api/swimmers/{id}/personal-bests?course=pool25` — таб Records & PB

```jsonc
[{
  "distance": 100, "stroke": "freestyle",
  "timeMs": 58420,
  "points": 672,
  "holdsClubRecord": true,
  "holdsNationalAgeRecord": true,
  "deltaToClubRecordMs": 0,               // 0 при holdsClubRecord
  "deltaToNationalAgeRecordMs": 440,      // > 0 = отставание, показываем «+0.44»
  "date": "2026-04-18",
  "competition": { "id": 52, "name": "…", "isChampionship": true },
  "resultId": 88410
}]
```

## `GET /api/swimmers/{id}/media` — таб Media

```jsonc
{ "canEdit": true, "items": [{ "id": 9, "type": "video|photo|link", "url": "…", "label": "1600m free", "thumbUrl": "…" }] }
```

## Общие правила

- Названия соревнований и имена приходят на иврите — везде `dir="auto"`, обрезка `ellipsis`, без транслитерации на клиенте.
- `isChampionship` — единственный источник значка 🏆; клиент не определяет чемпионат по названию.
- Все времена в миллисекундах; форматирование только `UI_SwimTime`.
- Пустой сезон: блок рендерится с состоянием «нет заплывов в этом сезоне», карусель не скрывается.
