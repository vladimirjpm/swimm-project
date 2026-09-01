# Стики-минимизация шапки соревнования — вариант 16b «Контекст»

Утверждённый паттерн. Макет: `Competition Overview.dc.html`, секция `#16b`.

Макет рисовался под ≤ 640px, но **поведение распространено на все ширины** (решение Влада,
31.08.2026): бар одинаковый везде, меняется только ширина содержимого и кегль названия —
см. «Десктоп» ниже. Отдельного десктопного варианта нет и не планируется.

## Поведение

- Полная шапка (иконка 52×62 + заголовок, мета в 2 строки, кнопки Add media / Change, табы) остаётся в потоке как есть (см. фикс 10b) и просто скроллится вверх.
- При `scrollTop > 120` поверх выезжает **компакт-бар** (fixed, top: 0, поверх контента); при возврате `scrollTop <= 120` — уезжает. Один порог в обе стороны, без гистерезиса.
- Анимация: `transform: translateY(-108%) → translateY(0)`, `transition: transform 200ms ease`. Высоту полной шапки НЕ менять, ничего не схлопывать — только оверлей.
- Тень бара: `box-shadow: 0 6px 16px rgba(0,0,0,.3)`.

## Состав компакт-бара (2 строки, ~98px)

Строка 1 — бар, фон `#0353a4` (на тон темнее hero `#0466c8`), `padding: 7px 12px`, flex, `gap: 10px`:

1. **Мини-иконка соревнования** 34×42 — та же трёхленточная плитка (сезон+кубок / буква / возраст), уменьшенная:
   верх 13px (❄️ 7px + 🏆 9px на белом), центр — буква 14px/900, низ 11px (`8-11`, 7px/800, белый фон, текст `#0466c8`).
2. **Название** — одна строка, `min-width:0; flex:1; overflow:hidden; text-overflow:ellipsis; white-space:nowrap`, 13.5px/800, `dir="auto"`, справа caret `▾` (10px, opacity .85), `flex:none`.
   **Тап по названию (вся зона иконка+название) = Change** — открывает свитчер соревнований. Отдельной кнопки Change нет.
3. **＋ (Add media)** — круглая кнопка 42×42, белая, текст `#0353a4`, 19px/800, `flex:none`. Рендерится только залогиненному (как и Add media в полной шапке).

Строка 2 — **табы**: тот же ряд, что в полной шапке (Overview | Swims N | Clubs N | Media N | Records если есть), фон `#0466c8` + подложка `rgba(0,0,0,.1)`, горизонтальный скролл, `white-space:nowrap`, паддинг табов 11px сверху / 12px снизу. Активный таб синхронизирован с in-flow табами (один state/URL).

## Хит-таргеты и разное

- Все таргеты ≥ 40px: зона «иконка+название» — вся высота бара; ＋ — 42px; табы — полная высота строки.
- z-index бара выше контента и персональной полосы; ниже модалок.
- Никаких live-обновлений в баре (счётчики табов — те же, что в потоке).
- Порог 120px — от верха скролл-контейнера страницы; если над шапкой есть topbar приложения, порог отсчитывается тем же скроллером.
- Бар перекрывает топбар приложения, а не встаёт под ним: он `fixed top:0` и по z-index выше топбара. Топбар возвращается сам, когда бар уезжает (скролл вверх за порог).

## Референс-разметка (из макета)

```html
<div style="position:fixed;top:0;left:0;right:0;z-index:40;transform:translateY(0);transition:transform .2s ease;box-shadow:0 6px 16px rgba(0,0,0,.3);">
  <div style="background:#0353a4;color:#fff;display:flex;align-items:center;gap:10px;padding:7px 12px;">
    <span style="width:34px;height:42px;flex:none;display:flex;flex-direction:column;border-radius:9px;overflow:hidden;border:1px solid rgba(255,255,255,.35);background:rgba(255,255,255,.2);">
      <span style="height:13px;flex:none;background:rgba(255,255,255,.92);display:flex;align-items:center;justify-content:center;gap:2px;line-height:1;"><span style="font-size:7px;">❄️</span><span style="font-size:9px;">🏆</span></span>
      <span style="flex:1;display:flex;align-items:center;justify-content:center;color:#fff;font-size:14px;font-weight:900;line-height:1;">K</span>
      <span style="height:11px;flex:none;background:#fff;color:#0466c8;display:flex;align-items:center;justify-content:center;font-size:7px;font-weight:800;line-height:1;">8-11</span>
    </span>
    <span style="min-width:0;flex:1;display:flex;align-items:center;gap:6px;cursor:pointer;"><!-- tap = Change -->
      <span dir="auto" style="min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:13.5px;font-weight:800;">אליפות ישראל "ארנה" לגילאי 8-11 חורף 2026</span>
      <span style="flex:none;font-size:10px;opacity:.85;">▾</span>
    </span>
    <button style="flex:none;width:42px;height:42px;border-radius:999px;border:none;background:#fff;color:#0353a4;font-size:19px;font-weight:800;line-height:1;">＋</button>
  </div>
  <div style="background:#0466c8;color:#fff;"><div style="background:rgba(0,0,0,.1);display:flex;gap:2px;padding:0 10px;overflow-x:auto;">
    <span style="border-bottom:2.5px solid #fff;padding:11px 10px 12px;font-size:13px;font-weight:700;white-space:nowrap;">Overview</span>
    <span style="border-bottom:2.5px solid transparent;padding:11px 10px 12px;font-size:13px;font-weight:700;opacity:.75;white-space:nowrap;">Swims 3029</span>
    <span style="border-bottom:2.5px solid transparent;padding:11px 10px 12px;font-size:13px;font-weight:700;opacity:.75;white-space:nowrap;">Clubs 61</span>
    <span style="border-bottom:2.5px solid transparent;padding:11px 10px 12px;font-size:13px;font-weight:700;opacity:.75;white-space:nowrap;">Media 8</span>
  </div></div>
</div>
```

Цвета брать из темы (`competition-blue`): hero `--theme-primary` (#0466c8), бар — тот же тон темнее (#0353a4).

## Десктоп (≥ 640px)

Тот же бар, то же поведение и тот же порог 120px. Отличий ровно два:

- содержимое строки 1 садится в общий контейнер страницы (`PAGE_CONTAINER`, max-width 1440
  + боковые паддинги), а не идёт край-в-край с паддингом 12px — иначе на 1920 плитка и
  кнопка уезжали бы к самым краям, а hero и табы в потоке живут в контейнере;
- кегль названия 15px вместо 13.5px.

Мини-плитка, круглая кнопка, табы, тень, анимация — без изменений.

## Липкие строки внутри табов

Бар публикует занятую сверху высоту в CSS-переменной **`--comp-sticky-chrome-h`**: свою
высоту, когда выехал, и высоту топбара приложения, когда уехал. Всё, что внутри табов
должно липнуть под шапкой, берёт `top` из неё и не знает ни про порог, ни про топбар:

```css
position: sticky;
top: var(--comp-sticky-chrome-h, 0px);
```

Первый потребитель — переключатель **All programme / ⭐ My plan** в табе Start list (липкий
на всех ширинах, решение Влада 31.08.2026): переключаться между программой и своим планом
нужно с любого места длинного протокола. Липкой строке обязателен непрозрачный фон
(`--theme-mode-page-bg`), вынесенный на края контейнера отрицательными полями, — иначе
сквозь неё просвечивает уезжающий контент.

Высоту топбара бар меряет по атрибуту `data-app-topbar` на его `<header>`, а не хардкодом.

## Иконка Add media

Круглая кнопка бара несёт **`UI_AddVideoIcon`** (камера с плюсиком) — тот же значок, что в
шапке результатов и на строках заплывов, — а не глиф `＋` из макета: у действия «добавить
медиа» в проекте уже есть свой знак, и второй значок для того же действия учит лишнему.
