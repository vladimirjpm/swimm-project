namespace Swimm.API.Security;

/// <summary>Имена схем аутентификации.</summary>
public static class AuthSchemes
{
    /// <summary>
    /// Транзитная cookie-схема для OAuth-рукопожатия (между Challenge и callback).
    /// Отделена от основной схемы намеренно: её claims (от Google) не содержат нашего
    /// SecurityStamp, а NameIdentifier там — это provider-key, а не наш int-Id.
    /// Если бы Google писал в основную схему, OnValidatePrincipal отверг бы этот
    /// промежуточный принципал → петля логина.
    /// </summary>
    public const string External = "External";
}
