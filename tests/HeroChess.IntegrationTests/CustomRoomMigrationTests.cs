using HeroChess.Api.Data;
using HeroChess.Api.Data.Bootstrap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class UserMigrationTests
{
    [Fact]
    public async Task Room_migration_targets_hero_chess_even_with_public_search_path()
    {
        await WithLegacyDatabase(async (connection, _) =>
        {
            await SqlFile(connection, "06_user_inheritance.sql");
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Database", "12_custom_rooms.sql"));
            sql = sql.Replace("SET LOCAL search_path = hero_chess, pg_catalog;", "SET LOCAL search_path = public, pg_catalog;");
            await using var migration = new NpgsqlCommand(sql, connection);
            await migration.ExecuteNonQueryAsync();
            await migration.ExecuteNonQueryAsync();
            await using var check = new NpgsqlCommand("""
                SELECT to_regclass('hero_chess.custom_room') IS NOT NULL
                    AND to_regclass('hero_chess.custom_room_member') IS NOT NULL
                    AND to_regclass('hero_chess.custom_room_rematch') IS NOT NULL
                    AND to_regclass('public.custom_room') IS NULL
                """, connection);
            Assert.Equal(true, await check.ExecuteScalarAsync());
        });
    }

    [Fact]
    public async Task Bootstrap_upgrades_a_legacy_schema_with_extra_tables_and_detects_missing_base_tables()
    {
        await WithLegacyDatabase(async (connection, connectionString) =>
        {
            var host = Host.CreateApplicationBuilder();
            host.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
            await using var services = host.Services.BuildServiceProvider();
            var bootstrap = new DatabaseBootstrapHostedService(services, host.Environment,
                Options.Create(new DatabaseBootstrapOptions { Enabled = true }), NullLogger<DatabaseBootstrapHostedService>.Instance);

            await using var extras = new NpgsqlCommand("""
                CREATE TABLE hero_chess.deployment_metadata(id int);
                CREATE TABLE hero_chess.deployment_audit(id int);
                """, connection);
            await extras.ExecuteNonQueryAsync();
            await bootstrap.StartAsync(default);
            await bootstrap.StartAsync(default);

            await using var check = new NpgsqlCommand("""
                SELECT to_regclass('hero_chess.user_account') IS NOT NULL
                    AND to_regclass('hero_chess.custom_room') IS NOT NULL
                    AND to_regclass('hero_chess.custom_room_member') IS NOT NULL
                    AND to_regclass('hero_chess.custom_room_rematch') IS NOT NULL
                """, connection);
            Assert.Equal(true, await check.ExecuteScalarAsync());

            await using var rename = new NpgsqlCommand("ALTER TABLE hero_chess.admin_audit_log RENAME TO unexpected_table", connection);
            await rename.ExecuteNonQueryAsync();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrap.StartAsync(default));
            Assert.Contains("admin_audit_log", error.Message);
        });
    }

    [Fact]
    public async Task Custom_roster_requires_two_distinct_humans_even_while_selecting()
    {
        await WithLegacyDatabase(async (connection, _) =>
        {
            await SqlFile(connection, "06_user_inheritance.sql");
            await SqlFile(connection, "12_custom_rooms.sql");
            await SqlFile(connection, "06_user_inheritance.sql");

            var rulesetId = Guid.NewGuid();
            var playerId = Guid.NewGuid();
            var guestId = Guid.NewGuid();
            await Execute("""
                INSERT INTO hero_chess.ruleset (id, code, name) VALUES (@ruleset, 'roster-test', 'Roster test');
                INSERT INTO hero_chess.user_account (id, display_name, is_guest)
                VALUES (@player, 'Player', false), (@guest, 'Guest', true);
                """);

            var matchId = Guid.NewGuid();
            const string match = """
                INSERT INTO hero_chess.game_match (id, mode, ruleset_id, ruleset_snapshot, content_version)
                VALUES (@match, 'custom', @ruleset, '{}', 'test');
                """;
            const string red = """
                INSERT INTO hero_chess.match_participant (match_id, side, player_id, participant_type, lineup_snapshot)
                VALUES (@match, 'red', @player, 'human', '{}');
                """;
            const string bot = """
                INSERT INTO hero_chess.match_participant (match_id, side, participant_type, bot_config, lineup_snapshot)
                VALUES (@match, 'black', 'bot', '{}', '{}');
                """;
            const string black = """
                INSERT INTO hero_chess.match_participant (match_id, side, player_id, participant_type, lineup_snapshot)
                VALUES (@match, 'black', @guest, 'human', '{}');
                """;

            var rosterError = await Assert.ThrowsAsync<PostgresException>(() => Execute(match + red + bot, matchId));
            Assert.Equal(PostgresErrorCodes.CheckViolation, rosterError.SqlState);
            await Execute(match + red + black, matchId);
            rosterError = await Assert.ThrowsAsync<PostgresException>(() => Execute(
                "DELETE FROM hero_chess.match_participant WHERE match_id = @match AND side = 'black'", matchId));
            Assert.Equal(PostgresErrorCodes.CheckViolation, rosterError.SqlState);

            async Task Execute(string sql, Guid? id = null)
            {
                await using var transaction = await connection.BeginTransactionAsync();
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("ruleset", rulesetId);
                command.Parameters.AddWithValue("player", playerId);
                command.Parameters.AddWithValue("guest", guestId);
                if (id is Guid value) command.Parameters.AddWithValue("match", value);
                await command.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
        });
    }
}
