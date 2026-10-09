import React, { useState } from 'react';
import { useMyHubGroupEdit } from '../use-my-hub-groups';
import type { HubGroupMemberRow, PrivateSwimmerInput, SaveResult } from '../my-groups-types';
import { inputCls, btnCls, btnDangerCls, type GroupEdit } from './my-groups-shared';
import { ClubSubscriptionBlock } from './club-subscription-block';

const subHeaderCls =
  'm-0 flex w-full cursor-pointer items-center gap-1.5 border-none bg-transparent p-0 text-left text-[11.5px] font-extrabold uppercase tracking-[0.12em] text-[var(--t-accent-dim)]';

/** Строка пловца в редакторе состава: имя (иврит по умолчанию), год, роль и действие справа. */
function MemberLine({ m, edit, action, showRole = true }: {
  m: HubGroupMemberRow;
  edit: GroupEdit;
  action: React.ReactNode;
  showRole?: boolean;
}) {
  return (
    <li className="flex flex-wrap items-center justify-between gap-2">
      <span className="min-w-0 truncate text-[13px] text-[var(--t-text)]">
        <bdi>{m.swimmerName || m.swimmerNameEn}</bdi>
        {m.birthYear > 0 && <span className="ml-1.5 text-[11px] text-[var(--t-text-3)]">{m.birthYear}</span>}
        {m.isPrivate && (
          <span className="ml-1.5 rounded-[6px] bg-[var(--t-accent-soft)] px-1.5 py-[1px] text-[10px] font-extrabold uppercase text-[var(--t-accent-dim)]"
            title="Added by the group — visible only inside this group">
            group only
          </span>
        )}
      </span>
      <div className="flex shrink-0 items-center gap-2">
        {showRole && (
          <select className={`${inputCls} w-[110px]`} value={m.role}
            onChange={(e) => edit.updateMember(m.id, e.target.value, m.sortOrder)}>
            <option value="member">member</option>
            <option value="captain">captain</option>
            <option value="coach">coach</option>
          </select>
        )}
        {action}
      </div>
    </li>
  );
}

/**
 * «Удалить насовсем» у неактивного пловца группы (Р71): вне группы его нет и поиском не вернуть,
 * поэтому это удаление вместе с временами тренировок. Подтверждение здесь же, вторым нажатием.
 */
function RemovePrivateSwimmerButton({ onConfirm }: { onConfirm: () => void }) {
  const [asking, setAsking] = useState(false);
  if (!asking) {
    return <button type="button" className={btnDangerCls} title="Delete for good, with training times" onClick={() => setAsking(true)}>✕</button>;
  }
  return (
    <span className="flex items-center gap-1.5 whitespace-nowrap text-[11.5px] text-[var(--t-danger)]"
      title="The swimmer exists only in this group: removing deletes them and their training times">
      Delete for good?
      <button type="button" className={btnDangerCls} onClick={() => { setAsking(false); onConfirm(); }}>Delete</button>
      <button type="button" className={btnCls} onClick={() => setAsking(false)}>Keep</button>
    </span>
  );
}

/**
 * «Добавить пловца не из loglig» (Р71): человек без аккаунта и без профиля в федерации, чтобы
 * ставить его на дорожки и писать времена тренировок. Такой пловец виден только внутри
 * группы — поэтому и имя проверяется лишь санитарно (длина), а не по списку слов.
 */
function AddPrivateSwimmerForm({ onAdd }: { onAdd: (input: PrivateSwimmerInput) => Promise<SaveResult> }) {
  const empty: PrivateSwimmerInput = { firstName: '', lastName: '', gender: '', birthYear: null };
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState<PrivateSwimmerInput>(empty);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    const result = await onAdd(input);
    setBusy(false);
    if (result.success) { setInput(empty); setOpen(false); } else setError(result.error ?? 'Could not add the swimmer');
  };

  if (!open) {
    return (
      <button type="button" className={`${btnCls} self-start`} onClick={() => setOpen(true)}>
        + Add a swimmer who isn’t on loglig
      </button>
    );
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-2 rounded-[10px] border border-[var(--t-accent-border)] p-3">
      <p className="m-0 text-[12px] text-[var(--t-text-2)]">
        For someone who trains with the group but has no account and no loglig profile. Visible only
        inside this group — no public page, not in search.
      </p>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        <input id="private-swimmer-first" className={inputCls} placeholder="First name" dir="auto" maxLength={50}
          value={input.firstName} onChange={(e) => setInput({ ...input, firstName: e.target.value })} />
        <input id="private-swimmer-last" className={inputCls} placeholder="Last name" dir="auto" maxLength={50}
          value={input.lastName} onChange={(e) => setInput({ ...input, lastName: e.target.value })} />
        <select id="private-swimmer-gender" className={inputCls} value={input.gender}
          onChange={(e) => setInput({ ...input, gender: e.target.value as PrivateSwimmerInput['gender'] })}>
          <option value="">Gender — not set</option>
          <option value="male">Male</option>
          <option value="female">Female</option>
        </select>
        <input id="private-swimmer-year" className={inputCls} placeholder="Birth year (optional)" inputMode="numeric"
          value={input.birthYear ?? ''}
          onChange={(e) => {
            const digits = e.target.value.replace(/\D/g, '').slice(0, 4);
            setInput({ ...input, birthYear: digits ? Number(digits) : null });
          }} />
      </div>
      {error && <p className="m-0 text-[12px] font-bold text-[var(--t-danger)]" role="alert">{error}</p>}
      <div className="flex gap-2">
        <button type="submit" className={btnCls}
          disabled={busy || !input.firstName.trim() || !input.lastName.trim()}>
          {busy ? 'Adding…' : 'Add to the group'}
        </button>
        <button type="button" className={btnCls} onClick={() => { setOpen(false); setError(null); }}>Cancel</button>
      </div>
    </form>
  );
}

/**
 * Состав пловцов группы. Три вида строк (docs/hubgroups-architecture.md §4а):
 *   · добавленные руками — как всегда, с ✕;
 *   · пришедшие из подписки на клуб — свёрнутым списком с поиском (их бывает 150+) и «Hide»
 *     вместо ✕: удалённого клубного вернула бы следующая пересборка;
 *   · скрытые владельцем — отдельно, с «Restore».
 * Ручной пловец и пловец группы (Р71) уходят в «Inactive» (Deactivate, Р74 — 30.09.2026): из
 * состава, дорожек и рекордов группы, Restore возвращает; удалить насовсем — оттуда.
 */
export function MembersEditor({ hubGroupId, clubId, onRosterChanged }: {
  hubGroupId: number;
  clubId?: number | null;
  /** Состав сдвинулся (подписка/отписка) — обновить счётчик в списке «My groups». */
  onRosterChanged?: () => void;
}) {
  const edit = useMyHubGroupEdit(hubGroupId);
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<Awaited<ReturnType<typeof edit.searchSwimmers>>>([]);
  const [clubSwimmers, setClubSwimmers] = useState<Awaited<ReturnType<typeof edit.getClubSwimmers>> | null>(null);
  const [showFromClub, setShowFromClub] = useState(false);
  const [clubFilter, setClubFilter] = useState('');
  const [showHidden, setShowHidden] = useState(false);
  const [showInactive, setShowInactive] = useState(false);

  if (!edit.data) return null;

  const onSearch = async (q: string) => {
    setQuery(q);
    setResults(q.trim().length > 0 ? await edit.searchSwimmers(q) : []);
  };

  const toggleClubSwimmers = async () => {
    if (clubSwimmers != null) { setClubSwimmers(null); return; }
    if (clubId) setClubSwimmers(await edit.getClubSwimmers(clubId));
  };

  const subscription = edit.data.clubSubscription ?? null;
  const manual = edit.data.members.filter((m) => m.source !== 'club' && !m.isExcluded);
  const fromClub = edit.data.members.filter((m) => m.source === 'club' && !m.isExcluded);
  const hidden = edit.data.members.filter((m) => m.source === 'club' && m.isExcluded);
  // Неактивные — пловцы группы (Р71) и ручные (Р74): вне состава и дорожек, времена целы —
  // отсюда Restore и удаление насовсем.
  const inactive = edit.data.members.filter((m) => m.source !== 'club' && m.isExcluded);

  const filter = clubFilter.trim().toLowerCase();
  const fromClubShown = filter.length === 0
    ? fromClub
    : fromClub.filter((m) => `${m.swimmerName} ${m.swimmerNameEn}`.toLowerCase().includes(filter));

  return (
    <div className="flex flex-col gap-2">
      <ClubSubscriptionBlock edit={edit} clubRows={fromClub.length + hidden.length} onChanged={onRosterChanged} />

      {subscription && (
        <p className="m-0 mt-1 text-[11.5px] font-extrabold uppercase tracking-[0.12em] text-[var(--t-accent-dim)]">
          Added by you · {manual.length}
        </p>
      )}
      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {manual.map((m) => (
          <MemberLine key={m.id} m={m} edit={edit}
            action={(
              <button type="button" className={btnCls}
                title="Take off the roster, lanes and group records — times are kept, restore any time"
                onClick={() => edit.setMemberExcluded(m.id, true)}>
                Deactivate
              </button>
            )} />
        ))}
        {edit.data.members.length === 0 && (
          <p className="text-[12.5px] text-[var(--t-text-2)]">The roster is empty.</p>
        )}
      </ul>

      {fromClub.length > 0 && (
        <div className="flex flex-col gap-2">
          <button type="button" className={subHeaderCls} aria-expanded={showFromClub}
            onClick={() => setShowFromClub((v) => !v)}>
            {showFromClub ? '▾' : '▸'} From the club · {fromClub.length}
          </button>
          {showFromClub && (
            <>
              <input className={inputCls} placeholder="Filter by name…" value={clubFilter}
                onChange={(e) => setClubFilter(e.target.value)} />
              <ul className="m-0 flex max-h-[360px] list-none flex-col gap-2 overflow-y-auto p-0 pr-1">
                {fromClubShown.map((m) => (
                  <MemberLine key={m.id} m={m} edit={edit}
                    action={(
                      <button type="button" className={btnCls}
                        title="Hide from the group everywhere — the next club update won't bring the swimmer back"
                        onClick={() => edit.setMemberExcluded(m.id, true)}>
                        Hide
                      </button>
                    )} />
                ))}
                {fromClubShown.length === 0 && (
                  <p className="text-[12.5px] text-[var(--t-text-2)]">No swimmers match the filter.</p>
                )}
              </ul>
            </>
          )}
        </div>
      )}

      {inactive.length > 0 && (
        <div className="flex flex-col gap-2">
          <button type="button" className={subHeaderCls} aria-expanded={showInactive}
            onClick={() => setShowInactive((v) => !v)}>
            {showInactive ? '▾' : '▸'} Inactive · {inactive.length}
          </button>
          {showInactive && (
            <ul className="m-0 flex list-none flex-col gap-2 p-0">
              {inactive.map((m) => (
                <MemberLine key={m.id} m={m} edit={edit} showRole={false}
                  action={(
                    <span className="flex items-center gap-2">
                      <button type="button" className={btnCls} onClick={() => edit.setMemberExcluded(m.id, false)}>
                        Restore
                      </button>
                      {m.isPrivate
                        ? <RemovePrivateSwimmerButton onConfirm={() => edit.removeMember(m.id)} />
                        // Пловец из loglig остаётся в справочнике — убираем только строку группы.
                        : (
                          <button type="button" className={btnDangerCls} title="Remove from the group for good"
                            onClick={() => edit.removeMember(m.id)}>
                            ✕
                          </button>
                        )}
                    </span>
                  )} />
              ))}
            </ul>
          )}
        </div>
      )}

      {hidden.length > 0 && (
        <div className="flex flex-col gap-2">
          <button type="button" className={subHeaderCls} aria-expanded={showHidden}
            onClick={() => setShowHidden((v) => !v)}>
            {showHidden ? '▾' : '▸'} Hidden · {hidden.length}
          </button>
          {showHidden && (
            <ul className="m-0 flex list-none flex-col gap-2 p-0">
              {hidden.map((m) => (
                <MemberLine key={m.id} m={m} edit={edit} showRole={false}
                  action={(
                    <button type="button" className={btnCls} onClick={() => edit.setMemberExcluded(m.id, false)}>
                      Restore
                    </button>
                  )} />
              ))}
            </ul>
          )}
        </div>
      )}

      <div className="relative">
        <input className={inputCls} placeholder="Find a swimmer by last name…" value={query}
          onChange={(e) => onSearch(e.target.value)} />
        {results.length > 0 && (
          <ul className="absolute z-10 m-0 mt-1 flex max-h-[220px] w-full list-none flex-col gap-1 overflow-y-auto rounded-[10px] border border-[var(--t-accent-border)] bg-[var(--t-surface-strong)] p-2">
            {results.map((s) => (
              <li key={s.id}>
                <button type="button"
                  className="w-full cursor-pointer rounded-[6px] border-none bg-transparent px-2 py-1 text-left text-[12.5px] text-[var(--t-text)] hover:bg-[var(--t-accent-soft)]"
                  onClick={async () => { await edit.addMember(s.id, 'member'); setQuery(''); setResults([]); }}>
                  {s.name || s.nameEn} {s.birthYear > 0 ? `(${s.birthYear})` : ''}
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      <AddPrivateSwimmerForm onAdd={edit.addPrivateSwimmer} />

      {/* Ручной подбор из справочника клуба — только пока нет подписки: с ней клуб и так весь в составе. */}
      {clubId && !subscription && (
        <div>
          <button type="button" className={btnCls} onClick={toggleClubSwimmers}>
            {clubSwimmers != null ? 'Hide club swimmers' : 'Show club swimmers'}
          </button>
          {clubSwimmers != null && (
            <ul className="m-0 mt-2 flex max-h-[220px] list-none flex-col gap-1 overflow-y-auto rounded-[10px] border border-[var(--t-accent-border)] bg-[var(--t-surface-strong)] p-2">
              {clubSwimmers.length === 0 && (
                <p className="px-2 py-1 text-[12.5px] text-[var(--t-text-2)]">No club swimmers found.</p>
              )}
              {clubSwimmers.map((s) => (
                <li key={s.id}>
                  <button type="button"
                    className="w-full cursor-pointer rounded-[6px] border-none bg-transparent px-2 py-1 text-left text-[12.5px] text-[var(--t-text)] hover:bg-[var(--t-accent-soft)]"
                    onClick={() => edit.addMember(s.id, 'member')}>
                    {s.name || s.nameEn} {s.birthYear > 0 ? `(${s.birthYear})` : ''}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
