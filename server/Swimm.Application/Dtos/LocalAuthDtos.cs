namespace Swimm.Application.Dtos;

public enum RegisterStatus { Ok, WeakPassword, InvalidEmail }

public enum LoginStatus { Ok, InvalidCredentials, LockedOut, EmailNotConfirmed }

public enum ResetStatus { Ok, InvalidOrExpiredToken, WeakPassword }

/// <summary>Результат регистрации. Намеренно НЕ раскрывает, существовал ли уже email
/// (anti-enumeration): контроллер всегда отвечает одинаково при Ok.</summary>
public class RegisterResult
{
    public RegisterStatus Status { get; init; }
    public static RegisterResult Success() => new() { Status = RegisterStatus.Ok };
    public static RegisterResult Fail(RegisterStatus s) => new() { Status = s };
}

/// <summary>Результат входа. На Ok содержит UserId для выпуска cookie контроллером.</summary>
public class LoginResult
{
    public LoginStatus Status { get; init; }
    public int UserId { get; init; }
    public DateTime? LockoutEnd { get; init; }

    public static LoginResult Success(int userId) => new() { Status = LoginStatus.Ok, UserId = userId };
    public static LoginResult Locked(DateTime until) => new() { Status = LoginStatus.LockedOut, LockoutEnd = until };
    public static LoginResult Fail(LoginStatus s) => new() { Status = s };
}

public class ResetResult
{
    public ResetStatus Status { get; init; }
    public int UserId { get; init; }
    public static ResetResult Success(int userId) => new() { Status = ResetStatus.Ok, UserId = userId };
    public static ResetResult Fail(ResetStatus s) => new() { Status = s };
}
