"""Второй уровень: метод-блок, чьё тело — `if (cond) { подблок; подблок; … }`, делится на
метод-цепочку и подметоды. Подблоки — на отступе 16, у каждого `return true` значит «строка
разобрана» (бывший continue основного цикла), поэтому в цепочке `if (Sub()) return true;`."""
import sys, json
path, method, spec_json, out_json = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
spec = json.loads(spec_json)          # [[name, first_stmt_line, end_line], ...] — номера строк файла
L = open(path, encoding='utf-8-sig').read().split('\n')
def ln(n): return L[n - 1]

s = next(k for k, l in enumerate(L, 1) if l == f'        private bool {method}()')
assert ln(s + 1) == '        {'
cond = ln(s + 2)
assert cond.startswith('            if (') and ln(s + 3) == '            {', (cond, ln(s + 3))
e = next(k for k in range(s + 4, len(L) + 1) if ln(k) == '        }')
assert ln(e - 1) == '            return false;' and ln(e - 2) == '            }', (ln(e - 2), ln(e - 1))
inner_first, inner_last = s + 4, e - 3

covered, subs = set(), []
for name, a, b in spec:
    assert ln(b) == ' ' * 16 + '}', (name, b, ln(b))
    assert ln(a).startswith(' ' * 16) and not ln(a).startswith(' ' * 17), (name, a, ln(a))
    c = a
    while ln(c - 1).startswith(' ' * 16 + '//'): c -= 1
    covered.update(range(c, b + 1))
    subs.append((name, [ln(k)[16:] for k in range(c, a)], [ln(k) for k in range(a, b + 1)]))
stray = [k for k in range(inner_first, inner_last + 1) if k not in covered and ln(k).strip()]
assert not stray, [(k, ln(k)) for k in stray]

chain = [f'        private bool {method}()', '        {', cond, '            {']
chain += [f'                if ({n}()) return true;' for n, _, _ in subs]
chain += ['            }', '            return false;', '        }']
methods = []
for name, com, body in subs:
    methods.append('')
    methods += ['        ' + c for c in com]
    methods += [f'        private bool {name}()', '        {']
    methods += [l[4:] if l.strip() else '' for l in body]
    methods += ['            return false;', '        }']

new = L[:s - 1] + chain + methods + L[e:]
open(path, 'w', encoding='utf-8-sig', newline='\n').write('\n'.join(new))
json.dump({n: {'comments': c, 'body': b} for n, c, b in subs}, open(out_json, 'w', encoding='utf-8'), ensure_ascii=False)
print('ok', method, '->', len(subs), 'sub-methods')
