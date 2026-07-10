import React, { useState, useEffect } from 'react';
import { GalleryItem } from '../../../../utils/interfaces/results';
import { HelperMedia } from '../../../../utils/helpers';

interface UI_SwimmerGalleryProps {
  gallery?: GalleryItem[];
  className?: string;
  popupSize?: 'sm' | 'md' | 'lg' | 'xl' | 'full';
  /**
   * Контролируемый режим (сетки галерей): триггер-иконка не рендерится,
   * попап открыт, пока openIndex != null. Родитель держит ОДИН экземпляр
   * лайтбокса на всю сетку и открывает его по клику на тайл.
   */
  openIndex?: number | null;
  onClose?: () => void;
}

const getEmbedUrl = (item: GalleryItem): string | null => {
  if (item.sourceType === 'youtube') {
    const id = item.code ?? HelperMedia.extractYoutubeId(item.url);
    if (id) return `https://www.youtube.com/embed/${id}`;
  }
  if (item.sourceType === 'vimeo') {
    const id = item.code ?? HelperMedia.extractVimeoId(item.url);
    if (id) return `https://player.vimeo.com/video/${id}`;
  }
  return null;
};

const GalleryItemView: React.FC<{ item: GalleryItem }> = ({ item }) => {
  if (item.type === 'image') {
    return (
      <img
        src={item.url}
        alt=""
        className="max-w-full max-h-[80vh] object-contain"
      />
    );
  }

  // video
  const embedUrl = getEmbedUrl(item);
  if (embedUrl) {
    return (
      <iframe
        src={embedUrl}
        title="Video"
        className="w-full aspect-video"
        allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
        allowFullScreen
      />
    );
  }

  // direct video file (sourceType === 'other' or no sourceType)
  if (item.url) {
    return (
      <video
        src={item.url}
        controls
        autoPlay
        className="max-w-full max-h-[80vh]"
      />
    );
  }

  return null;
};

const popupSizeClass: Record<string, string> = {
  sm: 'max-w-lg max-h-[60vh]',
  md: 'max-w-2xl max-h-[75vh]',
  lg: 'max-w-4xl max-h-[85vh]',
  xl: 'max-w-6xl max-h-[90vh]',
  full: 'max-w-[95vw] max-h-[95vh]',
};

const UI_SwimmerGallery: React.FC<UI_SwimmerGalleryProps> = ({
  gallery,
  className = '',
  popupSize = 'lg',
  openIndex,
  onClose,
}) => {
  const controlled = openIndex !== undefined;
  const [showPopup, setShowPopup] = useState(false);
  const [currentIndex, setCurrentIndex] = useState(0);

  // Контролируемый режим: открытие/индекс диктует родитель через openIndex.
  useEffect(() => {
    if (!controlled) return;
    if (openIndex != null) {
      setCurrentIndex(openIndex);
      setShowPopup(true);
    } else {
      setShowPopup(false);
    }
  }, [controlled, openIndex]);

  if (!gallery || gallery.length === 0) {
    return null;
  }

  const handleClick = () => {
    setCurrentIndex(0);
    setShowPopup(true);
  };

  const handleClose = () => {
    setShowPopup(false);
    onClose?.();
  };

  const handlePrev = () => {
    setCurrentIndex((i) => (i > 0 ? i - 1 : gallery.length - 1));
  };

  const handleNext = () => {
    setCurrentIndex((i) => (i < gallery.length - 1 ? i + 1 : 0));
  };

  const hasMultiple = gallery.length > 1;
  const hasVideo = gallery.some((item) => item.type === 'video');

  const videoIcon = (
    <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <rect x="3" y="6" width="13" height="12" rx="2" stroke="currentColor" strokeWidth="2"/>
      <path d="M16 10.5l5-3v9l-5-3v-3z" fill="currentColor"/>
    </svg>
  );

  const imageIcon = (
    <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <rect x="3" y="4" width="18" height="16" rx="2" stroke="currentColor" strokeWidth="2"/>
      <circle cx="8.5" cy="10" r="1.5" fill="currentColor"/>
      <path d="M3 16l4-4 3 3 4-5 7 6" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/>
    </svg>
  );

  return (
    <>
      {!controlled && (
        <div
          className={`cursor-pointer inline-flex items-center ${className}`}
          onClick={handleClick}
        >
          {hasVideo ? videoIcon : imageIcon}
        </div>
      )}

      {showPopup && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-70"
          onClick={handleClose}
        >
          <div
            className={`relative bg-white rounded-lg p-4 ${popupSizeClass[popupSize]} w-full flex flex-col items-center`}
            onClick={(e) => e.stopPropagation()}
          >
            <button
              className="absolute top-2 right-2 text-gray-500 hover:text-gray-800 text-2xl font-bold z-10"
              onClick={handleClose}
            >
              ×
            </button>

            <div className="w-full flex items-center justify-center min-h-[200px]">
              {hasMultiple && (
                <button
                  className="text-3xl text-gray-500 hover:text-gray-800 px-3"
                  onClick={handlePrev}
                >
                  ‹
                </button>
              )}

              <div className="flex-1 flex items-center justify-center">
                <GalleryItemView item={gallery[currentIndex]} />
              </div>

              {hasMultiple && (
                <button
                  className="text-3xl text-gray-500 hover:text-gray-800 px-3"
                  onClick={handleNext}
                >
                  ›
                </button>
              )}
            </div>

            {hasMultiple && (
              <div className="mt-2 text-sm text-gray-500">
                {currentIndex + 1} / {gallery.length}
              </div>
            )}
          </div>
        </div>
      )}
    </>
  );
};

export default UI_SwimmerGallery;
