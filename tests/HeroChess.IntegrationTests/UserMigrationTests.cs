using HeroChess.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace HeroChess.IntegrationTests;

// Mỗi test tạo database tên ngẫu nhiên trên server test; không chạy migration vào DB người dùng.
public sealed partial class UserMigrationTests
{
    [Fact]
    public async Task Upgrade_preserves_credentials_ids_audit_and_legacy_game_data_and_can_run_twice()
    {
        await WithLegacyDatabase(async (connection, connectionString) =>
        {
            var player = Guid.NewGuid(); var admin = Guid.NewGuid();
            var hasher = new PasswordHasher<User>(); var hash = hasher.HashPassword(new Player(), "Example-password-123!");
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO hero_chess.player(id,display_name,is_guest,role) VALUES (@p,'Player',false,'player'),(@a,'Admin',false,'admin');
                INSERT INTO identity.app_user(id,email,normalized_email,user_name,normalized_user_name,password_hash,security_stamp)
                    VALUES (@p,'p@example.test','P@EXAMPLE.TEST','p@example.test','P@EXAMPLE.TEST',@hash,'player-stamp'),
                           (@a,'a@example.test','A@EXAMPLE.TEST','a@example.test','A@EXAMPLE.TEST',@hash,'admin-stamp');
                INSERT INTO hero_chess.player_wallet(player_id,balance) VALUES (@p,123),(@a,456);
                INSERT INTO hero_chess.player_rating(player_id,elo) VALUES (@p,1000),(@a,1100);
                INSERT INTO hero_chess.auth_identity(player_id,provider,subject) VALUES (@a,'aspnet_identity',@a::text);
                INSERT INTO hero_chess.admin_audit_log(actor_player_id,action,entity_type,entity_id,reason)
                    VALUES (@a,'price.update','hero','legacy-id','preserve me');
                """, connection))
            {
                seed.Parameters.AddWithValue("p", player); seed.Parameters.AddWithValue("a", admin); seed.Parameters.AddWithValue("hash", hash);
                await seed.ExecuteNonQueryAsync();
            }
            await SqlFile(connection, "06_user_inheritance.sql");
            await SqlFile(connection, "06_user_inheritance.sql");
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
            var migrated = Assert.IsType<Admin>(await db.Users.SingleAsync(x => x.Id == admin));
            Assert.Equal("admin-stamp", migrated.SecurityStamp); Assert.Equal(hash, migrated.PasswordHash);
            Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(migrated, migrated.PasswordHash!, "Example-password-123!"));
            Assert.IsType<Player>(await db.Users.SingleAsync(x => x.Id == player));
            Assert.False(await db.Players.AnyAsync(x => x.Id == admin));
            Assert.Equal(456, (await db.Wallets.SingleAsync(x => x.PlayerId == admin)).Balance);
            Assert.Equal(123, (await db.Wallets.SingleAsync(x => x.PlayerId == player)).Balance);
            Assert.Equal(admin, (await db.AdminAuditLogs.SingleAsync()).ActorUserId);
            Assert.Equal(admin, (await db.AuthIdentities.SingleAsync()).UserId);
            await using var missingOld = new NpgsqlCommand("SELECT to_regclass('hero_chess.player') IS NULL AND to_regclass('identity.app_user') IS NULL", connection);
            Assert.Equal(true, await missingOld.ExecuteScalarAsync());

            // FK vẫn giữ lịch sử, nhưng không được thêm lineup mới của admin bằng SQL.
            await using var invalid = new NpgsqlCommand("INSERT INTO hero_chess.lineup(player_id,ruleset_id,name) VALUES (@a,gen_random_uuid(),'bad')", connection);
            invalid.Parameters.AddWithValue("a", admin);
            var error = await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync());
            Assert.Equal("23514", error.SqlState);
        });
    }

    [Fact]
    public async Task Orphan_credentials_abort_upgrade_without_losing_original_tables_or_data()
    {
        await WithLegacyDatabase(async (connection, _) =>
        {
            await using var seed = new NpgsqlCommand("INSERT INTO identity.app_user(id,email,security_stamp) VALUES (gen_random_uuid(),'orphan@example.test','keep')", connection);
            await seed.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<PostgresException>(() => SqlFile(connection, "06_user_inheritance.sql"));
            await using var rollback = new NpgsqlCommand("ROLLBACK", connection); await rollback.ExecuteNonQueryAsync();
            await using var check = new NpgsqlCommand("SELECT to_regclass('hero_chess.player') IS NOT NULL AND to_regclass('hero_chess.user_account') IS NULL AND (SELECT count(*) FROM identity.app_user)=1", connection);
            Assert.Equal(true, await check.ExecuteScalarAsync());
        });
    }

    // Chỉ tạo/xóa database có tên do test tự sinh; Pooling=false để cleanup không giữ connection.
    private static async Task WithLegacyDatabase(Func<NpgsqlConnection, string, Task> test)
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("HERO_CHESS_TEST_DB")
            ?? throw new InvalidOperationException("Set HERO_CHESS_TEST_DB to a disposable test PostgreSQL server.")) { Pooling = false };
        var name = "hc_user_migration_" + Guid.NewGuid().ToString("N"); builder.Database = "postgres";
        await using var server = new NpgsqlConnection(builder.ConnectionString); await server.OpenAsync();
        await using var create = new NpgsqlCommand("CREATE DATABASE " + name, server); await create.ExecuteNonQueryAsync();
        try
        {
            builder.Database = name;
            await using var connection = new NpgsqlConnection(builder.ConnectionString); await connection.OpenAsync();
            await SqlFile(connection, "01_schema.sql"); await SqlFile(connection, "04_identity_schema.sql");
            await test(connection, builder.ConnectionString);
        }
        finally
        {
            await using var drop = new NpgsqlCommand("DROP DATABASE " + name, server); await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task SqlFile(NpgsqlConnection connection, string name)
    {
        await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Database", name)), connection);
        await command.ExecuteNonQueryAsync();
    }
}
