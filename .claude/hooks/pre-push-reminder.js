// Хук PreToolUse (Bash|PowerShell): перед `git push` напоминает пройти docs/pre-push-rules.md.
// Ничего не блокирует и ничего не спрашивает — только добавляет напоминание в контекст модели
// (решение Влада 11.09.2026: «перед каждым пушем проверяй файл с правилами»).
let raw = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => { raw += chunk; });
process.stdin.on('end', () => {
  let command = '';
  try {
    command = JSON.parse(raw)?.tool_input?.command ?? '';
  } catch {
    return; // не разобрали вход — молчим, push не наше дело останавливать
  }
  // `git push` именно как команда — в начале строки или после &&, ||, ;, |, (. Слова «git push»
  // внутри текста (сообщение коммита, echo) напоминание не вызывают: ложный сигнал учит его игнорировать.
  if (!/(^|[;&|(\n])\s*git\s+push\b/.test(command)) return;

  process.stdout.write(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      additionalContext:
        'Перед push: пройди docs/pre-push-rules.md по диффу (git diff --name-only @{upstream}...HEAD). '
        + 'Совпало правило, а «то» не сделано — доделай до push или скажи Владу.',
    },
  }));
});
