import React, { useState } from 'react';

interface Props {
  trainingId?: number;
  thumbClass?: string;
}

const TrainingSetHeaderImages: React.FC<Props> = ({ trainingId, thumbClass }) => {
  const [showT, setShowT] = useState(true);
  const [showR, setShowR] = useState(true);
  const [modalSrc, setModalSrc] = useState<string | null>(null);

  if (!trainingId) return null;

  const base = import.meta.env.BASE_URL || '/';

  const tSrc = `${base}images/training/dolphin-masters/${trainingId}-t.jpg`;
  const rSrc = `${base}images/training/dolphin-masters/${trainingId}-r.jpg`;

  return (
    <>
      <div className="flex items-center gap-2">
        {showT && (
          <button
            type="button"
            onClick={() => setModalSrc(tSrc)}
            className="focus:outline-none"
            title="Open training image"
          >
            <img
              src={tSrc}
              alt={`training-${trainingId}-t`}
              className={thumbClass ?? 'w-16 h-16 md:w-20 md:h-20 object-cover rounded'}
              onError={() => setShowT(false)}
            />
          </button>
        )}

        {showR && (
          <button
            type="button"
            onClick={() => setModalSrc(rSrc)}
            className="focus:outline-none"
            title="Open results image"
          >
            <img
              src={rSrc}
              alt={`training-${trainingId}-r`}
              className={thumbClass ?? 'w-16 h-16 md:w-20 md:h-20 object-cover rounded'}
              onError={() => setShowR(false)}
            />
          </button>
        )}
      </div>

      {modalSrc && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div className="absolute inset-0 bg-black/60" onClick={() => setModalSrc(null)} />
          <div className="relative max-w-[90%] max-h-[90%]">
            <button
              type="button"
              className="absolute top-2 right-2 z-20 bg-white rounded-full p-1 shadow"
              onClick={() => setModalSrc(null)}
              aria-label="Close image"
            >
              ✕
            </button>
            <img src={modalSrc} alt="full-size" className="max-w-full max-h-[80vh] object-contain rounded" />
          </div>
        </div>
      )}
    </>
  );
};

export default TrainingSetHeaderImages;
