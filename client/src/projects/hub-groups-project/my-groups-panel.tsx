import { useCallback, useState } from 'react';
import { useCurrentIdentity, useGroupCreationPolicy, useMyHubGroups } from './use-my-hub-groups';
import DeleteGroupDialog from './components/delete-group-dialog';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import type { GroupCreationPolicy, HubGroupInput, MyHubGroupRow } from './my-groups-types';
import { routes } from '../../utils/routes';
import { cardCls, btnCls, btnDangerCls } from './my-groups/my-groups-shared';
import { GroupInputForm } from './my-groups/group-input-form';
import { EditGroupCard } from './my-groups/edit-group-card';

const EMPTY_INPUT: HubGroupInput = {
  name: '', nameEn: '', slug: '', description: '', iconUrl: '', coverImageUrl: '', location: '',
  country: '', clubId: null, isPublic: true, joinPolicy: 'open', links: [],
};

/**
 * Гостю панели «My groups» нет — вместо неё подсказка, что группу можно создать, и вход.
 * Без неё создание было не найти: кнопка живёт только в панели, а панель видят только
 * вошедшие. Обещать можно только то, что открыто: при политике `admin` молчим, при `coach`
 * так и пишем. Пока политика грузится — тоже молчим, чтобы текст не мигал.
 */
function GuestCreateHint({ policy }: { policy: GroupCreationPolicy | null }) {
  const { openLoginModal } = useLoginModal();
  if (policy == null || policy === 'admin') return null;

  return (
    <section className="px-4 pt-[26px] lg:px-16" aria-label="Create a group">
      <div className={`${cardCls} flex flex-wrap items-center justify-between gap-4`}>
        <div className="min-w-0 max-w-[640px]">
          <h2 className="mb-1.5 text-[15px] font-black uppercase tracking-[0.2em] text-[var(--t-accent)]">
            Create your own group
          </h2>
          <p className="m-0 text-[13px] text-[var(--t-text-2)]">
            {policy === 'coach'
              ? 'Creating groups is open to coaches right now. Sign in with your coach account to start one.'
              : 'Coach a squad or train with friends from different clubs? Sign in to create a group and follow everyone’s swims, records and season standings in one place.'}
          </p>
        </div>
        <button
          type="button"
          onClick={openLoginModal}
          className="hp-mono shrink-0 cursor-pointer rounded-[11px] bg-[var(--t-accent)] px-4 py-2 text-[13px] font-extrabold text-[var(--t-accent-ink)]"
        >
          Sign in to create a group
        </button>
      </div>
    </section>
  );
}

/**
 * Панель самообслуживания групп (8.6) — на странице списка групп. Вошедшим — список, лимит и
 * создание; гостю — подсказка со входом (`GuestCreateHint`).
 */
export default function MyGroupsPanel() {
  const identity = useCurrentIdentity();
  const mine = useMyHubGroups(identity.isAuthenticated);
  const guestPolicy = useGroupCreationPolicy(!identity.loading && !identity.isAuthenticated);
  const [creating, setCreating] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [deletingGroup, setDeletingGroup] = useState<MyHubGroupRow | null>(null);
  const closeDeleteDialog = useCallback(() => setDeletingGroup(null), []);

  if (identity.loading) return null;
  if (!identity.isAuthenticated) return <GuestCreateHint policy={guestPolicy} />;

  const eligibility = mine.eligibility;
  // Удалять может только владелец (или site-админ): в списке лежат и группы, где ты админ.
  const canDelete = (g: MyHubGroupRow) => identity.isAdmin || g.ownerUserId === identity.userId;

  const confirmDelete = async (g: MyHubGroupRow) => {
    const result = await mine.deleteGroup(g.id);
    if (result.success) {
      setDeletingGroup(null);
      if (editingId === g.id) setEditingId(null);
    }
    return result;
  };

  return (
    <section className="px-4 pt-[26px] lg:px-16" aria-label="My groups">
      <div className={cardCls}>
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
          <h2 className="text-[15px] font-black uppercase tracking-[0.2em] text-[var(--t-accent)]">My groups</h2>
          <div className="flex items-center gap-3">
            {eligibility?.limit != null && (
              <span className="hp-mono text-[11.5px] font-bold text-[var(--t-text-2)]"
                title="Groups you own out of your limit (official groups count too)">
                {eligibility.owned} / {eligibility.limit} groups
              </span>
            )}
            {!creating && eligibility?.canCreate && (
              <button type="button" className={btnCls} onClick={() => setCreating(true)}>
                + Create group
              </button>
            )}
          </div>
        </div>

        {!creating && !eligibility?.canCreate && eligibility?.reason && (
          <p className="mb-3 text-[12.5px] text-[var(--t-text-2)]">{eligibility.reason}</p>
        )}

        {deletingGroup && (
          <DeleteGroupDialog
            groupId={deletingGroup.id}
            groupName={deletingGroup.name}
            onConfirm={() => confirmDelete(deletingGroup)}
            onClose={closeDeleteDialog}
          />
        )}

        {creating && (
          <div className="mb-4">
            <GroupInputForm
              initial={EMPTY_INPUT}
              submitLabel="Create"
              onCancel={() => setCreating(false)}
              onSubmit={async (input) => { const r = await mine.createGroup(input); if (r.success) setCreating(false); return r; }}
            />
          </div>
        )}

        {mine.loading ? (
          <p className="text-[13px] text-[var(--t-text-2)]">Loading…</p>
        ) : mine.groups.length === 0 && !creating ? (
          <p className="text-[13px] text-[var(--t-text-2)]">You do not own or co-coach any group yet.</p>
        ) : (
          <ul className="m-0 flex list-none flex-col gap-3 p-0">
            {mine.groups.map((g) => (
              <li key={g.id}>
                <div className="flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <a href={routes.group(g.slug)}
                      className="truncate text-[14px] font-extrabold text-[var(--t-text)] no-underline hover:underline">
                      {g.name}
                    </a>
                    {g.isOfficial && (
                      <span className="hp-mono ml-2 rounded-[6px] border border-[var(--t-border)] px-[6px] py-[2px] text-[10px] font-extrabold text-[var(--t-text-2)]">
                        Official · {g.clubName}
                      </span>
                    )}
                    <div className="text-[11.5px] text-[var(--t-text-2)]">
                      {g.memberCount} · swimmers
                      {!g.isOfficial && g.followedClubName && (
                        <> · from club <bdi>{g.followedClubName}</bdi></>
                      )}
                    </div>
                    {/* Официальная группа клуба убрала копию из каталога (П4). Писем не шлём —
                        владелец узнаёт отсюда; текст считает сервер. */}
                    {g.hiddenByOfficialGroup && g.catalogNotice && (
                      <p className="m-0 mt-1 text-[11.5px] font-bold text-[var(--t-warn)]">
                        {g.catalogNotice}{' '}
                        {g.officialGroupSlug && (
                          <a href={routes.group(g.officialGroupSlug)}
                            className="text-[var(--t-accent)] no-underline hover:underline">
                            Open the official group →
                          </a>
                        )}
                      </p>
                    )}
                  </div>
                  <div className="flex shrink-0 gap-2">
                    <button type="button" className={btnCls}
                      onClick={() => setEditingId(editingId === g.id ? null : g.id)}>
                      {editingId === g.id ? 'Close' : 'Edit'}
                    </button>
                    {canDelete(g) && (
                      <button type="button" className={btnDangerCls} onClick={() => setDeletingGroup(g)}>
                        Delete
                      </button>
                    )}
                  </div>
                </div>
                {editingId === g.id && (
                  <div className="mt-3 border-t border-[var(--t-border)] pt-3">
                    <EditGroupCard
                      row={g}
                      currentUserId={identity.userId}
                      isAdmin={identity.isAdmin}
                      onClose={() => setEditingId(null)}
                      onSaved={() => mine.reload()}
                    />
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
