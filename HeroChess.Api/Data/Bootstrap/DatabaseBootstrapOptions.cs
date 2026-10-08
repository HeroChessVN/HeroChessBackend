// Vai trò file: Cờ bật bootstrap SQL và seed fixture; mặc định không tự sửa database.
// IsProductionDatabase: nếu true, dev fixtures bị từ chối ngay cả khi environment=Development.
// Dùng để ngăn chặn accident chạy dev seed trên production connection trong môi trường Development.
namespace HeroChess.Api.Data.Bootstrap;

public sealed class DatabaseBootstrapOptions
{
    public bool Enabled { get; set; }
    public bool SeedDevelopmentFixtures { get; set; }
    /// <summary>
    /// True nếu connection đang trỏ tới production database.
    /// Nếu true, dev fixtures bị từ chối kể cả khi SeedDevelopmentFixtures=true.
    /// </summary>
    public bool IsProductionDatabase { get; set; }
}
