// Vai trò file: Cấu hình lúc đăng ký: Elo đầu, coin và danh sách admin/hero fixture dành cho Development.
namespace HeroChess.Api.Auth;

public sealed class OnboardingOptions
{
    public bool GrantDevelopmentFixtureHeroes { get; set; }
    public int StartingElo { get; set; } = 1000;
    public long DevelopmentStartingCoins { get; set; }
    public string[] DevelopmentAdminEmails { get; set; } = Array.Empty<string>();
}
