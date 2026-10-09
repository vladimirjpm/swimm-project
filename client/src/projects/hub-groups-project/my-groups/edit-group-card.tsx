import { useMyHubGroupEdit } from '../use-my-hub-groups';
import type { HubGroupInput, MyHubGroupRow } from '../my-groups-types';
import { GroupInputForm } from './group-input-form';
import { MembersEditor } from './members-editor';
import { AdminsEditor } from './admins-editor';
import { UserMembersEditor } from './user-members-editor';
import { MediaEditor } from './media-editor';
import { ClubRequestPanel } from './club-request-panel';

const DISCLAIMER =
  'The roster is maintained by the group creator and is not an official club or federation entry.';

export function EditGroupCard({ row, currentUserId, isAdmin, onClose, onSaved }: {
  row: MyHubGroupRow; currentUserId: number | null; isAdmin: boolean; onClose: () => void; onSaved: () => void;
}) {
  const edit = useMyHubGroupEdit(row.id);

  if (edit.forbidden) return <p className="text-[13px] font-bold text-[var(--t-danger)]">You do not have rights to edit this group.</p>;
  if (edit.loading || !edit.data) return <p className="text-[13px] text-[var(--t-text-2)]">Loading…</p>;

  const isOwnerOrAdmin = isAdmin || edit.data.ownerUserId === currentUserId;
  const input: HubGroupInput = {
    name: edit.data.name, nameEn: edit.data.nameEn, slug: edit.data.slug, description: edit.data.description,
    iconUrl: edit.data.iconUrl, coverImageUrl: edit.data.coverImageUrl, location: edit.data.location,
    // "" (не null): пустое поле формы = явное «снять страну» на сервере.
    country: edit.data.country ?? '',
    clubId: edit.data.clubId, isPublic: edit.data.isPublic,
    joinPolicy: edit.data.joinPolicy, links: edit.data.links,
  };

  return (
    <div className="flex flex-col gap-4">
      <GroupInputForm
        initial={input}
        recommendApproval={edit.data.isOfficial}
        submitLabel="Save"
        onCancel={onClose}
        onSubmit={async (i) => { const r = await edit.update(i); if (r.success) onSaved(); return r; }}
      />
      <div>
        <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[var(--t-accent)]">Members</h3>
        <MembersEditor hubGroupId={row.id} clubId={edit.data.isOfficial ? edit.data.clubId : null}
          onRosterChanged={onSaved} />
        <p className="mt-2 text-[11.5px] italic text-[var(--t-text-3)]">{DISCLAIMER}</p>
      </div>
      <div>
        <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[var(--t-accent)]">Group admins</h3>
        <AdminsEditor hubGroupId={row.id} isOwnerOrAdmin={isOwnerOrAdmin} />
      </div>
      <div>
        <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[var(--t-accent)]">Account members</h3>
        <UserMembersEditor hubGroupId={row.id} />
      </div>
      <div>
        <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[var(--t-accent)]">Gallery</h3>
        <MediaEditor hubGroupId={row.id} slug={row.slug} />
      </div>
      {isOwnerOrAdmin && !edit.data.isOfficial && (
        <div>
          <h3 className="mb-2 text-[12px] font-black uppercase tracking-[0.18em] text-[var(--t-accent)]">
            Official club status
          </h3>
          <ClubRequestPanel hubGroupId={row.id} />
        </div>
      )}
      {edit.data.isOfficial && (
        <p className="text-[12.5px] font-bold text-[var(--t-accent)]">Official Group of {row.clubName}</p>
      )}
    </div>
  );
}
