"""Снимок страницы админки для сверки выноса JS (шаги 8 и 11): что браузер ИСПОЛНЯЕТ и что видит.

  python admin_page_snapshot.py BASE_URL OUT_DIR PAGE [PAGE …]     # PAGE: Api, Clubs, Swimmers/Loglig…

На каждую страницу пишет OUT_DIR/<page>.html — разметку, где каждый <script> заменён меткой
[[SCRIPT n]] (токены антифорджери и хэши asp-append-version замаскированы), и OUT_DIR/<page>.js —
тексты всех скриптов по порядку: inline как есть, /js/admin/*.js — телом файла без трёх строк шапки.
До выноса и после `diff -r` должен показать только задуманное: в .html — ничего, в .js — ничего
(или перенос Razor-значений в маленький inline-скрипт, его читать глазами).
"""
import re
import sys
import urllib.request
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8')

SCRIPT = re.compile(r'<script\b([^>]*)>(.*?)</script\s*>', re.I | re.S)
SRC = re.compile(r'\bsrc="([^"]+)"', re.I)
MASKS = [
    (re.compile(r'(name="__RequestVerificationToken"[^>]*value=")[^"]+'), r'\1<TOKEN>'),
    (re.compile(r'(value=")[^"]+("[^>]*name="__RequestVerificationToken")'), r'\1<TOKEN>\2'),
    (re.compile(r'\?v=[\w-]+'), '?v=<HASH>'),
]
# Токен в скрипте layout (`const _csrfToken = '…'`) — свой на каждый запрос.
CSRF_JS = re.compile(r"(_csrfToken = ')[^']+")
HEADER_LINES = 3


def get(url):
    with urllib.request.urlopen(url) as r:
        return r.read().decode('utf-8')


def norm(s):
    return s.replace('\r\n', '\n').strip('\n')


def main():
    base, out = sys.argv[1].rstrip('/'), Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    for page in sys.argv[3:]:
        html = get(f'{base}/Admin/{page}')
        scripts = []

        def repl(m):
            attrs, body = m.group(1), m.group(2)
            src = SRC.search(attrs)
            if src and '/js/admin/' in src.group(1):
                js = get(base + src.group(1).split('?')[0]).replace('\r\n', '\n')
                # Без пометки «откуда»: тело файла обязано совпасть с бывшим inline побайтно.
                scripts.append(norm('\n'.join(js.split('\n')[HEADER_LINES:])))
            elif src:
                scripts.append(f'/* external {src.group(1).split("?")[0]} */')
            else:
                scripts.append(CSRF_JS.sub(r'\1<TOKEN>', norm(body)))
            return f'[[SCRIPT {len(scripts) - 1}]]'

        masked = SCRIPT.sub(repl, html)
        for rx, rep in MASKS:
            masked = rx.sub(rep, masked)
        name = page.replace('/', '-').lower()
        (out / f'{name}.html').write_text(masked.replace('\r\n', '\n'), encoding='utf-8')
        (out / f'{name}.js').write_text('\n\n/* ---- */\n\n'.join(scripts) + '\n', encoding='utf-8')
        print(f'{page}: {len(html)} bytes, {len(scripts)} scripts')


if __name__ == '__main__':
    main()
