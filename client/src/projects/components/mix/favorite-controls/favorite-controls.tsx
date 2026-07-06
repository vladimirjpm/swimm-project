import React from 'react';

interface UI_FavoriteControlsProps {
  swimmerId?: number | null;
  isFavorite?: boolean;
  isPrimaryFavorite?: boolean;
  onToggleFavorite?: (swimmerId: number) => void;
  onTogglePrimary?: (swimmerId: number) => void;
  /** Показывать звезду "это я" (в таблице — только сердечко; звезда живёт в попапе). */
  showPrimary?: boolean;
  className?: string;
}

/**
 * Иконки избранного: сердечко (favorite) + золотая звезда (primary = "это я").
 * SVG под дизайн-прототип. Красное сердце #e23b5a / контур #c2c8d2; звезда gold
 * #f5b800 / #d99a00. Звезда показывается только когда пловец уже в избранном.
 */
const UI_FavoriteControls: React.FC<UI_FavoriteControlsProps> = ({
  swimmerId,
  isFavorite = false,
  isPrimaryFavorite = false,
  onToggleFavorite,
  onTogglePrimary,
  showPrimary = true,
  className = '',
}) => {
  if (!onToggleFavorite || swimmerId == null) return null;

  const stop = (e: React.MouseEvent) => e.stopPropagation();

  // "Звезда отменяет сердечко": у primary (me) вместо сердечка — золотая звезда
  if (isPrimaryFavorite) {
    return (
      <div className={`flex flex-col items-center gap-1 shrink-0 ${className}`}>
        <button
          type="button"
          title="Primary favorite (this is me)"
          onClick={(e) => { stop(e); onTogglePrimary?.(swimmerId); }}
          className="leading-none hover:scale-110 transition-transform"
        >
          <svg width="17" height="17" viewBox="0 0 24 24" fill="#f5b800" stroke="#d99a00" strokeWidth="1.8" strokeLinejoin="round">
            <path d="M12 2.5l2.9 5.9 6.5.95-4.7 4.6 1.1 6.45L12 21.3l-5.8 3.05 1.1-6.45-4.7-4.6 6.5-.95z" />
          </svg>
        </button>
      </div>
    );
  }

  return (
    <div className={`flex flex-col items-center gap-1 shrink-0 ${className}`}>
      <button
        type="button"
        title={isFavorite ? 'Remove from favorites' : 'Add to favorites'}
        onClick={(e) => { stop(e); onToggleFavorite(swimmerId); }}
        className="leading-none hover:scale-110 transition-transform"
      >
        <svg width="17" height="17" viewBox="0 0 24 24" fill={isFavorite ? '#e23b5a' : 'none'} stroke={isFavorite ? '#e23b5a' : '#c2c8d2'} strokeWidth="2">
          <path d="M12 21s-7.5-4.6-10-9.3C.4 8.3 2 5 5.2 5c2 0 3.3 1.1 4.1 2.3C10.1 6.1 11.4 5 13.4 5 16.6 5 18.2 8.3 16.6 11.7 14.1 16.4 12 21 12 21z" />
        </svg>
      </button>

      {showPrimary && isFavorite && onTogglePrimary && (
        <button
          type="button"
          title={isPrimaryFavorite ? 'Primary favorite (this is me)' : 'Set as primary (this is me)'}
          onClick={(e) => { stop(e); onTogglePrimary(swimmerId); }}
          className="leading-none hover:scale-110 transition-transform"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill={isPrimaryFavorite ? '#f5b800' : 'none'} stroke={isPrimaryFavorite ? '#d99a00' : '#c2c8d2'} strokeWidth="1.8" strokeLinejoin="round">
            <path d="M12 2.5l2.9 5.9 6.5.95-4.7 4.6 1.1 6.45L12 21.3l-5.8 3.05 1.1-6.45-4.7-4.6 6.5-.95z" />
          </svg>
        </button>
      )}
    </div>
  );
};

export default UI_FavoriteControls;
