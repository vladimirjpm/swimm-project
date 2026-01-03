import './club-icon.css';
import { useAppDispatch } from '../../../../store/store';
import React, { useEffect, useMemo, useState } from 'react';

type ClubIconsManifest = {
  files?: string[];
};

let clubIconsManifestPromise: Promise<Set<string> | null> | null = null;

const loadClubIconsManifest = (baseUrl: string) => {
  if (clubIconsManifestPromise) return clubIconsManifestPromise;

  const url = `${baseUrl}data/club-icons-manifest.json`;
  clubIconsManifestPromise = fetch(url)
    .then(async (res) => {
      if (!res.ok) return null;
      const json = (await res.json()) as ClubIconsManifest;
      const files = Array.isArray(json?.files) ? json.files : [];
      return new Set(files);
    })
    .catch(() => null);

  return clubIconsManifestPromise;
};

interface UI_ClubIconProps {
  clubName: string;
  className?: string;
  iconWidth: string;
  styleType?: 'icon-notext' | 'icon-text-bottom' | 'icon-text-right';
}

const UI_ClubIcon: React.FC<UI_ClubIconProps> = ({
  clubName,
  className = '',
  iconWidth,
  styleType = 'icon-notext',
}) => {
  const dispatch = useAppDispatch();

  const base = import.meta.env.BASE_URL;
  const safeClubName = (clubName ?? '').trim();
  const fileBaseName = safeClubName
    .replaceAll(' ', '-')
    .replaceAll('"', '-')
    .replaceAll("'", '-')
    .replaceAll('/', '-')
    .replaceAll('\\', '-')
    .replaceAll('?', '-')
    .replaceAll('#', '-');

  const fallbackSrc = `${base}images/club-icon/no-club.png`;
  const rawFileName = fileBaseName ? `${fileBaseName}.png` : 'no-club.png';
  const iconFileName = fileBaseName ? `${encodeURIComponent(fileBaseName)}.png` : 'no-club.png';
  const imageSrc = `${base}images/club-icon/${iconFileName}`;

  const [resolvedSrc, setResolvedSrc] = useState<string>(imageSrc);

  const resolvedTitle = useMemo(() => clubName, [clubName]);

  useEffect(() => {
    let cancelled = false;

    if (!fileBaseName) {
      setResolvedSrc(fallbackSrc);
      return;
    }

    loadClubIconsManifest(base).then((set) => {
      if (cancelled) return;

      if (!set) {
        setResolvedSrc(imageSrc);
        return;
      }

      if (set.has(rawFileName)) {
        setResolvedSrc(imageSrc);
      } else {
        setResolvedSrc(fallbackSrc);
      }
    });

    return () => {
      cancelled = true;
    };
  }, [base, fallbackSrc, fileBaseName, imageSrc, rawFileName]);
//console.log('imageSrc: ',imageSrc);
  const img = (
    <img
      src={resolvedSrc}
      alt={clubName}
      title={resolvedTitle}
      className={`w-${iconWidth} h-${iconWidth} object-contain`}
      onError={(e) => {
        if (e.currentTarget.src !== fallbackSrc) {
          e.currentTarget.src = fallbackSrc;
        }
      }}
    />
  );

  if (styleType === 'icon-text-bottom') {
    return (
      <div className={`dv-club-icon flex flex-col items-center space-y-1 text-gray-800 text-base  ${className}`}>
        {img}
        <span>{clubName}</span>
      </div>
    );
  }

  if (styleType === 'icon-text-right') {
    return (
      <div className={`dv-club-icon flex flex-row items-center gap-2 text-gray-800 text-base w-${iconWidth} h-${iconWidth} ${className}`}>
        {img}
        <span className="w-fit text-2xl">{clubName}</span>
      </div>
    );
  }

  return (
    <div className={`dv-club-icon h-auto flex items-center justify-center text-gray-900 w-${iconWidth} h-${iconWidth} ${className}`}>
      {img}
    </div>
  );
};

export default UI_ClubIcon;
