import './flag-icon.css';
import React from 'react';

type FlagSize =
  | '16x12' | '20x15' | '24x18' | '28x21' | '32x24' | '36x27'
  | '40x30' | '48x36' | '56x42' | '60x45' | '64x48' | '72x54'
  | '80x60' | '84x63' | '96x72' | '108x81' | '112x84' | '120x90'
  | '128x96' | '144x108' | '160x120' | '192x144' | '224x168' | '256x192';

type FlagEmojiProps = {
  countryCode: string;
  className?: string;
  title?: string;
  size?: FlagSize;
};

const FlagEmoji: React.FC<FlagEmojiProps> = ({
  countryCode,
  className = '',
  title,
  size = '24x18',
}) => {
  if (!countryCode || countryCode.length !== 2) return null;

  const code = countryCode.toLowerCase();
  const [width, height] = size.split('x').map(Number);
  const src = `https://flagcdn.com/${size}/${code}.png`;

  return (
    <img
      src={src}
      alt={`Flag of ${code.toUpperCase()}`}
      title={title || code.toUpperCase()}
      className={className}
      width={width}
      height={height}
      style={{ objectFit: 'cover', borderRadius: '2px' }}
    />
  );
};

export default FlagEmoji;
