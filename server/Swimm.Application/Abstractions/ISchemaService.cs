namespace Swimm.Application.Abstractions;

public interface ISchemaService
{
    Task<object> GetSchemaAsync(bool forceRefresh = false);
}
