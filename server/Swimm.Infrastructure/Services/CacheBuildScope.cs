using System.Collections.Concurrent;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Контекст сборки записи кэша (docs/plans/cache-tags-plan.md, К3): пока строится значение,
/// сюда стекаются его зависимости — таблицы, которых касались SQL-запросы
/// (<see cref="Data.CacheDependencyInterceptor"/>), и метки вложенных записей кэша.
///
/// Метку нигде не пишут руками: страница клуба получает <c>table:Records</c> потому, что стена
/// рекордов читает <c>Records</c>, — автор страницы про это не думает и забыть не может.
///
/// Токен метки снимается В МОМЕНТ КАСАНИЯ — до того, как запрос прочитал данные. Сбросили
/// таблицу позже, пока ответ ещё строился, — снятый токен уже отменён, и собранное из старых
/// данных в кэш не ляжет. (Redis сделает то же версией метки на момент касания.)
///
/// Живёт в <see cref="AsyncLocal{T}"/>: контекст течёт в await-цепочку сборки и не вытекает
/// наружу — асинхронный метод возвращает вызывающему его прежний контекст.
/// </summary>
public sealed class CacheBuildScope : IDisposable
{
    private static readonly AsyncLocal<CacheBuildScope?> CurrentScope = new();

    /// <summary>Сборка, внутри которой идёт выполнение; null — вне сборки записи кэша.</summary>
    public static CacheBuildScope? Current => CurrentScope.Value;

    private readonly CacheBuildScope? _parent;
    private readonly Func<string, CancellationToken> _captureToken;
    private readonly ConcurrentDictionary<string, CancellationToken> _tokens = new();

    private CacheBuildScope(CacheBuildScope? parent, Func<string, CancellationToken> captureToken)
    {
        _parent = parent;
        _captureToken = captureToken;
    }

    /// <summary>Открыть сборку внутри текущего контекста. Закрывается <see cref="Dispose"/>.</summary>
    public static CacheBuildScope Begin(Func<string, CancellationToken> captureToken)
    {
        var scope = new CacheBuildScope(CurrentScope.Value, captureToken);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>Отметить зависимость. Повторное касание сохраняет ПЕРВЫЙ снятый токен — самый ранний.</summary>
    public void Touch(string tag) => _tokens.GetOrAdd(tag, _captureToken);

    /// <summary>
    /// Унаследовать зависимости записи, из которой собирается этот ответ (попадание во вложенный
    /// кэш: SQL не выполнялся, и без этого внешний ответ не узнал бы, из чего собран внутренний).
    /// Берутся токены САМОЙ записи, а не текущие: запись валидна ровно с ними.
    /// </summary>
    public void Inherit(IReadOnlyDictionary<string, CancellationToken> tokens)
    {
        foreach (var (tag, token) in tokens) _tokens.TryAdd(tag, token);
    }

    /// <summary>Зависимости, собранные на этот момент: метка → токен на момент касания.</summary>
    public IReadOnlyDictionary<string, CancellationToken> Tokens => _tokens;

    public void Dispose() => CurrentScope.Value = _parent;
}
