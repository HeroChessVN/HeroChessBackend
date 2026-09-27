// Vai trò file: Adapter giữa ASP.NET Identity và các bảng EF tự map. Identity xử lý mật khẩu/token; store lưu user và khởi tạo dữ liệu game.
using HeroChess.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HeroChess.Api.Auth;

public sealed class UserStore(AppDbContext db, IHostEnvironment environment, IOptions<OnboardingOptions> options) :
    IUserPasswordStore<User>, IUserEmailStore<User>, IUserSecurityStampStore<User>
{
    // CreateAsync: Lưu đúng một subtype User. Chỉ Player được onboarding ví/rating/hero; tất cả trong một transaction.
    public async Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Chọn subtype từ cấu hình server; public register không cho client tự chọn admin.
        var now = DateTimeOffset.UtcNow;
        var displayName = (user.Email ?? user.UserName ?? "Player").Split('@')[0].Trim();
        if (displayName.Length == 0) displayName = "Player";
        if (displayName.Length > 80) displayName = displayName[..80];
        var isDevelopmentAdmin = environment.IsDevelopment() && user.Email is not null &&
            options.Value.DevelopmentAdminEmails.Contains(user.Email, StringComparer.OrdinalIgnoreCase);
        User account = isDevelopmentAdmin ? new Admin() : new Player { IsGuest = false };
        account.Id = user.Id; account.UserName = user.UserName; account.NormalizedUserName = user.NormalizedUserName;
        account.Email = user.Email; account.NormalizedEmail = user.NormalizedEmail; account.PasswordHash = user.PasswordHash;
        account.SecurityStamp = user.SecurityStamp; account.DisplayName = displayName;
        account.CreatedAt = now; account.UpdatedAt = now;
        db.Users.Add(account);
        db.AuthIdentities.Add(new AuthIdentity { Id = Guid.NewGuid(), UserId = user.Id, Provider = "aspnet_identity", Subject = user.Id.ToString("D") });
        // Admin không có hồ sơ kinh tế/gameplay. Dữ liệu legacy nếu có được migration giữ để bảo toàn lịch sử.
        if (account is Player)
        {
            var startingCoins = environment.IsDevelopment() ? Math.Max(0, options.Value.DevelopmentStartingCoins) : 0;
            db.Wallets.Add(new PlayerWallet { PlayerId = user.Id, Balance = startingCoins });
            db.Ratings.Add(new PlayerRating { PlayerId = user.Id, Elo = options.Value.StartingElo });
    
            var heroIds = await db.Heroes.AsNoTracking()
                .Where(x => x.IsStarter || (environment.IsDevelopment() && options.Value.GrantDevelopmentFixtureHeroes && x.IsTestFixture))
                .Select(x => x.Id).ToListAsync(cancellationToken);
            foreach (var heroId in heroIds)
                db.PlayerHeroes.Add(new PlayerHero { PlayerId = user.Id, HeroId = heroId, AcquiredVia = environment.IsDevelopment() ? "test" : "starter", AcquiredAt = now });
            if (startingCoins > 0)
                db.CoinTransactions.Add(new CoinTransaction { Id = Guid.NewGuid(), PlayerId = user.Id, IdempotencyKey = "onboarding:development-starter",
                    Kind = "starter", Amount = startingCoins, BalanceAfter = startingCoins, CreatedAt = now });
    
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return IdentityResult.Success;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return IdentityResult.Failed(new IdentityError { Code = "CREATE_FAILED", Description = "The account could not be created." });
        }
    }

    // UpdateAsync: Lưu user đã đổi; chuyển lỗi optimistic concurrency thành lỗi Identity.
    public async Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken)
    {
        db.Users.Update(user);
        try { await db.SaveChangesAsync(cancellationToken); return IdentityResult.Success; }
        catch (DbUpdateConcurrencyException) { return IdentityResult.Failed(new IdentityError { Code = "CONCURRENCY_FAILURE", Description = "The account changed during the request." }); }
    }

    // DeleteAsync: Xóa user; báo bị chặn nếu dữ liệu liên quan không cho xóa.
    public async Task<IdentityResult> DeleteAsync(User user, CancellationToken cancellationToken)
    {
        db.Users.Remove(user);
        try { await db.SaveChangesAsync(cancellationToken); return IdentityResult.Success; }
        catch (DbUpdateException) { return IdentityResult.Failed(new IdentityError { Code = "DELETE_BLOCKED", Description = "The account cannot be deleted while game data references it." }); }
    }

    // FindByIdAsync: Parse ID sang Guid rồi tìm user; ID sai trả null.
    public Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        Guid.TryParse(userId, out var id) ? db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) : Task.FromResult<User?>(null);
    // FindByNameAsync: Tìm user theo username đã được Identity chuẩn hóa.
    public Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(x => x.NormalizedUserName == normalizedUserName, cancellationToken);
    // FindByEmailAsync: Tìm user theo email đã chuẩn hóa.
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
    // GetUserIdAsync: Trả ID dạng chuỗi mà Identity yêu cầu.
    public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.Id.ToString("D"));
    // GetUserNameAsync: Đọc username trong object user.
    public Task<string?> GetUserNameAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
    // SetUserNameAsync: Đổi username trong object; chưa tự SaveChanges.
    public Task SetUserNameAsync(User user, string? userName, CancellationToken cancellationToken) { user.UserName = userName; return Task.CompletedTask; }
    // GetNormalizedUserNameAsync: Đọc username chuẩn hóa dùng khi tìm kiếm.
    public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
    // SetNormalizedUserNameAsync: Gán username chuẩn hóa; lưu DB khi Identity gọi UpdateAsync.
    public Task SetNormalizedUserNameAsync(User user, string? normalizedName, CancellationToken cancellationToken) { user.NormalizedUserName = normalizedName; return Task.CompletedTask; }
    // SetPasswordHashAsync: Nhận hash do Identity tạo; không nhận mật khẩu thô và không tự hash.
    public Task SetPasswordHashAsync(User user, string? passwordHash, CancellationToken cancellationToken) { user.PasswordHash = passwordHash; return Task.CompletedTask; }
    // GetPasswordHashAsync: Trả hash đã lưu để Identity kiểm tra mật khẩu.
    public Task<string?> GetPasswordHashAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash);
    // HasPasswordAsync: Cho biết user đã có password hash hay chưa.
    public Task<bool> HasPasswordAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.PasswordHash is not null);
    // SetEmailAsync: Gán email và dùng email làm username.
    public Task SetEmailAsync(User user, string? email, CancellationToken cancellationToken) { user.Email = email; user.UserName = email; return Task.CompletedTask; }
    // GetEmailAsync: Đọc email hiện tại.
    public Task<string?> GetEmailAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.Email);
    // GetEmailConfirmedAsync: Prototype luôn trả true: hiện chưa có quy trình xác minh email.
    public Task<bool> GetEmailConfirmedAsync(User user, CancellationToken cancellationToken) => Task.FromResult(true);
    // SetEmailConfirmedAsync: No-op trong prototype; không có cột lưu trạng thái xác minh email.
    public Task SetEmailConfirmedAsync(User user, bool confirmed, CancellationToken cancellationToken) => Task.CompletedTask;
    // GetNormalizedEmailAsync: Đọc email chuẩn hóa.
    public Task<string?> GetNormalizedEmailAsync(User user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedEmail);
    // SetNormalizedEmailAsync: Đồng bộ email và username chuẩn hóa cho luồng đăng nhập bằng email.
    public Task SetNormalizedEmailAsync(User user, string? normalizedEmail, CancellationToken cancellationToken) { user.NormalizedEmail = normalizedEmail; user.NormalizedUserName = normalizedEmail; return Task.CompletedTask; }
    // SetSecurityStampAsync: Gán stamp để Identity nhận biết thông tin bảo mật đã thay đổi.
    public Task SetSecurityStampAsync(User user, string stamp, CancellationToken cancellationToken) { user.SecurityStamp = stamp; return Task.CompletedTask; }
    // GetSecurityStampAsync: Đọc stamp hiện tại của user.
    public Task<string?> GetSecurityStampAsync(User user, CancellationToken cancellationToken) => Task.FromResult<string?>(user.SecurityStamp);
    // Dispose: Không tự dispose DbContext: container DI quản lý vòng đời của dependency này.
    public void Dispose() { }
}
