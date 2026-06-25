namespace Swimm.Application.Abstractions;

/// <summary>
/// Хеширование и проверка паролей. Реализация — Argon2id.
/// Идентификатор алгоритма (<see cref="Algorithm"/>) пишется в UserLocalCredential.PasswordAlgorithm,
/// чтобы в будущем можно было мигрировать на другие параметры/алгоритм без сброса паролей.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Идентификатор текущего алгоритма/параметров (напр. "argon2id-v1").</summary>
    string Algorithm { get; }

    /// <summary>Возвращает строку-хеш (содержит соль и параметры) для хранения.</summary>
    string Hash(string password);

    /// <summary>Проверяет пароль против сохранённого хеша.</summary>
    bool Verify(string password, string hash);
}
