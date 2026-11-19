using Npgsql;

namespace Swimm.Infrastructure.Data;

/// <summary>
/// Тёплый пул соединений (решение Влада 19.11.2026). При малом трафике Npgsql закрывает соединение,
/// простоявшее 5 минут, и следующий запрос открывает новое — TLS до Azure, на проде 18.11 это 635
/// открытий в сутки (rw 443, ro 192), обычно 120–130 мс, у медленных 5 % — до 1,2 с (rw) и 4,8 с (ro);
/// первое соединение после старта процесса — 7,7 с.
/// <list type="bullet">
/// <item><see cref="MinPoolSize"/> — столько соединений пул НЕ закрывает по простою (открываются по
///   первому требованию, дальше живут). Не больше <c>Maximum Pool Size</c> строки.</item>
/// <item><see cref="KeepAliveSeconds"/> — Npgsql пингует простаивающее соединение, чтобы сеть Azure его
///   не оборвала молча (тот же приём, что у моста кэша, <c>BridgeDataSource</c>).</item>
/// </list>
/// Заданное в строке подключения явно — не трогаем (ненулевое значение = «кто-то решил»). Лимит базы
/// B1ms — 50 соединений: два пула сайта по 2 постоянных — запас огромный.
/// </summary>
internal static class NpgsqlPoolDefaults
{
    public const int MinPoolSize = 2;
    public const int KeepAliveSeconds = 30;

    public static void Apply(NpgsqlDataSourceBuilder dataSource)
    {
        var cs = dataSource.ConnectionStringBuilder;
        if (!cs.Pooling) return;
        if (cs.MinPoolSize == 0) cs.MinPoolSize = Math.Min(MinPoolSize, cs.MaxPoolSize);
        if (cs.KeepAlive == 0) cs.KeepAlive = KeepAliveSeconds;
    }
}
