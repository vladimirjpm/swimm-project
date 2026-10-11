"""Сверка деления: каждая строка исходника (без отступа) нашлась в новых файлах, и что нового добавлено.

python split_coverage.py ORIG NEW1 NEW2 ...
Печатает: строки исходника, которых нет ни в одном новом файле (с номером), и строки новых
файлов, которых не было в исходнике (с файлом и номером). Пустые строки пропускаются.
Учитывает кратность: строка, встречавшаяся в исходнике N раз, должна встретиться в новых >= N раз.
"""
import sys
from collections import Counter
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8')

orig = Path(sys.argv[1]).read_text(encoding='utf-8').splitlines()
news = {p: Path(p).read_text(encoding='utf-8').splitlines() for p in sys.argv[2:]}

norm = lambda s: s.strip()
orig_c = Counter(norm(l) for l in orig if norm(l))
new_c = Counter(norm(l) for ls in news.values() for l in ls if norm(l))

print('=== В исходнике, но не в новых (или меньше раз) ===')
missing = orig_c - new_c
seen = Counter()
for i, l in enumerate(orig, 1):
    n = norm(l)
    if n and missing[n] > seen[n]:
        seen[n] += 1
        print(f'{i}: {n}')

print('=== Новое (нет в исходнике или больше раз) ===')
extra = new_c - orig_c
seen = Counter()
for p, ls in news.items():
    for i, l in enumerate(ls, 1):
        n = norm(l)
        if n and extra[n] > seen[n]:
            seen[n] += 1
            print(f'{Path(p).name}:{i}: {n}')
