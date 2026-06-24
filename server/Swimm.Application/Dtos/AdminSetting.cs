namespace Swimm.Application.Dtos;

public record AdminSetting(
    string Key,
    string Value,
    string DataType,
    string Scope,
    string Description
);
