// ЗАГЛУШКА листа для проверки механики печати (Ш2). Её заменит вид `large` (Ш3,
// `views/large-view.tsx`) — тогда файл удалить вместе с записью на витрине ui-kit.
//
// Рамка по внутреннему отступу 10 мм (38 px, §3.2) и метки в углах — чтобы в PDF было видно,
// что лист лёг ровно на страницу: ничего не срезано и колонтитулов нет.

import React from 'react';

const corner = 'absolute text-[11px] font-bold';

export const PrintSheetStub: React.FC<{ title?: string }> = ({ title = 'Print sheet stub' }) => (
  <div className="wprint-sheet" dir="ltr">
    <div className="absolute inset-[38px] flex flex-col items-center justify-center gap-6 border-2 border-black rounded-[6px] text-center">
      <div className="text-[48px] font-extrabold leading-tight">{title}</div>
      <div className="text-[28px] font-bold" dir="rtl">
        דולפין נתניה מאסטרס
      </div>
      <div className="font-mono text-[24px]">A4 · 210×297 mm · margin 10 mm</div>
    </div>
    <span className={`${corner} left-[4px] top-[4px]`}>TL</span>
    <span className={`${corner} right-[4px] top-[4px]`}>TR</span>
    <span className={`${corner} bottom-[4px] left-[4px]`}>BL</span>
    <span className={`${corner} bottom-[4px] right-[4px]`}>BR</span>
  </div>
);
