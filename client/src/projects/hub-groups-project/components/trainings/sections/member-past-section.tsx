import React from 'react';
import { useTrainingsTab } from '../trainings-context';
import PastSessions from '../past/past-sessions';

/**
 * Список «Past» УЧАСТНИКА в виде Next (решение Влада 26.11.2026): прошедшие занятия с опубликованным
 * планом за 90 дней, без счётчиков ответов; Open — `member-past-session`. Телефон — под карточкой
 * ближайшего на боковой линии сегментов, десктоп — в левой колонке под Next. Пусто — секции нет.
 */
function MemberPastSection() {
  const { isDesktop } = useTrainingsTab();
  return <PastSessions className={isDesktop ? '' : 'deep-panel-row'} />;
}

export default MemberPastSection;
