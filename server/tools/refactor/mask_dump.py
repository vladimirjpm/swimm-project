# Маскирует метки времени и токен restrict в двух `pg_dump --data-only`, чтобы сравнить копии
# базы после одинаковых операций (сквозная сверка, docs/plans/refactor-handoff.md §4).
#   python mask_dump.py a.sql a.masked b.sql b.masked && diff a.masked b.masked
import sys, re
ts = re.compile(r'\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d+)?\+00')
tok = re.compile(r'(?:un)?restrict [A-Za-z0-9]{20,}')
for src, dst in ((sys.argv[1], sys.argv[2]), (sys.argv[3], sys.argv[4])):
    t = open(src, encoding='utf-8').read()
    n_ts = len(ts.findall(t))
    t = tok.sub('restrict TOKEN', ts.sub('TS', t))
    open(dst, 'w', encoding='utf-8').write(t)
    print(dst.replace('\\', '/').split('/')[-1], 'chars', len(t), 'timestamps masked', n_ts)
