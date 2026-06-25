using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Swimm.Application.Abstractions;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Argon2id-хеширование паролей (OWASP-параметры: m=19 MiB, t=2, p=1).
/// Формат строки самоописывающий — соль и параметры хранятся вместе с хешем,
/// поэтому Verify не зависит от внешней конфигурации и параметры можно менять без сброса паролей:
///   argon2id$v=1$m=&lt;KiB&gt;,t=&lt;iter&gt;,p=&lt;par&gt;$&lt;saltB64&gt;$&lt;hashB64&gt;
/// </summary>
public class Argon2PasswordHasher : IPasswordHasher
{
    private const int MemoryKiB = 19456; // 19 MiB
    private const int Iterations = 2;
    private const int Parallelism = 1;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Algorithm => "argon2id-v1";

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Compute(password, salt, MemoryKiB, Iterations, Parallelism, HashSize);

        return $"argon2id$v=1$m={MemoryKiB},t={Iterations},p={Parallelism}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        try
        {
            // argon2id $ v=1 $ m=..,t=..,p=.. $ salt $ hash
            var parts = hash.Split('$');
            if (parts.Length != 5 || parts[0] != "argon2id")
                return false;

            var paramMap = parts[2].Split(',')
                .Select(kv => kv.Split('='))
                .ToDictionary(kv => kv[0], kv => int.Parse(kv[1]));

            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);

            var actual = Compute(password, salt, paramMap["m"], paramMap["t"], paramMap["p"], expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Compute(string password, byte[] salt, int memoryKiB, int iterations, int parallelism, int size)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };
        return argon2.GetBytes(size);
    }
}
