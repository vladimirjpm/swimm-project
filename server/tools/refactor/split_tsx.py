# Деление большого .tsx/.ts по верхнеуровневым объявлениям (шаг 9 рефакторинга).
#   python split_tsx.py PLAN.json [--write]
# PLAN.json: {"source": "client/src/…/big.tsx",
#             "files": {"client/src/…/part-a.tsx": ["NameA", "NameB"], …}}
# Не перечисленные объявления остаются в source. Для каждого файла:
#   • импорты исходника — только те имена, что используют его блоки (относительные пути
#     пересчитаны от нового каталога; импорты без имён — побочные — остаются в source);
#   • имена соседних частей — импортом из них (типы/интерфейсы — `type`), а у определения
#     появляется `export`, если его не было;
#   • блоки — дословно, в исходном порядке (вместе с комментариями над ними).
# Без --write только печатает план и сверяет. Сверка после записи: каждый блок в новом месте =
# исходный (с точностью до добавленного `export ` и пустых строк в начале файла).
import json, os, posixpath, re, sys
sys.path.insert(0, os.path.dirname(__file__))
from tsx_blocks import read, write, parse, import_names, uses

TYPE_KINDS = ('type', 'interface')


def rel_import(from_file, to_file):
    r = posixpath.relpath(posixpath.splitext(to_file)[0], posixpath.dirname(from_file))
    return r if r.startswith('.') else './' + r


def rewrite_src(src, old_file, new_file):
    if not src.startswith('.'):
        return src
    target = posixpath.normpath(posixpath.join(posixpath.dirname(old_file), src))
    r = posixpath.relpath(target, posixpath.dirname(new_file))
    return r if r.startswith('.') else './' + r


def build_import(src, names):
    """names: [(local, is_type, spec)] — spec None = default, '*' = namespace."""
    default = [n for n in names if n[2] is None]
    ns = [n for n in names if n[2] == '*']
    named = [n for n in names if n[2] not in (None, '*')]
    all_type = all(n[1] for n in names)
    parts = []
    if default:
        parts.append(default[0][0])
    if ns:
        parts.append('* as ' + ns[0][0])
    if named:
        specs = []
        for local, is_type, spec in named:
            s = spec[5:].strip() if spec.startswith('type ') else spec
            specs.append(('type ' + s) if (is_type and not all_type) else s)
        parts.append('{ ' + ', '.join(specs) + ' }')
    kw = 'import type ' if all_type and not default and not ns else 'import '
    if all_type and default:
        kw = 'import type '
    line = kw + ', '.join(parts) + f" from '{src}';"
    if len(line) > 110 and named:
        inner = ', '.join(s for s in line[line.index('{') + 2:line.rindex('}') - 1].split(', '))
        line = line[:line.index('{')] + '{\n  ' + inner + ',\n}' + f" from '{src}';"
    return line


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    plan = json.load(open(sys.argv[1], encoding='utf-8'))
    do_write = '--write' in sys.argv
    source = plan['source']
    text, bom, crlf = read(source)
    imports, blocks, _ = parse(text)
    owner = {b['name']: source for b in blocks}
    for f, names in plan['files'].items():
        for n in names:
            assert n in owner, f'нет объявления {n}'
            assert owner[n] == source, f'{n} назначен дважды'
            owner[n] = f
    targets = [source] + list(plan['files'])
    by_name = {b['name']: b for b in blocks}
    need_export = set()
    out = {}
    for t in targets:
        tb = [b for b in blocks if owner[b['name']] == t]
        body_text = '\n'.join('\n'.join(b['body']) for b in tb)
        # соседние части
        sibling = {}
        for b in blocks:
            if owner[b['name']] != t and uses(body_text, b['name']):
                sibling.setdefault(owner[b['name']], []).append(b)
                need_export.add(b['name'])
        lines = []
        for imp in imports:
            src, names = import_names(imp)
            if src is None:
                if t == source:
                    lines.append(imp)
                continue
            if not names:
                if t == source:
                    lines.append(imp)
                continue
            used = [n for n in names if uses(body_text, n[0])]
            if not used:
                continue
            if t == source and len(used) == len(names):
                lines.append(imp)          # исходный импорт исходника — как был
            else:
                lines.append(build_import(rewrite_src(src, source, t), used))
        for f in targets:
            if f in sibling:
                names = [(b['name'], b['kind'] in TYPE_KINDS, b['name']) for b in sibling[f]]
                lines.append(build_import(rel_import(t, f), names))
        out[t] = (lines, tb)
    files = {}
    for t, (imp_lines, tb) in out.items():
        chunks = []
        for k, b in enumerate(tb):
            lead = list(b['lead'])
            if k == 0:
                while lead and lead[0].strip() == '':
                    lead.pop(0)
                lead = [''] + lead
            body = list(b['body'])
            if b['name'] in need_export and not b['exported']:
                body[0] = 'export ' + body[0]
            chunks.append('\n'.join(lead + body))
        files[t] = '\n'.join(imp_lines) + '\n' + '\n'.join(chunks) + '\n'
    # отчёт
    for t in targets:
        tb = out[t][1]
        print(f"{t}: {files[t].count(chr(10))} строк; {', '.join(b['name'] for b in tb)}")
    print('export добавлен:', ', '.join(sorted(n for n in need_export if not by_name[n]['exported'])) or '—')
    # сверка: разбираем результат и сравниваем блоки с исходными
    errors = 0
    for t, content in files.items():
        _, nb, _ = parse(content)
        for b in nb:
            o = by_name[b['name']]
            body = list(b['body'])
            if b['name'] in need_export and not o['exported']:
                assert body[0].startswith('export ')
                body[0] = body[0][len('export '):]
            strip = lambda ls: [l for l in ls if l.strip()]
            if body != o['body'] or strip(b['lead']) != strip(o['lead']):
                errors += 1
                print('  ОТЛИЧИЕ в блоке', b['name'], 'файл', t)
        if sorted(x['name'] for x in nb) != sorted(x['name'] for x in out[t][1]):
            errors += 1
            print('  набор блоков не совпал в', t)
    print('сверка блоков:', 'OK' if errors == 0 else f'{errors} ошибок')
    if do_write and errors == 0:
        for t, content in files.items():
            os.makedirs(os.path.dirname(t), exist_ok=True)
            write(t, content, bom, crlf)
        print('записано')


if __name__ == '__main__':
    main()
