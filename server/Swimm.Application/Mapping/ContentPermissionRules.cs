using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>Что пользователь хочет сделать с контентом — то, на что бывает отдельное право.</summary>
public enum ContentAction
{
    /// <summary>Добавить фото или альбом (ссылку).</summary>
    AddPhoto,

    /// <summary>Добавить видео (ссылку).</summary>
    AddVideo,

    /// <summary>
    /// Показать своё медиа другим: заявка на публикацию в клуб/группу, галерея группы, ссылка к
    /// тренировке (её видят участники).
    /// </summary>
    Publish,
}

/// <summary>
/// Права на контент отдельно от входа (docs/plans/content-permissions-plan.md). Единственное
/// место, где политика по умолчанию (/Admin/Settings) и персональное исключение пользователя
/// (/Admin/Users) складываются в ответ: его зовут и проверка в эндпоинтах
/// (<c>ContentPermissionService</c>), и <c>/auth/me</c> (клиент прячет кнопки), и колонка в
/// админке. Вторая копия разбора разъехалась бы с первой.
///
/// Порядок:
///  1. site-админ — всегда да;
///  2. персональное исключение, если задано (да или нет), — важнее политики: так «новым — без
///     публикации» сочетается с «этому — можно»;
///  3. иначе — политика по умолчанию (дефолт <c>true</c>: сайт ведёт себя как до плана).
///
/// Решения Влада 18.11.2026: дефолт для новых — всё можно; на реакции (лайк, «болею») права нет;
/// снятое право на публикацию уже опубликованное не трогает.
/// </summary>
public static class ContentPermissionRules
{
    public const string DefaultCanAddPhotosKey = "ContentDefaultCanAddPhotos";
    public const string DefaultCanAddVideosKey = "ContentDefaultCanAddVideos";
    public const string DefaultCanPublishKey = "ContentDefaultCanPublish";

    /// <summary>Дефолт всех трёх политик — разрешено (поведение до плана).</summary>
    public const bool DefaultPolicy = true;

    /// <summary>Какое право нужно, чтобы добавить медиа этого типа: видео — своё, остальное (image, album) — фото.</summary>
    public static ContentAction AddActionFor(string? mediaType) =>
        string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase)
            ? ContentAction.AddVideo
            : ContentAction.AddPhoto;

    /// <summary>Решение по одному действию. Чистая функция — под юнит-тесты на каждую ветку.</summary>
    public static bool IsAllowed(bool isAdmin, bool? personalOverride, bool policyDefault) =>
        isAdmin || (personalOverride ?? policyDefault);

    /// <summary>Ключ настройки политики для действия.</summary>
    public static string PolicyKey(ContentAction action) => action switch
    {
        ContentAction.AddPhoto => DefaultCanAddPhotosKey,
        ContentAction.AddVideo => DefaultCanAddVideosKey,
        _ => DefaultCanPublishKey,
    };

    /// <summary>Действующие права пользователя.</summary>
    public static ContentPermissionsDto Evaluate(
        ISettingsService settings, bool isAdmin, bool? canAddPhotos, bool? canAddVideos, bool? canPublish) => new()
    {
        AddPhotos = IsAllowed(isAdmin, canAddPhotos, settings.GetValue(DefaultCanAddPhotosKey, DefaultPolicy)),
        AddVideos = IsAllowed(isAdmin, canAddVideos, settings.GetValue(DefaultCanAddVideosKey, DefaultPolicy)),
        Publish = IsAllowed(isAdmin, canPublish, settings.GetValue(DefaultCanPublishKey, DefaultPolicy)),
    };

    /// <summary>Разрешено ли действие при этих правах.</summary>
    public static bool Allows(ContentPermissionsDto permissions, ContentAction action) => action switch
    {
        ContentAction.AddPhoto => permissions.AddPhotos,
        ContentAction.AddVideo => permissions.AddVideos,
        _ => permissions.Publish,
    };

    /// <summary>
    /// Текст отказа (403) — идёт на витрину, поэтому по-английски (правило UI).
    /// </summary>
    public static string DeniedMessage(ContentAction action) => action switch
    {
        ContentAction.AddPhoto => "Adding photos is not available for your account.",
        ContentAction.AddVideo => "Adding videos is not available for your account.",
        _ => "Sharing media with others is not available for your account. Your own media stays private to you.",
    };
}
