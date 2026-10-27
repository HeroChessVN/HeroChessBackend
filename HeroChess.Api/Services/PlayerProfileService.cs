// Vai trò file: Đọc và ghi hồ sơ hiển thị của player (tên, ảnh, khung) và dựng URL ảnh tuyệt đối; không chứa email hay dữ liệu riêng tư.
using HeroChess.Api.Data;
using HeroChess.Api.Infrastructure;
using HeroChess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Services;

public sealed class PlayerProfileService(AppDbContext db, IHttpContextAccessor http, IConfiguration configuration, IHostEnvironment environment)
{
    // MaxAvatarBytes: Giới hạn kích thước ảnh đại diện tải lên từ máy.
    private const int MaxAvatarBytes = 2 * 1024 * 1024;

    // Ctor rỗng: Dùng khi chỉ cần đọc cursor/validate mà không cần hồ sơ; DI dùng ctor chính.
    public PlayerProfileService(AppDbContext db)
        : this(db, new HttpContextAccessor { HttpContext = null! }, new ConfigurationBuilder().Build(), new StubEnvironment()) { }

    // StubEnvironment: Môi trường tối thiểu cho trường hợp không cần ghi file ảnh.
    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    // ProfileAsync: Hồ sơ đầy đủ của một player để trả về client; elo là điểm hiện tại.
    public async Task<PlayerProfileDto> ProfileAsync(Guid playerId, CancellationToken ct)
    {
        var player = await db.Players.AsNoTracking().SingleOrDefaultAsync(x => x.Id == playerId, ct)
            ?? throw new ApiException(404, "PLAYER_NOT_FOUND", "The player profile is unavailable.");
        var elo = await db.Ratings.AsNoTracking().Where(x => x.PlayerId == playerId)
            .Select(x => (int?)x.Elo).SingleOrDefaultAsync(ct) ?? 0;
        return Build(player, elo);
    }

    // ForPlayersAsync: Hồ sơ nhiều player theo bảng tra cứu; eloOverride thay điểm hiện tại bằng điểm sau trận.
    public async Task<Dictionary<Guid, PlayerProfileDto>> ForPlayersAsync(IEnumerable<Guid> playerIds,
        IReadOnlyDictionary<Guid, int>? eloOverride, CancellationToken ct)
    {
        var ids = playerIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, PlayerProfileDto>();
        var players = await db.Players.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var ratings = await db.Ratings.AsNoTracking().Where(x => ids.Contains(x.PlayerId))
            .ToDictionaryAsync(x => x.PlayerId, x => x.Elo, ct);
        var result = new Dictionary<Guid, PlayerProfileDto>();
        foreach (var player in players)
        {
            var elo = eloOverride is not null && eloOverride.TryGetValue(player.Id, out var fixedElo)
                ? fixedElo
                : ratings.GetValueOrDefault(player.Id);
            result[player.Id] = Build(player, elo);
        }
        return result;
    }

    // UpdateAsync: Sửa displayName/avatarName/frameName; field null giữ nguyên giá trị đang lưu.
    public async Task<MeDto> UpdateAsync(Guid playerId, UpdateProfileRequest request, CancellationToken ct)
    {
        var player = await db.Players.SingleOrDefaultAsync(x => x.Id == playerId, ct)
            ?? throw new ApiException(404, "PLAYER_NOT_FOUND", "The player profile is unavailable.");
        if (request.DisplayName is not null)
        {
            var name = request.DisplayName.Trim();
            if (name.Length is 0 or > 80) throw new ApiException(422, "INVALID_DISPLAY_NAME", "The display name must contain 1 to 80 characters.");
            player.DisplayName = name;
        }
        if (request.AvatarName is not null) player.AvatarName = Asset(request.AvatarName, "INVALID_AVATAR_NAME", "The avatar name must be a non-empty token without spaces or slashes.");
        if (request.FrameName is not null) player.FrameName = Asset(request.FrameName, "INVALID_FRAME_NAME", "The frame name must be a non-empty token without spaces or slashes.");
        player.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await MeAsync(player, ct);
    }

    // UploadAvatarAsync: Lưu ảnh tải lên từ máy vào thư mục avatar rồi trả URL tuyệt đối; chỉ nhận ảnh.
    public async Task<MeDto> UploadAvatarAsync(Guid playerId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ApiException(400, "AVATAR_REQUIRED", "An avatar image file is required.");
        if (file.Length > MaxAvatarBytes) throw new ApiException(413, "AVATAR_TOO_LARGE", "The avatar image must be 2 MB or smaller.");
        var extension = file.ContentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => throw new ApiException(415, "AVATAR_TYPE_UNSUPPORTED", "The avatar image must be PNG, JPEG or WebP.")
        };
        var player = await db.Players.SingleOrDefaultAsync(x => x.Id == playerId, ct)
            ?? throw new ApiException(404, "PLAYER_NOT_FOUND", "The player profile is unavailable.");
        var directory = Path.Combine(environment.ContentRootPath, "wwwroot", "avatars");
        Directory.CreateDirectory(directory);
        // Tên file do server sinh từ id player: không nhận tên file từ client để tránh path traversal.
        var fileName = $"{playerId:N}{extension}";
        await using (var stream = File.Create(Path.Combine(directory, fileName)))
            await file.CopyToAsync(stream, ct);
        player.AvatarPath = fileName;
        player.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await MeAsync(player, ct);
    }

    // MeAsync: Hồ sơ tài khoản của player kèm ví/Elo, ảnh và khung.
    public async Task<MeDto> MeAsync(Guid playerId, CancellationToken ct)
    {
        var player = await db.Players.AsNoTracking().SingleOrDefaultAsync(x => x.Id == playerId, ct)
            ?? throw new ApiException(404, "PLAYER_NOT_FOUND", "The player profile is unavailable.");
        return await MeAsync(player, ct);
    }

    // MeAsync: Dựng MeDto từ entity player đã nạp.
    private async Task<MeDto> MeAsync(Player player, CancellationToken ct)
    {
        var wallet = await db.Wallets.AsNoTracking().Where(x => x.PlayerId == player.Id)
            .Select(x => (long?)x.Balance).SingleOrDefaultAsync(ct) ?? 0;
        // Elo luôn được trả kể cả khi chưa có hồ sơ rating: bằng 0, không bỏ field.
        var elo = await db.Ratings.AsNoTracking().Where(x => x.PlayerId == player.Id)
            .Select(x => (int?)x.Elo).SingleOrDefaultAsync(ct) ?? 0;
        return new MeDto(player.Id, player.DisplayName, player.Role, player.Status, player.Id, player.IsGuest,
            wallet.ToString(), elo, player.AvatarName, player.FrameName, AvatarUrl(player.AvatarPath));
    }

    // Build: Dựng DTO hồ sơ công khai từ entity player.
    private PlayerProfileDto Build(Player player, int elo) =>
        new(player.Id, player.DisplayName, elo, player.AvatarName, player.FrameName, AvatarUrl(player.AvatarPath));

    // Asset: Kiểm tra mã ảnh/khung là token không dấu cách, không chứa '/' hoặc '\'.
    private static string? Asset(string value, string code, string message)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is 0 or > 80 || trimmed.Any(char.IsWhiteSpace) || trimmed.Contains('/') || trimmed.Contains('\\'))
            throw new ApiException(422, code, message);
        return trimmed;
    }

    // AvatarUrl: Dựng URL tuyệt đối từ host của request; không có request thì dùng base URL cấu hình.
    private string? AvatarUrl(string? avatarPath)
    {
        if (string.IsNullOrEmpty(avatarPath)) return null;
        var request = http.HttpContext?.Request;
        var baseUrl = request is not null
            ? $"{request.Scheme}://{request.Host}{request.PathBase}"
            : configuration["PublicBaseUrl"]?.TrimEnd('/');
        return string.IsNullOrEmpty(baseUrl) ? null : $"{baseUrl}/avatars/{avatarPath}";
    }
}
