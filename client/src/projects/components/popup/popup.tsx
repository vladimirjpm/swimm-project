import React from 'react';
import { rootActions, useAppDispatch, useAppSelector } from '../../../store/store';
import { Enums } from '../../../utils/interfaces/enums';
import PopupContentNormative from './popup-content-normative';
import PopupContentHtml from './popup-content-html';

const Popup: React.FC = () => {
  const dispatch = useAppDispatch();
  const isPopup = useAppSelector((state) => state.isPopup);
  const popUpType = useAppSelector((state) => state.popUpType as Enums.PopupType);

  // Мапа: тип попапа → компонент
  const contentByType: Record<Enums.PopupType, React.ReactNode> = {
    [Enums.PopupType.none]: null,
    [Enums.PopupType.normative]: <PopupContentNormative />,
    [Enums.PopupType.html]: <PopupContentHtml />,
  };

  const close = React.useCallback(() => {
    dispatch(rootActions.updateState({ isPopup: false, popUpType: Enums.PopupType.none }));
  }, [dispatch]);

  // Закрытие по Esc
  React.useEffect(() => {
    if (!isPopup) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') close();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [isPopup, close]);

  if (!isPopup) return null;

  return (
    <div
      className="fixed inset-0 bg-black/50 flex justify-center items-center z-50"
      onClick={close} // закрытие по фону
    >
      <div
        className="bg-white rounded-2xl p-6 w-[min(92vw,1200px)] shadow-lg relative"
        onClick={(e) => e.stopPropagation()} // не закрывать при клике внутри
      >
        <button
          onClick={close}
          className="absolute top-3 right-3 text-gray-500 hover:text-black"
          aria-label="Close"
        >
          ✕
        </button>

        {/* Контент по типу */}
        {contentByType[popUpType] ?? null}
      </div>
    </div>
  );
};

export default Popup;