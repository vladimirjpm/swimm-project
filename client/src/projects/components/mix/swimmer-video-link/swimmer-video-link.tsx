import React, { useEffect, useState } from 'react';

interface VideoFile {
  file: string;
  swimmer: string;
  style: string;
  distance: string | null;
}

interface VideoCompetition {
  competition: string;
  name: string;
  files: VideoFile[];
}

interface VideosConfig {
  videos: VideoCompetition[];
}

interface UI_SwimmerVideoLinkProps {
  firstNameEn?: string;
  lastNameEn?: string;
  styleName?: string;
  styleLen?: string;
  competition?: string;
  className?: string;
}

let cachedConfig: VideosConfig | null = null;
let configPromise: Promise<VideosConfig> | null = null;

const loadVideosConfig = async (): Promise<VideosConfig> => {
  if (cachedConfig) return cachedConfig;
  if (configPromise) return configPromise;

  configPromise = (async () => {
    try {
      const baseUrl = import.meta.env.BASE_URL || '/';
      const response = await fetch(`${baseUrl}data/config/videos-config.json`);
      if (!response.ok) throw new Error('Failed to load videos config');
      const config = await response.json();
      cachedConfig = config;
      return config;
    } catch (error) {
      console.error('Error loading videos config:', error);
      return { videos: [] };
    } finally {
      configPromise = null;
    }
  })();

  return configPromise;
};

const UI_SwimmerVideoLink: React.FC<UI_SwimmerVideoLinkProps> = ({
  firstNameEn,
  lastNameEn,
  styleName,
  styleLen,
  competition,
  className = '',
}) => {
  const [videoUrl, setVideoUrl] = useState<string | null>(null);
  const [showPopup, setShowPopup] = useState(false);

  // Build swimmer key: "firstname_lastname" in lowercase
  const swimmerKey = [firstNameEn, lastNameEn]
    .filter(Boolean)
    .join('_')
    .toLowerCase()
    .replace(/\s+/g, '_');

  useEffect(() => {
    if (!swimmerKey || !styleName || !styleLen) {
      /* console.log('Видео: НЕ НАЙДЕНО (нет входных данных)', {
        swimmerKey,
        styleName,
        styleLen,
        competition,
      }); */
      setVideoUrl(null);
      return;
    }

    let cancelled = false;

    const findVideo = async () => {
      const config = await loadVideosConfig();

      // Prefer narrowing by competition if provided.
      // Some datasets store a human name (e.g. "Masters Arena Jan 2026"),
      // while videos-config.json uses a folder id (e.g. "2026-01-09-masters-arena").
      // Support both by matching against `competition` OR `name`.
      let competitions = competition
        ? config.videos.filter(
            (c) => c.competition === competition || c.name === competition,
          )
        : config.videos;

      // If narrowing produced no matches, fallback to global search.
      if (competition && competitions.length === 0) {
        competitions = config.videos;
      }

      let foundUrl: string | null = null;

      for (const comp of competitions) {
        for (const file of comp.files) {
          if (
            file.swimmer.toLowerCase() === swimmerKey &&
            file.style.toLowerCase() === styleName.toLowerCase() &&
            file.distance === styleLen
          ) {
            const baseUrl = import.meta.env.BASE_URL || '/';
            foundUrl = `${baseUrl}${file.file}`;
            break;
          }
        }

        if (foundUrl) break;
      }

      if (cancelled) return;

      setVideoUrl(foundUrl);

      if (foundUrl) {
        const fileName = foundUrl.split('/').pop() || foundUrl;
        /* console.log('Видео: НАЙДЕНО', {
          videoUrl: foundUrl,
          fileName,
          swimmerKey,
          styleName,
          styleLen,
          competition,
        }); */
      } else {
        const baseUrl = import.meta.env.BASE_URL || '/';
        const expectedFileName = `${swimmerKey}-${styleName}-${styleLen}.mp4`;
        const expectedUrls = competitions.map(
          (c) => `${baseUrl}video/competition/${c.competition}/${expectedFileName}`,
        );
        /* console.log('Видео: НЕ НАЙДЕНО', {
          foundUrl,
          expectedFileName,
          expectedUrls,
          swimmerKey,
          styleName,
          styleLen,
          competition,
        }); */
      }
    };

    findVideo();

    return () => {
      cancelled = true;
    };
  }, [swimmerKey, styleName, styleLen, competition]);

  if (!videoUrl) {
    return null;
  }

  const handleClick = () => {
    const fileName = videoUrl.split('/').pop() || videoUrl;
    console.log('Opening video popup for URL:', videoUrl, 'file:', fileName);
    setShowPopup(true);
  };

  const handleClosePopup = () => {
    setShowPopup(false);
  };

  return (
    <>
      <div className={`cursor-pointer inline-flex items-center ${className}`} onClick={handleClick}>
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" aria-hidden="true">
          <rect x="3" y="6" width="13" height="12" rx="2" stroke="currentColor" strokeWidth="2"/>
          <path d="M16 10.5l5-3v9l-5-3v-3z" fill="currentColor"/>
        </svg>
      </div>

      {showPopup && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-70"
          onClick={handleClosePopup}
        >
          <div
            className="relative bg-white rounded-lg p-4 max-w-4xl max-h-[90vh]"
            onClick={(e) => e.stopPropagation()}
          >
            <button
              className="absolute top-2 right-2 text-gray-500 hover:text-gray-800 text-2xl font-bold"
              onClick={handleClosePopup}
            >
              ×
            </button>
            <video
              src={videoUrl}
              controls
              autoPlay
              className="max-w-full max-h-[80vh]"
            />
          </div>
        </div>
      )}
    </>
  );
};

export default UI_SwimmerVideoLink;
