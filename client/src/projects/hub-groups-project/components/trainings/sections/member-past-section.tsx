import React from 'react';
import { useTrainingsTab } from '../trainings-context';
import PastSessions from '../past/past-sessions';

/**
 * Список «Past» УЧАСТНИКА — вид Past (решение Влада 26.11.2026, свой вид с 27.11): прошедшие занятия с опубликованным
 * планом за 90 дней, без счётчиков ответов; Open — `member-past-session`.
 * Телефон — на боковой линии сегментов, десктоп — левая колонка. Весь список сразу, без заголовка «Past»: его
 * роль играет сегмент. Пусто — секции нет (и сегмента тоже, `pastHidden`).
 */
function MemberPastSection() {
  const { isDesktop } = useTrainingsTab();
  return <PastSessions className={isDesktop ? '' : 'deep-panel-row'} showAll heading={false} />;
}

export default MemberPastSection;
