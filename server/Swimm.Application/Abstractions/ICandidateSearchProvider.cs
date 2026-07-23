namespace Swimm.Application.Abstractions;

/// <summary>Поиск кандидатов Loglig ID по имени пловца (сменный провайдер: serper и т.п.).</summary>
public interface ICandidateSearchProvider
{
    /// <summary>true — провайдер сконфигурирован (есть ключ); false — поиск отключён.</summary>
    bool IsConfigured { get; }

    /// <summary>До 5 уникальных loglig ID (Players/Details/{id}) из поисковой выдачи. Пустой список — ничего не нашли/поиск отключён/ошибка.</summary>
    Task<IReadOnlyList<int>> FindCandidatesAsync(string lastNameHe, string firstNameHe, CancellationToken ct = default);
}
