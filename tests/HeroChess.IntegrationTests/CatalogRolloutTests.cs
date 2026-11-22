using HeroChess.Api.Auth;
using HeroChess.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed partial class UserMigrationTests
{
    [Fact]
    public async Task Fresh_installer_has_the_same_43_heroes_14_starters_and_four_piece_sets()
    {
        await WithLegacyDatabase(async (connection, _) =>
        {
            await Execute(connection, "DROP SCHEMA hero_chess CASCADE; DROP SCHEMA identity CASCADE");
            await SqlFile(connection, "00_install_fresh_database.sql");
            await AssertCatalogue(connection);
            Assert.Equal(4L, await Scalar(connection, "SELECT count(*) FROM hero_chess.piece_cosmetic"));
            Assert.Equal(43L, await Scalar(connection, "SELECT count(*) FROM hero_chess.hero WHERE is_enabled AND asset_key IS NOT NULL"));
        });
    }

    [Fact]
    public async Task Catalogue_rollout_replaces_retired_heroes_without_resetting_accounts_or_history()
    {
        await WithLegacyDatabase(async (connection, _) =>
        {
            await PrepareCatalogue(connection);
            await Execute(connection, """
                BEGIN;
                INSERT INTO hero_chess.user_account(id,display_name,role,is_guest) VALUES('aaaaaaaa-0000-4000-8000-000000000001','Legacy player','player',false);
                INSERT INTO hero_chess.player_wallet(player_id,balance) VALUES('aaaaaaaa-0000-4000-8000-000000000001',777);
                INSERT INTO hero_chess.hero(code,class_code,name,setup_points)
                VALUES ('dinh-liet-rook','ROOK','Old rook',7),('luu-nhan-chu-horse','HORSE','Old horse',4),
                 ('tran-khat-chan-cannon','CANNON','Old cannon',6),('nguyen-trung-ngan-advisor','ADVISOR','Old advisor',1);
                INSERT INTO hero_chess.player_hero(player_id,hero_id,acquired_via)
                SELECT 'aaaaaaaa-0000-4000-8000-000000000001',id,'starter' FROM hero_chess.hero;
                INSERT INTO hero_chess.user_account(id,display_name,role,is_guest)
                VALUES('aaaaaaaa-0000-4000-8000-000000000003','Owner without lineup','player',false);
                INSERT INTO hero_chess.player_hero(player_id,hero_id,acquired_via)
                SELECT 'aaaaaaaa-0000-4000-8000-000000000003',id,'starter' FROM hero_chess.hero WHERE code='dinh-liet-rook';
                INSERT INTO hero_chess.hero_faction(hero_id,faction_id)
                SELECT h.id,f.id FROM (VALUES ('dinh-liet-rook','hau-le'),('luu-nhan-chu-horse','hau-le'),
                 ('tran-khat-chan-cannon','tran'),('nguyen-trung-ngan-advisor','tran')) r(hero_code,faction_code)
                JOIN hero_chess.hero h ON h.code=r.hero_code JOIN hero_chess.faction f ON f.code=r.faction_code;
                INSERT INTO hero_chess.lineup(id,player_id,ruleset_id,name)
                SELECT 'bbbbbbbb-0000-4000-8000-000000000001','aaaaaaaa-0000-4000-8000-000000000001',id,'Old lineup' FROM hero_chess.ruleset;
                INSERT INTO hero_chess.lineup_entry(lineup_id,slot_no,class_code,hero_id)
                SELECT 'bbbbbbbb-0000-4000-8000-000000000001',s.slot_no,s.class_code,h.id
                FROM hero_chess.lineup_slot s JOIN hero_chess.hero h ON h.code=CASE s.slot_no
                 WHEN 1 THEN 'tran-khanh-du-rook' WHEN 2 THEN 'tran-quoc-toan-horse' WHEN 3 THEN 'le-khoi-elephant'
                 WHEN 4 THEN 'nguyen-trung-ngan-advisor' WHEN 5 THEN 'tran-thanh-tong-general' WHEN 6 THEN 'nguyen-trai-1-advisor'
                 WHEN 7 THEN 'le-van-hung-elephant' WHEN 8 THEN 'luu-nhan-chu-horse' WHEN 9 THEN 'dinh-liet-rook'
                 WHEN 10 THEN 'nguyen-xi-cannon' WHEN 11 THEN 'tran-khat-chan-cannon' ELSE 'linh-nha-tran-soldier' END;
                CREATE TABLE hero_chess.starter_hero_config(hero_id uuid PRIMARY KEY REFERENCES hero_chess.hero(id),position int NOT NULL,updated_at timestamptz DEFAULT now());
                INSERT INTO hero_chess.starter_hero_config SELECT id,row_number() OVER() FROM hero_chess.hero;
                INSERT INTO hero_chess.game_match(id,mode,status,ruleset_id,ruleset_snapshot,content_version,result,end_reason,ended_at)
                SELECT 'cccccccc-0000-4000-8000-000000000001','bot','completed',id,'{}','legacy','red_win','resign',now() FROM hero_chess.ruleset;
                INSERT INTO hero_chess.match_participant(match_id,side,player_id,participant_type,lineup_snapshot)
                SELECT 'cccccccc-0000-4000-8000-000000000001','red','aaaaaaaa-0000-4000-8000-000000000001','human',
                 jsonb_build_object('pieces',jsonb_agg(jsonb_build_object('heroId',id,'heroName',name)))
                FROM hero_chess.hero WHERE code IN ('dinh-liet-rook','luu-nhan-chu-horse','tran-khat-chan-cannon','nguyen-trung-ngan-advisor');
                INSERT INTO hero_chess.match_participant(match_id,side,participant_type,bot_config,lineup_snapshot)
                VALUES('cccccccc-0000-4000-8000-000000000001','black','bot','{}','{}');
                COMMIT;
                """);
            var before = await Scalar(connection, "SELECT count(*) FROM hero_chess.player_hero WHERE player_id='aaaaaaaa-0000-4000-8000-000000000001'");
            var history = await Scalar(connection, "SELECT lineup_snapshot::text FROM hero_chess.match_participant WHERE participant_type='human'");
            await SqlFile(connection, "20_hero_catalog_and_starters.sql");
            await SqlFile(connection, "20_hero_catalog_and_starters.sql");
            await AssertCatalogue(connection);
            Assert.Equal(777L, await Scalar(connection, "SELECT balance FROM hero_chess.player_wallet"));
            Assert.Equal((long)before! - 4, await Scalar(connection, "SELECT count(*) FROM hero_chess.player_hero WHERE player_id='aaaaaaaa-0000-4000-8000-000000000001'"));
            Assert.Equal(2L, await Scalar(connection, "SELECT count(*) FROM hero_chess.player_hero p JOIN hero_chess.hero h ON h.id=p.hero_id WHERE player_id='aaaaaaaa-0000-4000-8000-000000000003' AND h.class_code='ROOK'"));
            Assert.Equal(14L, await Scalar(connection, "SELECT count(*) FROM hero_chess.starter_hero_config"));
            Assert.Equal(2, await Scalar(connection, "SELECT revision FROM hero_chess.lineup"));
            Assert.Equal(16L, await Scalar(connection, "SELECT count(*) FROM hero_chess.lineup_entry"));
            Assert.Equal(45L, await Scalar(connection, "SELECT sum(h.setup_points) FROM hero_chess.lineup_entry e JOIN hero_chess.hero h ON h.id=e.hero_id"));
            Assert.Equal(history, await Scalar(connection, "SELECT lineup_snapshot::text FROM hero_chess.match_participant WHERE participant_type='human'"));
            await Execute(connection, "UPDATE hero_chess.hero SET coin_price=555,is_starter=false,is_enabled=false WHERE code='le-sat-horse'");
            await SqlFile(connection, "02_seed_catalog.sql");
            Assert.Equal(true, await Scalar(connection, "SELECT coin_price=555 AND NOT is_starter AND NOT is_enabled FROM hero_chess.hero WHERE code='le-sat-horse'"));
            Assert.Equal(0L, await Scalar(connection, "SELECT count(*) FROM hero_chess.hero WHERE code IN ('dinh-liet-rook','luu-nhan-chu-horse','tran-khat-chan-cannon','nguyen-trung-ngan-advisor')"));
        });
    }

    [Fact]
    public async Task Catalogue_rollout_rejects_financial_references_and_rolls_back()
    {
        await WithLegacyDatabase(async (connection, _) =>
        {
            await PrepareCatalogue(connection);
            await Execute(connection, """
                INSERT INTO hero_chess.user_account(id,display_name,role,is_guest) VALUES('aaaaaaaa-0000-4000-8000-000000000002','Buyer','player',false);
                INSERT INTO hero_chess.player_wallet(player_id,balance) VALUES('aaaaaaaa-0000-4000-8000-000000000002',900);
                INSERT INTO hero_chess.hero(code,class_code,name,setup_points) VALUES('dinh-liet-rook','ROOK','Old rook',7);
                INSERT INTO hero_chess.coin_transaction(player_id,idempotency_key,kind,amount,balance_after,hero_id)
                SELECT 'aaaaaaaa-0000-4000-8000-000000000002','purchase-before-rollout','hero_purchase',-100,900,id FROM hero_chess.hero WHERE code='dinh-liet-rook';
                """);
            await Assert.ThrowsAsync<PostgresException>(() => SqlFile(connection, "20_hero_catalog_and_starters.sql"));
            Assert.Equal(1L, await Scalar(connection, "SELECT count(*) FROM hero_chess.hero WHERE code='dinh-liet-rook'"));
            Assert.Equal(900L, await Scalar(connection, "SELECT balance FROM hero_chess.player_wallet"));
        });
    }

    private static async Task PrepareCatalogue(NpgsqlConnection connection)
    {
        foreach (var file in new[] { "05_app_extensions.sql", "06_user_inheritance.sql", "02_seed_catalog.sql", "13_admin_module.sql", "13_hero_factions.sql" })
            await SqlFile(connection, file);
    }

    private static Task<int> Execute(NpgsqlConnection connection, string sql) => new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync();
    private static Task<object?> Scalar(NpgsqlConnection connection, string sql) => new NpgsqlCommand(sql, connection).ExecuteScalarAsync();

    private static async Task AssertCatalogue(NpgsqlConnection connection)
    {
        Assert.Equal(43L, await Scalar(connection, "SELECT count(*) FROM hero_chess.hero WHERE is_enabled"));
        Assert.Equal(14L, await Scalar(connection, "SELECT count(*) FROM hero_chess.hero WHERE is_enabled AND is_starter"));
        Assert.Equal(29L, await Scalar(connection, "SELECT count(*) FROM hero_chess.hero WHERE is_enabled AND is_in_shop AND coin_price>0"));
        Assert.Equal(true, await Scalar(connection, "SELECT NOT is_starter AND is_in_shop AND asset_key='nguyenvanloc' AND coin_price=100 AND setup_points=6 FROM hero_chess.hero WHERE code='nguyen-van-loc-cannon'"));
        Assert.Equal(true, await Scalar(connection, "SELECT bool_and(n=4) FROM (SELECT count(*) AS n FROM hero_chess.hero h JOIN hero_chess.hero_faction f ON f.hero_id=h.id WHERE h.is_starter AND h.class_code<>'SOLDIER' GROUP BY f.faction_id) counts"));
    }
}

public sealed class ReleaseOnboardingTests
{
    [Fact]
    public async Task Production_onboarding_grants_1000_coins_and_only_the_14_starter_heroes()
    {
        await using var baseFactory = new HeroChessFactory();
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = $"release-{Guid.NewGuid():N}@hero.local";
        var user = new Player { Id = Guid.NewGuid(), Email = email, UserName = email };
        Assert.True((await manager.CreateAsync(user, $"A1!{Guid.NewGuid():N}")).Succeeded);
        Assert.Equal(1000, (await db.Wallets.SingleAsync(x => x.PlayerId == user.Id)).Balance);
        var grant = await db.CoinTransactions.SingleAsync(x => x.PlayerId == user.Id && x.Kind == "starter");
        Assert.Equal(1000, grant.Amount);
        Assert.Equal(1000, grant.BalanceAfter);
        var owned = await db.PlayerHeroes.Where(x => x.PlayerId == user.Id).Join(db.Heroes, x => x.HeroId, h => h.Id, (_, h) => h).ToArrayAsync();
        Assert.Equal(14, owned.Length);
        Assert.All(owned, h => Assert.True(h.IsStarter && h.IsEnabled));
        Assert.All(owned, h => Assert.False(h.IsTestFixture));
    }

}
