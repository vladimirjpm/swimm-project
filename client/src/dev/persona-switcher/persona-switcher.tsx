import React, { useEffect, useState } from 'react';
import { routes } from '../../utils/routes';

/**
 * Переключатель тестового персонажа (docs/plans/test-personas-plan.md, этап 3).
 *
 * Только dev: плашку в каждую страницу вставляет плагин `devPersonaSwitcher` из vite.config.js
 * (apply: 'serve'), в прод-сборку она не попадает. Данные и переключение — dev-ручки API
 * `/api/dev/personas` и `/api/dev/persona?as=…` (есть только при DevAdminBypass в Development;
 * нет ручки — нет и плашки). Переключение ставит dev-куку и перезагружает страницу: так
 * личность меняется для ВСЕХ запросов страницы, включая antiforgery-токен.
 *
 * Это инструмент разработчика, не UI продукта: цвета свои и фиксированные, темы сайта не
 * наследуются — плашка должна одинаково читаться на любой странице.
 */

interface PersonaRow {
  nick: string;
  email: string;
  description: string;
  exists: boolean;
  isActive: boolean;
}

interface PersonasInfo {
  current: string;
  defaultEmail: string;
  realLogin: boolean;
  personas: PersonaRow[];
  testGroups: { slug: string; name: string; joinPolicy: string }[];
}

const DEFAULT = 'default';
const GUEST = 'utest-guest';

const PersonaSwitcher: React.FC = () => {
  const [info, setInfo] = useState<PersonasInfo | null>(null);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    fetch('/api/dev/personas', { credentials: 'same-origin' })
      .then((r) => (r.ok ? r.json() : null))
      .then(setInfo)
      .catch(() => setInfo(null));
  }, []);

  if (!info) return null;

  const choose = async (as: string) => {
    setBusy(true);
    try {
      await fetch(`/api/dev/persona?as=${encodeURIComponent(as)}`, { credentials: 'same-origin' });
    } finally {
      window.location.reload();
    }
  };

  const label = info.current === DEFAULT ? `default · ${info.defaultEmail}` : info.current;
  const noPersonas = info.personas.every((p) => !p.exists);

  const option = (value: string, title: string, sub: string, disabled = false) => {
    const active = info.current === value;
    return (
      <button
        key={value}
        type="button"
        disabled={disabled || busy}
        onClick={() => choose(value)}
        className={`block w-full rounded-md px-2.5 py-1.5 text-left transition-colors ${
          active ? 'bg-amber-400/20 text-amber-200' : 'text-slate-100 hover:bg-white/10'
        } disabled:cursor-not-allowed disabled:opacity-40`}
      >
        <div className="font-mono text-[12px] font-bold">{active ? '● ' : ''}{title}</div>
        <div className="text-[11px] text-slate-400">{sub}</div>
      </button>
    );
  };

  return (
    <div className="fixed bottom-3 left-3 z-[2147483000] font-sans text-[12px]" data-dev-persona-switcher>
      {open && (
        <div className="mb-2 max-h-[70vh] w-[300px] max-w-[calc(100vw-24px)] overflow-y-auto rounded-xl border border-white/15 bg-slate-900/95 p-2 shadow-2xl backdrop-blur">
          <div className="px-2.5 pb-1.5 pt-1 text-[11px] font-bold uppercase tracking-wide text-slate-400">
            Dev · view the site as
          </div>
          {info.realLogin && (
            <div className="mx-1 mb-2 rounded-md bg-rose-500/20 px-2.5 py-1.5 text-[11px] text-rose-200">
              You are signed in for real — that login wins over the switcher. Sign out to use it.
            </div>
          )}
          {option(DEFAULT, 'default', info.defaultEmail)}
          {option(GUEST, GUEST, 'Not signed in')}
          <div className="my-1.5 border-t border-white/10" />
          {noPersonas && (
            <div className="px-2.5 py-1 text-[11px] text-amber-200">
              No utest accounts yet — run <span className="font-mono">dotnet run -- --seed-personas</span>
            </div>
          )}
          {info.personas.map((p) =>
            option(
              p.nick,
              p.nick,
              p.exists && !p.isActive ? `${p.description} (acts as guest)` : p.description,
              !p.exists,
            ),
          )}
          {info.testGroups.length > 0 && (
            <>
              <div className="my-1.5 border-t border-white/10" />
              <div className="px-2.5 pb-1 text-[11px] font-bold uppercase tracking-wide text-slate-400">Test groups</div>
              {info.testGroups.map((g) => (
                <a
                  key={g.slug}
                  href={routes.group(g.slug)}
                  className="block rounded-md px-2.5 py-1 text-sky-300 no-underline hover:bg-white/10"
                >
                  {g.name} <span className="text-slate-500">· {g.joinPolicy}</span>
                </a>
              ))}
            </>
          )}
        </div>
      )}
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        title="Dev persona switcher — whose eyes you see the site with"
        className="rounded-full border border-amber-300/40 bg-slate-900/90 px-3 py-1.5 font-mono text-[12px] font-bold text-amber-200 shadow-lg backdrop-blur hover:bg-slate-800"
      >
        DEV · {label} {open ? '▾' : '▴'}
      </button>
    </div>
  );
};

export default PersonaSwitcher;
