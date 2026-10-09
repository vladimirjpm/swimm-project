# Верхнеуровневые блоки .tsx/.ts: импорты в начале файла, объявления (function/const/type/
# interface/class/enum, с export и без) и прилипшие к ним сверху комментарии. Общая часть
# split_tsx.py; запуск напрямую — анализ, кто из блоков кого использует:
#   python tsx_blocks.py FILE
# Граница блока — строка С НУЛЯ, начинающая объявление. Хвост сегмента из пустых строк и
# комментариев с нуля уходит следующему блоку (это его doc-комментарий).
import re, sys

DECL = re.compile(r'^(export\s+)?(default\s+)?(async\s+)?(function|const|let|type|interface|class|enum)\s+([A-Za-z_$][\w$]*)')
IMPORT_END = re.compile(r"""(from\s+['"][^'"]+['"]|^import\s+['"][^'"]+['"]);?\s*$""")
COMMENT_LINE = re.compile(r'^(//|/\*|\*|\s\*|\*/)')


def read(path):
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    crlf = b'\r\n' in raw
    return raw.decode('utf-8-sig').replace('\r\n', '\n'), bom, crlf


def write(path, text, bom, crlf):
    if crlf:
        text = text.replace('\n', '\r\n')
    open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + text.encode('utf-8'))


def parse(text):
    """-> (imports, prelude, blocks). prelude — строки между импортами и первым блоком, которые
    не прилипли к нему (обычно пусто). blocks: dict(name, kind, exported, default, lead, body)."""
    lines = text.split('\n')
    trailing_nl = lines and lines[-1] == ''
    if trailing_nl:
        lines = lines[:-1]
    i = 0
    imports = []
    while i < len(lines):
        if lines[i].startswith('import '):
            j = i
            while not IMPORT_END.search(lines[j]):
                j += 1
            imports.append('\n'.join(lines[i:j + 1]))
            i = j + 1
        elif lines[i].strip() == '' and any(l.startswith('import ') for l in lines[i:i + 3]):
            i += 1
        else:
            break
    starts = [k for k in range(i, len(lines)) if DECL.match(lines[k])]
    blocks = []
    prev_end = i
    segs = []
    for n, s in enumerate(starts):
        e = starts[n + 1] if n + 1 < len(starts) else len(lines)
        segs.append((s, e))
    lead_from = i
    for n, (s, e) in enumerate(segs):
        # тело блока — до следующего объявления минус хвост из пустых строк/комментариев с нуля
        end = e
        if n + 1 < len(segs):
            while end - 1 > s and (lines[end - 1].strip() == '' or COMMENT_LINE.match(lines[end - 1])):
                end -= 1
        m = DECL.match(lines[s])
        blocks.append(dict(name=m.group(5), kind=m.group(4), exported=bool(m.group(1)),
                           default=bool(m.group(2)), lead=lines[lead_from:s], body=lines[s:end]))
        lead_from = end
    return imports, blocks, trailing_nl


def import_names(imp):
    """-> (src, [(local_name, is_type, spec)]); spec — как имя записано в фигурных скобках."""
    m = re.match(r'^import\s+(type\s+)?(.*?)\s+from\s+[\'"]([^\'"]+)[\'"]', imp, re.S)
    if not m:
        return None, []
    whole_type, spec, src = bool(m.group(1)), m.group(2), m.group(3)
    names = []
    d = re.match(r'^([A-Za-z_$][\w$]*)\s*(,|$)', spec)
    if d:
        names.append((d.group(1), whole_type, None))
    ns = re.search(r'\*\s+as\s+([A-Za-z_$][\w$]*)', spec)
    if ns:
        names.append((ns.group(1), whole_type, '*'))
    br = re.search(r'\{(.*)\}', spec, re.S)
    if br:
        for part in br.group(1).split(','):
            part = part.strip()
            if not part:
                continue
            t = part.startswith('type ')
            p = part[5:].strip() if t else part
            names.append((p.split(' as ')[-1].strip(), whole_type or t, part))
    return src, names


def strip_comments(text):
    text = re.sub(r'/\*.*?\*/', '', text, flags=re.S)
    return re.sub(r'(^|[^:])//.*$', r'\1', text, flags=re.M)


def uses(text, name):
    return re.search(r'(?<![\w$.])' + re.escape(name) + r'(?![\w$])', strip_comments(text)) is not None


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    text, _, _ = read(sys.argv[1])
    imports, blocks, _ = parse(text)
    names = [b['name'] for b in blocks]
    print(f'imports {len(imports)}, blocks {len(blocks)}')
    for b in blocks:
        body = '\n'.join(b['body'])
        deps = [x for x in names if x != b['name'] and uses(body, x)]
        print(f"{b['name']:30} {b['kind']:9} {len(b['lead']) + len(b['body']):5}  -> {', '.join(deps)}")
