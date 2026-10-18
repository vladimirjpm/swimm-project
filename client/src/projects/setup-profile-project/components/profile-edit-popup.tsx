import React, { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useDeepThemeClass } from '../../components/deep/use-deep-theme-class';
import { useSwimmerSearch } from '../../swimmer-project/use-swimmer-page';
import type { ProfileSetupTarget } from '../../../hooks/useProfileSetup';

/**
 * Один попап на всё: смена, добавление и удаление строки «Set up profile» — хендофф
 * `!design_handoff/set-up-profile-ex4`, вариант 5d (план docs/plans/profile-setup-plan.md,
 * «Правка итога»). Открывается кликом по строке итога, из «+ Add another club or group» и из
 * кнопок шагов чек-листа.
 *
 * Попап только собирает выбор: что сделать с избранным и группами, решает страница
 * (`onSave` / `onRemove`) — там же тост Undo и отложенный уход из группы (Р9).
 *
 * Своё окно, а не `UI_H2HPickerModal`: у того нет футера (Remove / Cancel / Save), а на телефоне
 * нужна шторка снизу (docs/ui-components.md §6). Портал — с классом темы на корне, как у
 * остальных окон deep: снаружи корня страницы роли `--t-*` пусты.
 */

export type EditKind = 'me' | 'family' | 'club' | 'group';

export interface EditRequest {
  kind: EditKind;
  /** add — пустая строка или «+ Add»; change — клик по заполненной строке. */
  mode: 'add' | 'change';
  /** Что меняем (режим change). У семьи — не нужен: правится весь список. */
  current?: ProfileSetupTarget | null;
}

/** Кандидат в списке попапа. */
interface Option {
  id: number;
  name: string;
  sub?: string;
  /** Уже выбран где-то ещё (другой клуб в избранном, группа, где уже состоит) — недоступен. */
  taken?: string;
  /** Группа с одобрением — плашка «тренер подтвердит». */
  approval?: boolean;
}

interface ClubOption { id: number; name: string; swimmerCount?: number; isPseudo?: boolean }
interface GroupOption { id: number; name: string; slug: string; club_name?: string | null; join_policy?: string }

// Справочники клубов и групп — один запрос на страницу, сколько бы раз окно ни открывали.
let clubsCache: Promise<ClubOption[]> | null = null;
let groupsCache: Promise<GroupOption[]> | null = null;
const loadClubs = () => (clubsCache ??= fetch('/api/me/hub-groups/clubs', { credentials: 'include' })
  .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
  .catch((e) => { clubsCache = null; throw e; }));
const loadGroups = () => (groupsCache ??= fetch('/api/hub-groups', { credentials: 'include' })
  .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
  .catch((e) => { groupsCache = null; throw e; }));

const TITLES: Record<EditKind, { add: string; change: string; noun: string }> = {
  me: { add: 'Find me', change: 'Change Me', noun: 'Me' },
  family: { add: 'Add children', change: 'Edit family', noun: 'family' },
  club: { add: 'Add club', change: 'Change club', noun: 'club' },
  group: { add: 'Add group', change: 'Change group', noun: 'group' },
};

const PLACEHOLDERS: Record<EditKind, string> = {
  me: 'Search your name…',
  family: "Search your child's name…",
  club: 'Search a club…',
  group: 'Search a group…',
};

interface Props {
  request: EditRequest | null;
  /** Уже в избранном / уже вступил — чтобы не предлагать второй раз. */
  takenClubIds: Set<number>;
  takenGroupIds: Set<number>;
  /** Нынешняя семья (чекбоксы) и «это я» (его в семью не предлагаем). */
  family: ProfileSetupTarget[];
  meSwimmerId: number | null;
  familyMax: number | null;
  icon: React.ReactNode;
  onClose: () => void;
  /** Сохранить выбор: одиночный id (Me, клуб, группа) или весь список (семья). Ошибка — текстом. */
  onSave: (ids: number[]) => Promise<string | null>;
  onRemove: () => Promise<string | null>;
}

function ProfileEditPopup({
  request, takenClubIds, takenGroupIds, family, meSwimmerId, familyMax, icon, onClose, onSave, onRemove,
}: Props) {
  const deep = useDeepThemeClass();
  const open = request != null;
  const kind = request?.kind ?? 'me';
  const multi = kind === 'family';

  const [query, setQuery] = useState('');
  const [picked, setPicked] = useState<number[]>([]);
  const [confirmRemove, setConfirmRemove] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [clubs, setClubs] = useState<ClubOption[] | null>(null);
  const [groups, setGroups] = useState<GroupOption[] | null>(null);
  const [loadError, setLoadError] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const initial = useMemo(
    () => (multi ? family.map((f) => f.id) : request?.current ? [request.current.id] : []),
    [multi, family, request],
  );

  // Новое открытие — чистый лист: выбор = текущее значение.
  useEffect(() => {
    if (!open) return;
    setQuery('');
    setPicked(initial);
    setConfirmRemove(false);
    setError(null);
    setLoadError(false);
    if (kind === 'club') loadClubs().then(setClubs).catch(() => setLoadError(true));
    if (kind === 'group') loadGroups().then(setGroups).catch(() => setLoadError(true));
    // autofocus поиска: окно открыли, чтобы печатать.
    const t = setTimeout(() => inputRef.current?.focus(), 0);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, kind, request]);

  // Esc закрывает; плавающая кнопка темы прячется, пока окно открыто (как у окна H2H).
  useEffect(() => {
    if (!open) return undefined;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    document.body.classList.add('h2h-modal-open');
    return () => {
      window.removeEventListener('keydown', onKey);
      document.body.classList.remove('h2h-modal-open');
    };
  }, [open, onClose]);

  const found = useSwimmerSearch(open && (kind === 'me' || kind === 'family') ? query : '');

  const options: Option[] | null = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (kind === 'club') {
      if (!clubs) return null;
      // Псевдоклубы («No club», страны делегаций) своим клубом не выбирают. Текущий — первым,
      // дальше крупные клубы: искать свой проще среди тех, где много пловцов.
      const currentId = request?.current?.id;
      return clubs
        .filter((c) => c.name && !c.isPseudo && (!q || c.name.toLowerCase().includes(q)))
        .sort((a, b) => Number(b.id === currentId) - Number(a.id === currentId)
          || (b.swimmerCount ?? 0) - (a.swimmerCount ?? 0))
        .slice(0, 40)
        .map((c) => ({
          id: c.id,
          name: c.name,
          sub: c.swimmerCount ? `${c.swimmerCount} swimmers` : undefined,
          taken: c.id !== request?.current?.id && takenClubIds.has(c.id) ? 'Added' : undefined,
        }));
    }
    if (kind === 'group') {
      if (!groups) return null;
      return groups
        .filter((g) => !q || g.name.toLowerCase().includes(q) || (g.club_name ?? '').toLowerCase().includes(q))
        .slice(0, 40)
        .map((g) => ({
          id: g.id,
          name: g.name,
          sub: g.club_name ?? undefined,
          taken: g.id !== request?.current?.id && takenGroupIds.has(g.id) ? 'Joined' : undefined,
          approval: g.join_policy === 'approval',
        }));
    }
    // Пловцы: сначала то, что уже выбрано (текущее «это я» / семья), потом выдача поиска.
    const head: Option[] = kind === 'family'
      ? family.map((f) => ({ id: f.id, name: f.name }))
      : request?.current ? [{ id: request.current.id, name: request.current.name }] : [];
    const hits: Option[] = (found.data ?? [])
      .filter((h) => !head.some((x) => x.id === h.id))
      // Себя в семью не предлагаем: семья сняла бы звезду «Me» (уровни исключают друг друга).
      .filter((h) => kind !== 'family' || h.id !== meSwimmerId)
      .map((h) => ({
        id: h.id,
        name: h.name,
        sub: [h.birthYear ? String(h.birthYear) : null, h.clubName].filter(Boolean).join(' · ') || undefined,
      }));
    return [...head, ...hits];
  }, [kind, clubs, groups, query, found.data, family, request, takenClubIds, takenGroupIds, meSwimmerId]);

  if (!open || !request) return null;

  const changed = picked.length !== initial.length || picked.some((id) => !initial.includes(id));
  const full = multi && familyMax != null && picked.length >= familyMax;
  const pickedApproval = kind === 'group' && options?.find((o) => o.id === picked[0])?.approval
    && picked[0] !== request.current?.id;
  const title = TITLES[kind][request.mode === 'change' ? 'change' : 'add'];
  const canRemove = request.mode === 'change' && (kind !== 'family' || family.length > 0);
  const currentName = kind === 'family' ? 'your family list' : request.current?.name ?? TITLES[kind].noun;

  const toggle = (id: number) => {
    setError(null);
    if (!multi) { setPicked([id]); return; }
    setPicked((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : full ? prev : [...prev, id]));
  };

  const run = async (action: () => Promise<string | null>) => {
    setBusy(true);
    setError(null);
    try {
      const err = await action();
      if (err) setError(err);
      else onClose();
    } finally {
      setBusy(false);
    }
  };

  const isSwimmerSearch = kind === 'me' || kind === 'family';
  const typing = query.trim().length >= 2;

  return createPortal(
    <div
      className={`${deep} fixed inset-0 z-[100] flex items-end justify-center bg-[rgba(15,42,58,0.42)] sm:items-center sm:p-4`}
      onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className="flex max-h-[85vh] w-full flex-col rounded-t-[14px] bg-[var(--t-card)] text-[var(--t-text)] shadow-[0_24px_60px_rgba(15,42,58,0.35)] sm:w-[400px] sm:rounded-[14px]"
      >
        <div className="flex items-center gap-2.5 border-b border-[var(--t-border)] px-[18px] py-4">
          <span className="flex w-5 shrink-0 justify-center text-[var(--t-accent)]">{icon}</span>
          <h2 className="m-0 flex-1 text-[16px] font-extrabold">{title}</h2>
          <button type="button" onClick={onClose} aria-label="Close" className="text-[18px] leading-none text-[var(--t-text-2)] hover:text-[var(--t-text)]">×</button>
        </div>

        <div className="flex min-h-0 flex-1 flex-col gap-2.5 px-[18px] py-4">
          <input
            ref={inputRef}
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={PLACEHOLDERS[kind]}
            aria-label={PLACEHOLDERS[kind].replace(/…$/, '')}
            className="h-[38px] w-full rounded-[8px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-3 text-[14px] text-[var(--t-text)] outline-none focus:border-[var(--t-accent)]"
          />
          {multi && familyMax != null && (
            <div className="text-[12px] text-[var(--t-text-3)]">
              {picked.length}/{familyMax} marked{full ? ' — unmark someone to add another' : ''}
            </div>
          )}

          <div className="min-h-[120px] flex-1 overflow-y-auto" role={multi ? 'group' : 'radiogroup'} aria-label={title}>
            {loadError ? (
              <p className="m-0 py-3 text-[13px] text-[var(--t-text-2)]">Could not load the list right now.</p>
            ) : options == null || (isSwimmerSearch && found.loading && options.length === 0) ? (
              <p className="m-0 py-3 text-[13px] text-[var(--t-text-2)]">Loading…</p>
            ) : options.length === 0 ? (
              <p className="m-0 py-3 text-[13px] text-[var(--t-text-2)]">
                {isSwimmerSearch && !typing ? 'Type at least two letters.' : 'Nothing found.'}
              </p>
            ) : (
              options.map((o) => {
                const on = picked.includes(o.id);
                const isCurrent = initial.includes(o.id);
                const disabled = !!o.taken || (multi && full && !on);
                return (
                  <button
                    key={o.id}
                    type="button"
                    role={multi ? 'checkbox' : 'radio'}
                    aria-checked={on}
                    disabled={disabled}
                    onClick={() => toggle(o.id)}
                    className={`flex w-full items-center gap-2.5 rounded-[8px] px-2.5 py-2 text-left transition-colors disabled:cursor-default disabled:opacity-50 ${
                      on ? 'bg-[var(--t-accent-soft)]' : 'hover:bg-[var(--t-accent-soft)]'
                    }`}
                  >
                    <span
                      aria-hidden="true"
                      className={`flex h-4 w-4 shrink-0 items-center justify-center border-2 ${multi ? 'rounded-[4px]' : 'rounded-full'} ${
                        on ? 'border-[var(--t-accent)] bg-[var(--t-accent)]' : 'border-[var(--t-border)]'
                      }`}
                    >
                      {on && <span className={`block bg-[var(--t-accent-ink)] ${multi ? 'h-1.5 w-1.5 rounded-[1px]' : 'h-1.5 w-1.5 rounded-full'}`} />}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span dir="auto" className="block truncate text-[14px] font-semibold">{o.name}</span>
                      {(o.sub || isCurrent || o.taken) && (
                        <span className="block truncate text-[12px] text-[var(--t-text-2)]">
                          {[isCurrent ? 'Current' : o.taken, o.sub].filter(Boolean).join(' · ')}
                        </span>
                      )}
                    </span>
                  </button>
                );
              })
            )}
          </div>

          {/* Плашка последствий — только когда выбор что-то затронет (Р8: смена клуба группу не трогает). */}
          {pickedApproval && (
            <div className="flex items-start gap-2 rounded-[8px] bg-[color-mix(in_srgb,var(--t-danger)_8%,var(--t-card))] px-3 py-2 text-[12px] text-[var(--t-text)]">
              <span aria-hidden="true" className="text-[var(--t-danger)]">ⓘ</span>
              <span>The coach will confirm. Until then it shows as Pending.</span>
            </div>
          )}
          {error && <p className="m-0 text-[13px] text-[var(--t-danger)]">{error}</p>}
        </div>

        <div className="flex items-center gap-2 border-t border-[var(--t-border)] px-[18px] py-3">
          {confirmRemove ? (
            // Подтверждение — в том же футере, второго окна нет (хендофф 5d).
            <>
              <span dir="auto" className="min-w-0 flex-1 truncate text-[13px] font-semibold">Remove {currentName}?</span>
              <button type="button" disabled={busy} onClick={() => setConfirmRemove(false)} className="h-8 rounded-[8px] border border-[var(--t-border)] px-3 text-[13px] font-semibold">
                Cancel
              </button>
              <button
                type="button"
                disabled={busy}
                onClick={() => { void run(onRemove); }}
                className="h-8 rounded-[8px] bg-[var(--t-danger)] px-3 text-[13px] font-bold text-white disabled:opacity-50"
              >
                Remove
              </button>
            </>
          ) : (
            <>
              {canRemove && (
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => setConfirmRemove(true)}
                  className="flex items-center gap-1 text-[13px] font-semibold text-[var(--t-danger)] hover:underline"
                >
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                    <path d="M4 7h16M9 7V4h6v3M6.5 7l1 13h9l1-13" />
                  </svg>
                  Remove {TITLES[kind].noun}
                </button>
              )}
              <span className="flex-1" />
              <button type="button" disabled={busy} onClick={onClose} className="h-8 rounded-[8px] border border-[var(--t-accent)] px-3 text-[13px] font-semibold text-[var(--t-accent)]">
                Cancel
              </button>
              <button
                type="button"
                disabled={busy || !changed || (!multi && picked.length === 0)}
                onClick={() => { void run(() => onSave(picked)); }}
                className="h-8 rounded-[8px] bg-[var(--t-accent)] px-4 text-[13px] font-bold text-[var(--t-accent-ink)] disabled:opacity-40"
              >
                Save
              </button>
            </>
          )}
        </div>
      </div>
    </div>,
    document.body,
  );
}

export default ProfileEditPopup;
