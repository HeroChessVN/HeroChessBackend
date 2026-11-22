using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class UserMigrationTests
{
    [Fact]
    public async Task Timer_rollout_rejects_catalogue_drift_without_partial_balance_changes()
    {
        await WithLegacyDatabase(async (connection, _) =>
        {
            await PrepareCatalogue(connection);
            await SqlFile(connection, "08_step6_skills.sql");
            await SqlFile(connection, "09_hero_movement_fixes.sql");
            await SqlFile(connection, "16_faction_skills.sql");
            await Execute(connection, "UPDATE hero_chess.team_skill SET cooldown_turns=99 WHERE code='rao'");
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => SqlFile(connection, "23_skill_timers.sql"));
            Assert.Equal(true, await Scalar(connection, "SELECT cooldown_turns=14 FROM hero_chess.team_skill WHERE code='thanh'"));
            Assert.Equal(true, await Scalar(connection, "SELECT NOT config ? 'skillTimingVersion' FROM hero_chess.ruleset WHERE is_active"));
        });
    }

    [Fact]
    public async Task Timer_rollout_and_restart_preserve_later_tuning_and_unrelated_catalogue_data()
    {
        await WithLegacyDatabase(async (connection, _) =>
        {
            await PrepareCatalogue(connection);
            await SqlFile(connection, "08_step6_skills.sql");
            await SqlFile(connection, "09_hero_movement_fixes.sql");
            await SqlFile(connection, "16_faction_skills.sql");
            await Execute(connection, """
                UPDATE hero_chess.hero_trait SET name='Custom name',parameters=parameters || '{"customKey":123}'::jsonb
                WHERE implementation_key='quang_trung.special_move';
                UPDATE hero_chess.team_skill SET eligibility=eligibility || '{"customRule":true}'::jsonb WHERE code='hich_xuat_quan';
                """);
            await SqlFile(connection, "23_skill_timers.sql");
            Assert.Equal(true, await Scalar(connection, "SELECT bool_and(config->>'skillTimingVersion'='2') FROM hero_chess.ruleset WHERE is_active"));
            Assert.Equal(true, await Scalar(connection, "SELECT name='Custom name' AND parameters->>'cooldownTurns'='7' AND parameters->>'customKey'='123' FROM hero_chess.hero_trait WHERE implementation_key='quang_trung.special_move'"));
            Assert.Equal(true, await Scalar(connection, "SELECT cooldown_turns=11 AND parameters->>'durationTurns'='6' AND eligibility->>'customRule'='true' FROM hero_chess.team_skill WHERE code='hich_xuat_quan'"));
            await Execute(connection, """
                UPDATE hero_chess.hero_trait SET parameters=jsonb_set(parameters,'{cooldownTurns}','9') WHERE implementation_key='quang_trung.special_move';
                UPDATE hero_chess.team_skill SET cooldown_turns=17,parameters=jsonb_set(parameters,'{durationTurns}','8') WHERE code='hich_xuat_quan';
                """);
            foreach (var file in new[] { "23_skill_timers.sql", "02_seed_catalog.sql", "08_step6_skills.sql", "09_hero_movement_fixes.sql" })
                await SqlFile(connection, file);
            Assert.Equal(true, await Scalar(connection, "SELECT name='Custom name' AND parameters->>'cooldownTurns'='9' FROM hero_chess.hero_trait WHERE implementation_key='quang_trung.special_move'"));
            Assert.Equal(true, await Scalar(connection, "SELECT cooldown_turns=17 AND parameters->>'durationTurns'='8' FROM hero_chess.team_skill WHERE code='hich_xuat_quan'"));
        });
    }
}
