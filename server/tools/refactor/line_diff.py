# Построчная сверка перенесённого кода: диапазон строк старого файла против участка нового
# (от строки, равной START, до первой строки, равной END, после неё). Отступы и пустые строки
# не сравниваются; печатаются только отличия — их надо прочитать глазами и убедиться, что это
# лишь задуманные механические замены (docs/plans/refactor-handoff.md §4).
#   python line_diff.py OLD.cs 113 1212 NEW.cs "    private sealed class ImportRun" "    }"
import sys, difflib
sys.stdout.reconfigure(encoding='utf-8')
old_path, a0, a1, new_path, start, end = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), sys.argv[4], sys.argv[5], sys.argv[6]
old = open(old_path, encoding='utf-8-sig').read().split('\n')
new = open(new_path, encoding='utf-8-sig').read().split('\n')
s = next(i for i, l in enumerate(new) if l == start)
e = next(i for i in range(s + 1, len(new)) if new[i] == end)
A = [l.strip() for l in old[a0 - 1:a1] if l.strip()]
B = [l.strip() for l in new[s:e + 1] if l.strip()]
sm = difflib.SequenceMatcher(a=A, b=B, autojunk=False)
print(f'old {len(A)} lines, new {len(B)} lines, equal {sum(b.size for b in sm.get_matching_blocks())}')
for op, i1, i2, j1, j2 in sm.get_opcodes():
    if op == 'equal': continue
    for l in A[i1:i2]: print('  - ' + l[:150])
    for l in B[j1:j2]: print('  + ' + l[:150])
