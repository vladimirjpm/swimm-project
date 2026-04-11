using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Swimm.API.Migrations
{
    /// <inheritdoc />
    public partial class PrefixSysInternalAuthTables2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID('dbo.AppUserRoles', 'U') IS NOT NULL AND OBJECT_ID('dbo.Sys_AppUserRoles', 'U') IS NULL
                    EXEC sp_rename 'dbo.AppUserRoles', 'Sys_AppUserRoles';

                IF OBJECT_ID('dbo.UserExternalLogins', 'U') IS NOT NULL AND OBJECT_ID('dbo.Sys_UserExternalLogins', 'U') IS NULL
                    EXEC sp_rename 'dbo.UserExternalLogins', 'Sys_UserExternalLogins';

                IF OBJECT_ID('dbo.UserLoginHistory', 'U') IS NOT NULL AND OBJECT_ID('dbo.Sys_UserLoginHistory', 'U') IS NULL
                    EXEC sp_rename 'dbo.UserLoginHistory', 'Sys_UserLoginHistory';

                IF OBJECT_ID('PK_AppUserRoles', 'PK') IS NOT NULL
                    EXEC sp_rename 'PK_AppUserRoles', 'PK_Sys_AppUserRoles', 'OBJECT';
                IF OBJECT_ID('PK_UserExternalLogins', 'PK') IS NOT NULL
                    EXEC sp_rename 'PK_UserExternalLogins', 'PK_Sys_UserExternalLogins', 'OBJECT';
                IF OBJECT_ID('PK_UserLoginHistory', 'PK') IS NOT NULL
                    EXEC sp_rename 'PK_UserLoginHistory', 'PK_Sys_UserLoginHistory', 'OBJECT';

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AppUserRoles_RoleId' AND object_id = OBJECT_ID('dbo.Sys_AppUserRoles'))
                    EXEC sp_rename 'dbo.Sys_AppUserRoles.IX_AppUserRoles_RoleId', 'IX_Sys_AppUserRoles_RoleId', 'INDEX';
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserExternalLogins_Provider_ProviderKey' AND object_id = OBJECT_ID('dbo.Sys_UserExternalLogins'))
                    EXEC sp_rename 'dbo.Sys_UserExternalLogins.IX_UserExternalLogins_Provider_ProviderKey', 'IX_Sys_UserExternalLogins_Provider_ProviderKey', 'INDEX';
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserExternalLogins_UserId' AND object_id = OBJECT_ID('dbo.Sys_UserExternalLogins'))
                    EXEC sp_rename 'dbo.Sys_UserExternalLogins.IX_UserExternalLogins_UserId', 'IX_Sys_UserExternalLogins_UserId', 'INDEX';
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserLoginHistory_LoginAt' AND object_id = OBJECT_ID('dbo.Sys_UserLoginHistory'))
                    EXEC sp_rename 'dbo.Sys_UserLoginHistory.IX_UserLoginHistory_LoginAt', 'IX_Sys_UserLoginHistory_LoginAt', 'INDEX';
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserLoginHistory_UserId' AND object_id = OBJECT_ID('dbo.Sys_UserLoginHistory'))
                    EXEC sp_rename 'dbo.Sys_UserLoginHistory.IX_UserLoginHistory_UserId', 'IX_Sys_UserLoginHistory_UserId', 'INDEX';

                IF OBJECT_ID('FK_AppUserRoles_AppRoles_RoleId', 'F') IS NOT NULL
                    EXEC sp_rename 'FK_AppUserRoles_AppRoles_RoleId', 'FK_Sys_AppUserRoles_AppRoles_RoleId', 'OBJECT';
                IF OBJECT_ID('FK_AppUserRoles_AppUsers_UserId', 'F') IS NOT NULL
                    EXEC sp_rename 'FK_AppUserRoles_AppUsers_UserId', 'FK_Sys_AppUserRoles_AppUsers_UserId', 'OBJECT';
                IF OBJECT_ID('FK_UserExternalLogins_AppUsers_UserId', 'F') IS NOT NULL
                    EXEC sp_rename 'FK_UserExternalLogins_AppUsers_UserId', 'FK_Sys_UserExternalLogins_AppUsers_UserId', 'OBJECT';
                IF OBJECT_ID('FK_UserLoginHistory_AppUsers_UserId', 'F') IS NOT NULL
                    EXEC sp_rename 'FK_UserLoginHistory_AppUsers_UserId', 'FK_Sys_UserLoginHistory_AppUsers_UserId', 'OBJECT';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID('FK_Sys_AppUserRoles_AppRoles_RoleId', 'F') IS NOT NULL
                    EXEC sp_rename 'FK_Sys_AppUserRoles_AppRoles_RoleId', 'FK_AppUserRoles_AppRoles_RoleId', 'OBJECT';
                IF OBJECT_ID('FK_Sys_AppUserRoles_AppUsers_UserId', 'F') IS NOT NULL
                    EXEC sp_rename 'FK_Sys_AppUserRoles_AppUsers_UserId', 'FK_AppUserRoles_AppUsers_UserId', 'OBJECT';
                IF OBJECT_ID('FK_Sys_UserExternalLogins_AppUsers_UserId', 'F') IS NOT NULL
                    EXEC sp_rename 'FK_Sys_UserExternalLogins_AppUsers_UserId', 'FK_UserExternalLogins_AppUsers_UserId', 'OBJECT';
                IF OBJECT_ID('FK_Sys_UserLoginHistory_AppUsers_UserId', 'F') IS NOT NULL
                    EXEC sp_rename 'FK_Sys_UserLoginHistory_AppUsers_UserId', 'FK_UserLoginHistory_AppUsers_UserId', 'OBJECT';

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Sys_AppUserRoles_RoleId' AND object_id = OBJECT_ID('dbo.Sys_AppUserRoles'))
                    EXEC sp_rename 'dbo.Sys_AppUserRoles.IX_Sys_AppUserRoles_RoleId', 'IX_AppUserRoles_RoleId', 'INDEX';
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Sys_UserExternalLogins_Provider_ProviderKey' AND object_id = OBJECT_ID('dbo.Sys_UserExternalLogins'))
                    EXEC sp_rename 'dbo.Sys_UserExternalLogins.IX_Sys_UserExternalLogins_Provider_ProviderKey', 'IX_UserExternalLogins_Provider_ProviderKey', 'INDEX';
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Sys_UserExternalLogins_UserId' AND object_id = OBJECT_ID('dbo.Sys_UserExternalLogins'))
                    EXEC sp_rename 'dbo.Sys_UserExternalLogins.IX_Sys_UserExternalLogins_UserId', 'IX_UserExternalLogins_UserId', 'INDEX';
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Sys_UserLoginHistory_LoginAt' AND object_id = OBJECT_ID('dbo.Sys_UserLoginHistory'))
                    EXEC sp_rename 'dbo.Sys_UserLoginHistory.IX_Sys_UserLoginHistory_LoginAt', 'IX_UserLoginHistory_LoginAt', 'INDEX';
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Sys_UserLoginHistory_UserId' AND object_id = OBJECT_ID('dbo.Sys_UserLoginHistory'))
                    EXEC sp_rename 'dbo.Sys_UserLoginHistory.IX_Sys_UserLoginHistory_UserId', 'IX_UserLoginHistory_UserId', 'INDEX';

                IF OBJECT_ID('PK_Sys_AppUserRoles', 'PK') IS NOT NULL
                    EXEC sp_rename 'PK_Sys_AppUserRoles', 'PK_AppUserRoles', 'OBJECT';
                IF OBJECT_ID('PK_Sys_UserExternalLogins', 'PK') IS NOT NULL
                    EXEC sp_rename 'PK_Sys_UserExternalLogins', 'PK_UserExternalLogins', 'OBJECT';
                IF OBJECT_ID('PK_Sys_UserLoginHistory', 'PK') IS NOT NULL
                    EXEC sp_rename 'PK_Sys_UserLoginHistory', 'PK_UserLoginHistory', 'OBJECT';

                IF OBJECT_ID('dbo.Sys_AppUserRoles', 'U') IS NOT NULL AND OBJECT_ID('dbo.AppUserRoles', 'U') IS NULL
                    EXEC sp_rename 'dbo.Sys_AppUserRoles', 'AppUserRoles';

                IF OBJECT_ID('dbo.Sys_UserExternalLogins', 'U') IS NOT NULL AND OBJECT_ID('dbo.UserExternalLogins', 'U') IS NULL
                    EXEC sp_rename 'dbo.Sys_UserExternalLogins', 'UserExternalLogins';

                IF OBJECT_ID('dbo.Sys_UserLoginHistory', 'U') IS NOT NULL AND OBJECT_ID('dbo.UserLoginHistory', 'U') IS NULL
                    EXEC sp_rename 'dbo.Sys_UserLoginHistory', 'UserLoginHistory';
                """);
        }
    }
}
