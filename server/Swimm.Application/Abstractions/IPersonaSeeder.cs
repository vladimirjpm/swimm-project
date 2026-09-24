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
}
