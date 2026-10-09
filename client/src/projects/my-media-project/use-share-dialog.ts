import { useState } from 'react';
import {
  parseTargetKey, targetKey, type PublishTargetRef, type UserMediaPublicationDto,
} from '../../hooks/useUserMedia';
import type { AllUserMediaDto, PublishTargetDto } from './use-all-my-media';

type Level = 'members' | 'public';

/**
 * Публикация медиа в группу/клуб: модал «Share» (mobile и Unlinked; разметка — ShareDialog)
 * и инлайн-share строки заплыва (SwimList). Правило «withdraw + резаявка» — в `publishTo`,
 * одном месте на оба входа.
 */
export function useShareDialog(params: {
  publicationsByMedia: Map<number, UserMediaPublicationDto[]>;
  submitPublication: (mediaId: number, target: PublishTargetRef, level: Level) => Promise<{ ok: boolean; error?: string }>;
  withdrawPublication: (mediaId: number, target: PublishTargetRef) => Promise<boolean>;
}) {
  const { publicationsByMedia, submitPublication, withdrawPublication } = params;

  const [shareTarget, setShareTarget] = useState<AllUserMediaDto | null>(null);
  const [shareTargets, setShareTargets] = useState<PublishTargetDto[] | null>(null);
  // Ключ цели строкой («group:17» / «club:438»): у `<select>` значение всегда строка, а
  // цель теперь пара (тип, id) — числом её не выразить.
  const [shareTargetKey, setShareTargetKey] = useState<string>('');
  const [shareLevel, setShareLevel] = useState<Level>('members');
  const [shareBusy, setShareBusy] = useState(false);
  const [shareError, setShareError] = useState<string | null>(null);

  // ── Share (модал — mobile и Unlinked; inline share живёт в SwimList) ─────
  const openShare = async (item: AllUserMediaDto) => {
    setShareTarget(item);
    setShareTargets(null);
    setShareError(null);
    const active = (publicationsByMedia.get(item.id) ?? []).find(
      (p) => p.status === 'pending' || p.status === 'approved'
    );
    setShareTargetKey(active ? targetKey({ type: active.target_type, id: active.target_id }) : '');
    setShareLevel(active ? active.level : 'members');
    try {
      const r = await fetch(`/api/me/media/${item.id}/publish-targets`, { credentials: 'include' });
      setShareTargets(r.ok ? await r.json() : []);
    } catch {
      setShareTargets([]);
    }
  };

  // Одно место на модал и на инлайн-строку заплыва: сервер запрещает переподачу, пока
  // публикация в этой группе pending/approved («publication already exists»), поэтому
  // смена уровня (members ↔ everyone) идёт через withdraw + резаявку.
  const publishTo = async (
    mediaId: number, target: PublishTargetRef, level: Level
  ): Promise<{ ok: boolean; error?: string }> => {
    const active = (publicationsByMedia.get(mediaId) ?? []).find(
      (p) => p.target_type === target.type && p.target_id === target.id
             && (p.status === 'pending' || p.status === 'approved')
    );
    if (active) {
      if (active.level === level) return { ok: true }; // менять нечего
      await withdrawPublication(mediaId, target);
    }
    return submitPublication(mediaId, target, level);
  };

  const handlePublish = async () => {
    const target = parseTargetKey(shareTargetKey);
    if (shareTarget == null || target == null) return;
    setShareBusy(true);
    setShareError(null);
    const res = await publishTo(shareTarget.id, target, shareLevel);
    setShareBusy(false);
    if (res.ok) setShareTarget(null);
    else setShareError(res.error ?? 'Could not submit the request');
  };

  const submitInlineShare = async (mediaId: number, target: PublishTargetRef, level: Level): Promise<boolean> => {
    const res = await publishTo(mediaId, target, level);
    return res.ok;
  };

  return {
    shareTarget, setShareTarget,
    shareTargets,
    shareTargetKey, setShareTargetKey,
    shareLevel, setShareLevel,
    shareBusy, shareError,
    openShare, handlePublish, submitInlineShare,
  };
}

export type ShareDialogState = ReturnType<typeof useShareDialog>;
