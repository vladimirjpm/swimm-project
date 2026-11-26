/**
 * Стенд механики печати workout (Ш2, docs/plans/workout-print-plan.md): кнопка открывает
 * упрощённое окно предпросмотра с листом-заглушкой, `Print` печатает только лист.
 *
 * Это не окно Ш4, а его минимальный макет — ровно столько, чтобы проверить контракт
 * `workout-print.css` (`.wprint-noprint`, `.wprint-stage`, `.wprint-sheet`): окно fixed, лист
 * уменьшен `transform: scale`, вокруг — затемнение и кнопки. Ш3 заменит заглушку видом `large`,
 * Ш4 — этот макет настоящим окном; тогда запись на витрине поменять или убрать.
 */
import React, { useState } from 'react';
import { createPortal } from 'react-dom';
import { usePrintSession } from '../../projects/hub-groups-project/components/trainings/workout/print/use-print-session';
import { PrintSheetStub } from '../../projects/hub-groups-project/components/trainings/workout/print/print-sheet-stub';

const btn = 'rounded-md border border-white/40 px-3 py-1.5 text-sm font-semibold text-white hover:bg-white/10';

const StubPreview: React.FC<{ onClose: () => void }> = ({ onClose }) => {
  const { root, print } = usePrintSession('portrait');
  if (!root) return null;
  return createPortal(
    <div className="fixed inset-0 z-[1000] flex flex-col items-center overflow-auto bg-[rgba(2,10,30,0.88)] p-4">
      <div className="wprint-noprint mb-3 flex items-center gap-3 text-white">
        <span className="font-bold">Print preview</span>
        <button type="button" className={btn} onClick={print} data-testid="wprint-print">
          Print
        </button>
        <button type="button" className={btn} onClick={onClose} data-testid="wprint-close">
          Close
        </button>
      </div>
      {/* Масштаб 0.6: лист 794 px → 476 px; обёртка держит место под уменьшенный лист. */}
      <div style={{ width: '126mm', height: '178.2mm' }}>
        <div className="wprint-stage origin-top-left shadow-2xl" style={{ transform: 'scale(0.6)' }}>
          <PrintSheetStub />
        </div>
      </div>
    </div>,
    root
  );
};

const PrintStubPreview: React.FC = () => {
  const [open, setOpen] = useState(false);
  return (
    <div className="flex flex-col gap-2">
      <button
        type="button"
        className="self-start rounded-md bg-sky-600 px-3 py-1.5 text-sm font-semibold text-white"
        onClick={() => setOpen(true)}
        data-testid="wprint-open"
      >
        Open print preview
      </button>
      <p className="text-xs opacity-70">
        Ctrl+P при закрытом окне — обычная печать страницы; при открытом — только лист.
      </p>
      {open && <StubPreview onClose={() => setOpen(false)} />}
    </div>
  );
};

export default PrintStubPreview;
