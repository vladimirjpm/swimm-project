namespace Swimm.Application.Dtos;

/// <summary>Фильтры списка Records (Admin/Records). Все поля опциональны — null = без фильтра.</summary>
public sealed record RecordFilter(
    string? RegionType,
    string? RegionCode,
    string? Category,
    string? Gender,
    string? PoolType,
    string? Style);

/// <summary>Фильтры списка NormativeStandards (Admin/Records, вкладка Standards).</summary>
public sealed record StandardFilter(
    string? Kind,
    string? Gender,
    string? PoolType,
    string? Style);

/// <summary>Полный набор полей для создания рекорда (все три оси + значение).</summary>
public sealed class RecordInputDto
{
    public string RegionType { get; set; } = "";
    public string RegionCode { get; set; } = "";
    public string Category { get; set; } = "";
    public string AgeKey { get; set; } = "";
    public string Gender { get; set; } = "";
    public string PoolType { get; set; } = "";
    public string Style { get; set; } = "";
    public string Distance { get; set; } = "";
    public string Time { get; set; } = "";
    public string? HolderName { get; set; }
    public string? Club { get; set; }
    public string? HolderCountry { get; set; }
    public string? RecordDate { get; set; }
}

/// <summary>Инлайн-редактирование существующего рекорда — оси (позиция в дереве) не меняются.</summary>
public sealed class RecordQuickEditDto
{
    public string Time { get; set; } = "";
    public string? HolderName { get; set; }
    public string? Club { get; set; }
    public string? HolderCountry { get; set; }
    public string? RecordDate { get; set; }
}

/// <summary>Полный набор полей для создания норматива.</summary>
public sealed class NormativeStandardInputDto
{
    public string Kind { get; set; } = "";
    public string Country { get; set; } = "";
    public string Gender { get; set; } = "";
    public string PoolType { get; set; } = "";
    public string Style { get; set; } = "";
    public string Distance { get; set; } = "";
    public string AgeKey { get; set; } = "";
    public string Level { get; set; } = "";
    public string Time { get; set; } = "";
}

/// <summary>Инлайн-редактирование норматива — оси не меняются, только время порога.</summary>
public sealed class StandardQuickEditDto
{
    public string Time { get; set; } = "";
}

/// <summary>Результат мутации Records/NormativeStandards: успех + Id + ошибка валидации для UI.</summary>
public sealed record RecordSaveResult(bool Success, int Id, string? Error)
{
    public static RecordSaveResult Ok(int id) => new(true, id, null);
    public static RecordSaveResult Fail(string error) => new(false, 0, error);
}
