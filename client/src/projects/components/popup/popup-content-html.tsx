import React from 'react';
import { useAppSelector } from '../../../store/store';

const PopupContentHtml: React.FC = () => {
  const popUpType = useAppSelector((state) => state.popUpType);

   return (
    <div>
      <h2 className="text-xl font-bold mb-4">HTML Content</h2>
      <div dangerouslySetInnerHTML={{ __html: '<p>HTML content from the store or API</p>' }} />
    </div>
  );
};

export default PopupContentHtml;