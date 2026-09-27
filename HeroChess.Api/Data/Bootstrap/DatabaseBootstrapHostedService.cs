// Vai trò file: Worker startup chạy SQL theo thứ tự, dùng advisory lock để tránh hai tiến trình bootstrap đồng thời.
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HeroChess.Api.Data.Bootstrap;

public sealed class DatabaseBootstrapHostedService(IServiceProvider services, IHostEnvironment environment,
    IOptions<DatabaseBootstrapOptions> options, ILogger<DatabaseBootstrapHostedService> logger) : IHostedService
{
    // StartAsync: Khi bật cờ: kiểm tra schema, chạy identity/extensions/catalog và dev overlay; schema không đúng 25 bảng thì dừng.
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled) { logger.LogInformation("Database bootstrap is disabled."); return; }
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using (var advisoryLock = connection.CreateCommand())
        {
            advisoryLock.CommandText = "SELECT pg_advisory_lock(hashtext('hero_chess_bootstrap'))";
            await advisoryLock.ExecuteScalarAsync(cancellationToken);
        }
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT to_regnamespace('hero_chess') IS NOT NULL";
            var exists = (bool)(await check.ExecuteScalarAsync(cancellationToken) ?? false);
            if (!exists)
                await ExecuteFile(connection, Path.Combine(AppContext.BaseDirectory, "Database", "01_schema.sql"), cancellationToken);
            else
            {
                check.CommandText = "SELECT count(*) FROM information_schema.tables WHERE table_schema='hero_chess' AND table_type='BASE TABLE'";
                var tableCount = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken));
                if (tableCount != 25)
                    throw new InvalidOperationException($"Existing hero_chess schema has {tableCount} base tables; expected 25. Refusing to modify a partial or incompatible schema.");
            }
        }
        // 04 chỉ dành schema cũ/chưa có user_account; không tạo lại bảng credential đã hợp nhất.
        await using (var accountCheck = connection.CreateCommand())
        {
            accountCheck.CommandText = "SELECT to_regclass('hero_chess.user_account') IS NOT NULL";
            if (!(bool)(await accountCheck.ExecuteScalarAsync(cancellationToken) ?? false))
                await ExecuteFile(connection, Path.Combine(AppContext.BaseDirectory, "Database", "04_identity_schema.sql"), cancellationToken);
        }
        await ExecuteFile(connection, Path.Combine(AppContext.BaseDirectory, "Database", "05_app_extensions.sql"), cancellationToken);
        await ExecuteFile(connection, Path.Combine(AppContext.BaseDirectory, "Database", "06_user_inheritance.sql"), cancellationToken);
        await ExecuteFile(connection, Path.Combine(AppContext.BaseDirectory, "Database", "07_exclusive_hero_traits.sql"), cancellationToken);
        await ExecuteFile(connection, Path.Combine(AppContext.BaseDirectory, "Database", "02_seed_catalog.sql"), cancellationToken);
        if (environment.IsDevelopment() && options.Value.SeedDevelopmentFixtures)
            await ExecuteFile(connection, Path.Combine(AppContext.BaseDirectory, "Database", "03_seed_dev_only.sql"), cancellationToken);
        logger.LogInformation("Database bootstrap completed.");
    }

    // StopAsync: Không có tác vụ nền cần dọn riêng khi ứng dụng tắt.
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ExecuteFile: Đọc file SQL đã copy ra output, chạy trên connection với timeout 120 giây.
    private static async Task ExecuteFile(System.Data.Common.DbConnection connection, string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Database bootstrap file was not copied to output.", path);
        await using var command = connection.CreateCommand();
        command.CommandText = await File.ReadAllTextAsync(path, cancellationToken);
        command.CommandType = CommandType.Text;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
