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
                WHERE id='40000000-0000-4000-8000-000000000002';
                DELETE FROM hero_chess.hero_trait WHERE id='327e712d-26d2-5da4-5c9a-87a1ff74c1e1';
                """, connection)) await legacy.ExecuteNonQueryAsync();
            await SqlFile(connection, "07_exclusive_hero_traits.sql");
            await SqlFile(connection, "07_exclusive_hero_traits.sql");
            await SqlFile(connection, "02_seed_catalog.sql"); // Seed lặp không gắn lại trait chung hoặc tạo bản thừa.
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
            var heroes = await db.Heroes.Include(h => h.Trait).OrderBy(h => h.Id).ToArrayAsync();
            Assert.Equal(3, heroes.Length);
            Assert.Equal(Guid.Parse("30000000-0000-4000-8000-000000000001"), heroes[0].TraitId);
            Assert.Equal(Guid.Parse("327e712d-26d2-5da4-5c9a-87a1ff74c1e1"), heroes[1].TraitId);
            Assert.Equal(3, await db.HeroTraits.CountAsync());
            Assert.Equal(heroes[0].Trait!.Kind, heroes[1].Trait!.Kind);
            Assert.Equal(heroes[0].Trait!.ImplementationKey, heroes[1].Trait!.ImplementationKey);
            Assert.Equal(heroes[0].Trait!.Parameters.RootElement.GetRawText(), heroes[1].Trait!.Parameters.RootElement.GetRawText());
            Assert.Equal(heroes[0].Trait!.Description, heroes[1].Trait!.Description);

            // SQL constraint thực sự từ chối chia sẻ; không chỉ dựa vào EF navigation fix-up.
            await using var duplicate = new NpgsqlCommand("UPDATE hero_chess.hero SET trait_id='30000000-0000-4000-8000-000000000001' WHERE id='40000000-0000-4000-8000-000000000002'", connection);
            var error = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
            Assert.Equal("23505", error.SqlState); Assert.Equal("uq_hero_trait_id", error.ConstraintName);
            await using var rename = new NpgsqlCommand("UPDATE hero_chess.hero_trait SET name='Tên riêng của Bùi Thị Xuân' WHERE id='327e712d-26d2-5da4-5c9a-87a1ff74c1e1'", connection);
            await rename.ExecuteNonQueryAsync();
            Assert.Equal("Tượng đi chéo 1–2 bước", await db.HeroTraits.Where(t => t.Id == heroes[0].TraitId).Select(t => t.Name).SingleAsync());
            await using var noTrait = new NpgsqlCommand("UPDATE hero_chess.hero SET trait_id=NULL", connection);
            Assert.Equal(3, await noTrait.ExecuteNonQueryAsync()); // Nhiều hero chưa có trait vẫn hợp lệ.
        });
    }
}
