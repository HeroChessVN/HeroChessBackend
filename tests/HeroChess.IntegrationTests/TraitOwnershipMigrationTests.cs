using HeroChess.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace HeroChess.IntegrationTests;

// Dùng helper DB cô lập ở UserMigrationTests; kiểm tra nâng cấp catalog cũ đang chia sẻ trait.
public sealed partial class UserMigrationTests
{
    [Fact]
    public async Task Shared_traits_are_split_without_changing_behavior_and_cannot_be_reused_by_another_hero()
    {
        await WithLegacyDatabase(async (connection, connectionString) =>
        {
            await SqlFile(connection, "02_seed_catalog.sql");
            // Tái tạo catalog cũ: Bùi Thị Xuân và Trần Bình Trọng cùng một trait.
            await using (var legacy = new NpgsqlCommand("""
                UPDATE hero_chess.hero SET trait_id='30000000-0000-4000-8000-000000000001'
                WHERE code='bui-thi-xuan-elephant';
                DELETE FROM hero_chess.hero_trait WHERE id='327e712d-26d2-5da4-5c9a-87a1ff74c1e1';
                """, connection)) await legacy.ExecuteNonQueryAsync();
            await SqlFile(connection, "07_exclusive_hero_traits.sql");
            await SqlFile(connection, "07_exclusive_hero_traits.sql");
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
            var heroes = await db.Heroes.Include(h => h.Trait)
                .Where(h => h.Code == "tran-binh-trong-elephant" || h.Code == "bui-thi-xuan-elephant")
                .OrderBy(h => h.Code).ToArrayAsync();
            Assert.Equal(2, heroes.Length);
            Assert.NotEqual(heroes[0].TraitId, heroes[1].TraitId);
            var tranTraitId = heroes.Single(h => h.Code == "tran-binh-trong-elephant").TraitId;
            Assert.Equal(Guid.Parse("30000000-0000-4000-8000-000000000001"), tranTraitId);
            Assert.Equal(heroes[0].Trait!.Kind, heroes[1].Trait!.Kind);
            Assert.Equal(heroes[0].Trait!.ImplementationKey, heroes[1].Trait!.ImplementationKey);
            Assert.Equal(heroes[0].Trait!.Parameters.RootElement.GetRawText(), heroes[1].Trait!.Parameters.RootElement.GetRawText());
            Assert.Equal(heroes[0].Trait!.Description, heroes[1].Trait!.Description);

            // SQL constraint thực sự từ chối chia sẻ; không chỉ dựa vào EF navigation fix-up.
            await using var duplicate = new NpgsqlCommand("UPDATE hero_chess.hero SET trait_id='30000000-0000-4000-8000-000000000001' WHERE code='bui-thi-xuan-elephant'", connection);
            var error = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
            Assert.Equal("23505", error.SqlState); Assert.Equal("uq_hero_trait_id", error.ConstraintName);
            await using var rename = new NpgsqlCommand($"UPDATE hero_chess.hero_trait SET name='Tên riêng của Bùi Thị Xuân' WHERE id='{heroes.Single(h => h.Code == "bui-thi-xuan-elephant").TraitId}'", connection);
            await rename.ExecuteNonQueryAsync();
            Assert.Equal("Trần Bình Trọng đi chéo 1–2 ô", await db.HeroTraits.Where(t => t.Id == tranTraitId).Select(t => t.Name).SingleAsync());
            await using var noTrait = new NpgsqlCommand("UPDATE hero_chess.hero SET trait_id=NULL WHERE code IN ('tran-binh-trong-elephant','bui-thi-xuan-elephant')", connection);
            Assert.Equal(2, await noTrait.ExecuteNonQueryAsync());
        });
    }
}
