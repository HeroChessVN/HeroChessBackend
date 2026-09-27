// Vai trò file: Cờ bật bootstrap SQL và seed fixture; mặc định không tự sửa database.
namespace HeroChess.Api.Data.Bootstrap;

public sealed class DatabaseBootstrapOptions
{
    public bool Enabled { get; set; }
    public bool SeedDevelopmentFixtures { get; set; }
}
