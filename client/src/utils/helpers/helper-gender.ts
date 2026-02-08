/**
 * Хелпер для работы с полом
 */
export type Gender = 'male' | 'female' | 'none';

export default class HelperGender {
  /**
   * Нормализует значение пола к типу Gender
   * @param gender - значение пола из данных
   * @returns 'male' | 'female' | 'none'
   */
  static resolveGender(gender: unknown): Gender {
    const value = gender === null || gender === undefined
      ? ''
      : String(gender).trim().toLowerCase();

    if (value === 'female' || value === 'f' || value === 'w') return 'female';
    if (value === 'male' || value === 'm') return 'male';
    return 'none';
  }

  /**
   * Возвращает CSS класс фона в зависимости от пола
   * @param gender - пол: 'male' | 'female' | 'none'
   * @returns Tailwind CSS класс фона
   */
  static getGenderBgClass(gender: string): string {
    if (gender === 'none') return 'bg-gray-100';
    return gender === 'female' ? 'bg-pink-100' : 'bg-blue-100';
  }
}
