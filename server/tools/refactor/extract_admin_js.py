"""Вынос inline-<script> страницы админки в wwwroot/js/admin/<name>.js (шаг 8/11 рефакторинга).

  python extract_admin_js.py report  PAGE.cshtml            # блоки и Razor-вставки в них
  python extract_admin_js.py extract PAGE.cshtml NAME [IDX]  # вынести блок IDX (по умолчанию — самый большой)

Запускать из server/Swimm.API. Содержимое <script> переносится БАЙТ В БАЙТ (отступ — часть
шаблонных строк), сверху — три строки шапки. Единственная правка — Razor-экранирование `@@` → `@`
(в .cshtml это литерал «@», в .js экранировать нечего). Блок с настоящими Razor-вставками
(`@Model…`, `@(…)`, `@Html…`, `@if`…) не выносится — их сначала переделать в маленький inline-скрипт
перед файлом (window.__X = …) или data-атрибуты. Концы строк сохраняются как в исходнике.
"""
import re
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8')

OPEN = re.compile(r'<script(?![^>]*\bsrc=)[^>]*>', re.I)
CLOSE = re.compile(r'</script\s*>', re.I)
# Razor: «@» не после буквы/цифры (иначе это e-mail) и перед кодом. «@@» — экранированный литерал.
RAZOR = re.compile(r'(?<![\w@])@(?!@)(?=[A-Za-z_(:{*])')


def blocks(text):
    out, pos = [], 0
    while True:
        m = OPEN.search(text, pos)
        if not m:
            return out
        c = CLOSE.search(text, m.end())
        out.append((m.start(), m.end(), c.start(), c.end()))
        pos = c.end()


def line_of(text, idx):
    return text.count('\n', 0, idx) + 1


def razor_hits(text, start, end):
    body = text[start:end]
    return [(line_of(text, start + m.start()), body[m.start():m.start() + 40].split('\n')[0])
            for m in RAZOR.finditer(body)]


def main():
    mode, page = sys.argv[1], Path(sys.argv[2])
    raw = page.read_bytes()
    text = raw.decode('utf-8-sig')
    bom = raw.startswith(b'\xef\xbb\xbf')
    bl = blocks(text)
    if mode == 'report':
        for i, (_, s, e, _) in enumerate(bl):
            hits = razor_hits(text, s, e)
            print(f'[{i}] lines {line_of(text, s)}-{line_of(text, e)} ({text.count(chr(10), s, e)}), '
                  f'razor={len(hits)}, @@={text.count("@@", s, e)}')
            for ln, frag in hits:
                print(f'     {ln}: {frag}')
        return
    name = sys.argv[3]
    idx = int(sys.argv[4]) if len(sys.argv) > 4 else max(range(len(bl)), key=lambda i: bl[i][2] - bl[i][1])
    o0, s, e, c1 = bl[idx]
    hits = razor_hits(text, s, e)
    if hits:
        sys.exit(f'Razor inside block {idx}: {hits[:5]} — сначала вынести значения в inline-скрипт')
    nl = '\r\n' if '\r\n' in text else '\n'
    rel = page.as_posix().split('Pages/')[1]
    header = (f'// Скрипт страницы {page.as_posix()}. Вынесен из разметки как есть (10.10.2026):{nl}'
              f'// классический скрипт, не модуль — функции из onclick в разметке остаются глобальными.{nl}'
              f'// Tailwind-классы отсюда собирает @source в Styles/admin.css.{nl}')
    body = text[s:e].replace('@@', '@')
    js = Path(f'wwwroot/js/admin/{name}.js')
    if js.exists():
        sys.exit(f'{js} уже есть')
    js.write_bytes((header + body.lstrip('\r\n')).encode('utf-8'))
    tag = f'<script src="~/js/admin/{name}.js" asp-append-version="true"></script>'
    new = text[:o0] + tag + text[c1:]
    page.write_bytes(('﻿' if bom else '').encode('utf-8') + new.encode('utf-8'))
    print(f'{rel}: block {idx} ({text.count(chr(10), s, e)} lines) -> {js}')


if __name__ == '__main__':
    main()
