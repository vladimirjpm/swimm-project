namespace Swimm.Application.Dtos;

/// <summary>
/// Уровни группы и состав с уровнями — карточка «Levels» в табе Admin страницы группы
/// (docs/plans/lane-plans-plan.md, L1). Только управляющим (CanEdit); camelCase, как весь
/// <c>/api/me/hub-groups</c>.
/// </summary>
public sealed class HubGroupLevelsDto
{
    /// <summary>По рангу: первый — сильнейший.</summary>
    public List<HubGroupLevelDto> Levels { get; set; } = [];

    /// <summary>Видимый состав группы (без скрытых клубных) — у каждого уровень или null.</summary>
    public List<HubGroupLevelSwimmerDto> Swimmers { get; set; } = [];
}

public sealed class HubGroupLevelDto
{
    public int Id { get; set; }
    public int Rank { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Color { get; set; }

    /// <summary>Сколько пловцов СОСТАВА на этом уровне — для подтверждения удаления.</summary>
    public int SwimmerCount { get; set; }
}

public sealed class HubGroupLevelSwimmerDto
{
    public int SwimmerId { get; set; }

    /// <summary>Имя на иврите (правило имён: иврит по умолчанию), EN — фоллбек.</summary>
    public string Name { get; set; } = "";
    public string NameEn { get; set; } = "";
    public int BirthYear { get; set; }
    public string? Gender { get; set; }
    public string? ClubName { get; set; }
    public int? LevelId { get; set; }
}

/// <summary>Весь список уровней разом: порядок в списке = ранг. Нет в списке — удаляется.</summary>
public sealed class HubGroupLevelsInputDto
{
    public List<HubGroupLevelInputDto> Levels { get; set; } = [];
}

public sealed class HubGroupLevelInputDto
{
    /// <summary>null — новый уровень.</summary>
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
}

/// <summary>Уровень одного пловца; null — снять уровень.</summary>
public sealed class HubGroupSwimmerLevelInputDto
{
    public int? LevelId { get; set; }
}
