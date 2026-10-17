namespace Swimm.Application.Abstractions;

/// <summary>
/// Сидер тестовых персонажей (docs/plans/test-personas-plan.md, этап 2): utest-аккаунты
/// (<c>TestPersonas.All</c>), две тестовые группы (IsTest) и связи между ними — участие, заявка,
/// админ группы, избранное, медиа с публикациями, одна тренировка.
/// Только Development. Запуск: <c>dotnet run -- --seed-personas [--reset]</c>.
/// </summary>
public interface IPersonaSeeder
{
    /// <summary>
    /// Идемпотентно: повтор ничего не задваивает и не трогает уже созданное (состав групп,
    /// выбранные «дети»). <paramref name="reset"/> — сначала удалить всех utest-пользователей и
    /// тестовые группы, которыми они владеют, и засидить заново. Возвращает лог.
    /// </summary>
    Task<IReadOnlyList<string>> SeedAsync(bool reset = false);

    /// <summary>
    /// Вернуть ОДНОГО персонажа в исходное состояние (кнопка «Сбросить» в /Admin/Users, просьба
    /// Влада 17.10.2026): стереть всё, что на нём накопилось руками, и засидить его заново — id
    /// аккаунта не меняется. null — это не персонаж из <c>TestPersonas.All</c> (сбрасывать нечего).
    /// Только Development.
    /// </summary>
    Task<IReadOnlyList<string>?> ResetPersonaAsync(int userId);

    /// <summary>
    /// Чем каждый персонаж отличается от исходного состояния (то, что выставил бы сидер):
    /// id аккаунта → список отличий человеческими словами; пустой список — не менялся. Для
    /// подсветки кнопки «Сбросить» в /Admin/Users (17.10.2026). Отметки «видел группу» и журнал
    /// входов изменением не считаются — они появляются от простого просмотра.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetPersonaChangesAsync();
}
