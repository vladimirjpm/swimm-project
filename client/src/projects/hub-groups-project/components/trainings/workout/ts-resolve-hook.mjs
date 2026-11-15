// Тестам workout (node --test): модули импортируют друг друга без расширения, как в Vite и tsc,
// а Node ищет файл с расширением. Хук пробует `.ts`, если без расширения файла нет.
// Нужен Node ≥ 23.5 (`registerHooks`); подключать первой строкой теста.
import { registerHooks } from 'node:module';

registerHooks({
  resolve(specifier, context, next) {
    try {
      return next(specifier, context);
    } catch (error) {
      if (specifier.startsWith('.') && !/\.[cm]?[jt]s$/.test(specifier)) return next(`${specifier}.ts`, context);
      throw error;
    }
  },
});
