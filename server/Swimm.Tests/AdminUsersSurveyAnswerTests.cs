using Swimm.API.Pages.Admin.Users;
using Swimm.Application.Dtos;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Ответ опроса «Who are you?» в панели деталей /Admin/Users (docs/plans/profile-setup-plan.md):
/// «не отвечал» и «снял все ответы» — разные состояния, различает их время ответа.
/// </summary>
public class AdminUsersSurveyAnswerTests
{
    [Fact]
    public void NeverAnswered_Null() =>
        Assert.Null(IndexModel.SurveyAnswer(new UserDetailDto { ProfileRoleSwimmer = true }));

    [Fact]
    public void AnsweredWithNothing_Empty() =>
        Assert.Equal("", IndexModel.SurveyAnswer(new UserDetailDto { ProfileRolesAnsweredAt = DateTime.UtcNow }));

    [Fact]
    public void PickedRoles_InScreenOrder_WithScreenLabels() =>
        Assert.Equal("I swim, I'm a coach", IndexModel.SurveyAnswer(new UserDetailDto
        {
            ProfileRolesAnsweredAt = DateTime.UtcNow,
            ProfileRoleCoach = true,
            ProfileRoleSwimmer = true,
        }));
}
