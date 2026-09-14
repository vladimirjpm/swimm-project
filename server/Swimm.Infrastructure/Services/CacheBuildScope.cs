using System.Collections.Concurrent;
using Swimm.Application.Constants;

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
///
/// Сужение (docs/plans/cache-row-precision-plan.md §2.3, К4б.3): внутри блока
/// <see cref="Narrow"/> чтение перечисленных таблиц даёт не <c>table:T</c>, а метки строк
/// корня и <c>anyrow:T</c> — страница одной группы зависит от своих строк, а не от всей таблицы.
/// </summary>
public sealed class CacheBuildScope : IDisposable
{
    /// <summary>
    /// Потолок сужения (§2.3): больше id в одном блоке — блок работает как таблица. Сотня меток
    /// у страницы группы нормальна; тысяча — уже не выгода, а нагрузка (в Redis — MGET на каждое
    /// попадание).
    /// </summary>
    public const int MaxNarrowedIds = 200;

    private static readonly AsyncLocal<CacheBuildScope?> CurrentScope = new();

    // Открытый блок сужения. Своя AsyncLocal, а не поле сборки: блок течёт только в ту
    // await-цепочку, которая его открыла, и не задевает параллельную ветку той же сборки.
    private static readonly AsyncLocal<RowNarrowing?> CurrentNarrowing = new();

    /// <summary>Сборка, внутри которой идёт выполнение; null — вне сборки записи кэша.</summary>
    public static CacheBuildScope? Current => CurrentScope.Value;

    private readonly CacheBuildScope? _parent;
    private readonly Func<string, CancellationToken> _captureToken;
    private readonly ConcurrentDictionary<string, CancellationToken> _tokens = new();

    private CacheBuildScope(CacheBuildScope? parent, Func<string, CancellationToken> captureToken,
        bool rowPrecision, bool isVerification)
    {
        _parent = parent;
        _captureToken = captureToken;
        RowPrecision = rowPrecision;
        IsVerification = isVerification;
    }

    /// <summary>
    /// Сужение до строк включено (выключатель <c>CacheRowPrecision</c> на момент начала сборки).
    /// Выключено — блоки <see cref="Narrow"/> ничего не делают, запись зависит от таблиц.
    /// </summary>
    public bool RowPrecision { get; }

    /// <summary>
    /// Сборка-сверка (§4-6): ответ строится заново мимо кэша, вложенные записи тоже, а метки
    /// никуда не идут — сверке нужен только сам ответ.
    /// </summary>
    public bool IsVerification { get; }

    /// <summary>Открыть сборку внутри текущего контекста. Закрывается <see cref="Dispose"/>.</summary>
    public static CacheBuildScope Begin(Func<string, CancellationToken> captureToken, bool rowPrecision = false)
    {
        var scope = new CacheBuildScope(CurrentScope.Value, captureToken, rowPrecision, isVerification: false);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>Открыть сборку-сверку: её касания токенов не снимают, её метки выбрасываются.</summary>
    public static CacheBuildScope BeginVerification()
    {
        var scope = new CacheBuildScope(CurrentScope.Value, static _ => CancellationToken.None,
            rowPrecision: false, isVerification: true);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>Отметить зависимость. Повторное касание сохраняет ПЕРВЫЙ снятый токен — самый ранний.</summary>
    public void Touch(string tag) => _tokens.GetOrAdd(tag, _captureToken);

    /// <summary>
    /// Запрос прочёл таблицу: <c>table:T</c>, а если таблица сужена открытым в ЭТОЙ сборке
    /// блоком — метки строк корня и <c>anyrow:T</c>. Плюс <c>col:T.C</c> на каждую служебную
    /// колонку <paramref name="serviceColumns"/>, которую запрос назвал (К4б.6, §2.6).
    ///
    /// ⚠ Блок чужой сборки не действует: вложенная запись (скажем, «все стили»), собранная внутри
    /// блока группы 24, легла бы в кэш с меткой группы 24 и потом врала бы всем. Поэтому
    /// вложенная сборка начинает с чистого сужения, а её метки наружу наследуются как раньше.
    ///
    /// Служебная правка не сбрасывает ни <c>table:T</c>, ни метки корней по FK — только <c>col:</c>
    /// и, если таблица — корень, метку своей строки. Поэтому <c>col:</c> носит всякий читатель
    /// колонки, и суженный потомок тоже (участников группы правка их пловца по <c>row:</c> группы
    /// не найдёт), — кроме корня в его же блоке: его строки читаются по id, и служебную правку
    /// такой строки закрывает её <c>row:</c>. С <c>col:</c> страница группы, которая читает свою
    /// строку целиком (с <c>UpdatedAt</c>), падала бы от отметки «обновлено» ЛЮБОЙ группы.
    /// </summary>
    public void TouchTable(string table, IReadOnlyCollection<string>? serviceColumns = null)
    {
        if (CurrentNarrowing.Value is { } narrowing
            && ReferenceEquals(narrowing.Scope, this)
            && narrowing.Tables.Contains(table))
        {
            Touch(CacheTags.AnyRow(table));
            narrowing.TouchRows();
            if (table == narrowing.RootTable) return;
        }
        else
        {
            Touch(CacheTags.Table(table));
        }

        if (serviceColumns is null) return;
        foreach (var column in serviceColumns) Touch(CacheTags.Column(table, column));
    }

    /// <summary>
    /// Блок сужения: пока открыт, чтение таблиц <paramref name="tables"/> в этой сборке зависит
    /// от строк <paramref name="ids"/> корня <paramref name="rootTable"/>, а не от таблиц. Зовёт
    /// только <c>CacheRows</c> (<see cref="Data.CacheRowsExtensions"/>) — он проверил по модели,
    /// что у таблиц есть FK на корень.
    ///
    /// Выключатель выключен или id больше <see cref="MaxNarrowedIds"/> — блок пустой, запись
    /// зависит от таблиц. Вложенный блок на своё время заменяет внешний, на выходе внешний
    /// возвращается: чтение, которое внутренний не сужает, зависит от таблицы — это лишний сброс,
    /// не недосброс.
    /// </summary>
    public IDisposable Narrow(string rootTable, IReadOnlyCollection<long> ids, IReadOnlySet<string> tables)
    {
        if (!RowPrecision || ids.Count > MaxNarrowedIds) return NoBlock.Instance;

        var outer = CurrentNarrowing.Value;
        CurrentNarrowing.Value = new RowNarrowing(this, rootTable, ids, tables);
        return new Block(outer);
    }

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

    /// <summary>Открытый блок сужения: чья сборка, какой корень, какие строки и таблицы.</summary>
    private sealed class RowNarrowing(
        CacheBuildScope scope, string rootTable, IReadOnlyCollection<long> ids, IReadOnlySet<string> tables)
    {
        private int _rowsTouched;

        public CacheBuildScope Scope => scope;
        public string RootTable => rootTable;
        public IReadOnlySet<string> Tables => tables;

        /// <summary>
        /// Метки строк корня — один раз на блок: токен снимается при первом касании, до чтения
        /// данных, как у таблиц; повторные касания его всё равно не меняют.
        /// </summary>
        public void TouchRows()
        {
            if (Interlocked.Exchange(ref _rowsTouched, 1) == 1) return;
            foreach (var id in ids) scope.Touch(CacheTags.Row(rootTable, id));
        }
    }

    private sealed class Block(RowNarrowing? outer) : IDisposable
    {
        public void Dispose() => CurrentNarrowing.Value = outer;
    }

    private sealed class NoBlock : IDisposable
    {
        public static readonly NoBlock Instance = new();
        public void Dispose() { }
    }
}
