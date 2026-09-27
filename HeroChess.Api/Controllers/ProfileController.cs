// Vai trò file: Hồ sơ tài khoản chung; chỉ Player có thêm ví/Elo, Admin không giả lập dữ liệu game.
using HeroChess.Api.Auth;
using HeroChess.Api.Data;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Controllers;

[ApiController]
[Authorize(Policy = AccountPolicies.Account)]
[Route("api/v1")]
public sealed class ProfileController(AppDbContext db, ICurrentUser current) : ControllerBase
{
    [HttpGet("me")]
    // Me: userId có ở cả hai loại; playerId/coin/Elo là null cho Admin. Policy kiểm tra status hiện tại.
    public async Task<ActionResult<MeDto>> Me(CancellationToken cancellationToken)
    {
        var account = await db.Users.AsNoTracking().SingleAsync(x => x.Id == current.UserId, cancellationToken);
        if (account is Admin) return Ok(new MeDto(account.Id, account.DisplayName, account.Role, account.Status));
        var result = await (from player in db.Players.AsNoTracking()
            join wallet in db.Wallets.AsNoTracking() on player.Id equals wallet.PlayerId
            join rating in db.Ratings.AsNoTracking() on player.Id equals rating.PlayerId
            where player.Id == current.UserId && player.Status == "active"
            select new MeDto(player.Id, player.DisplayName, player.Role, player.Status, player.Id, player.IsGuest, wallet.Balance.ToString(), rating.Elo))
            .SingleOrDefaultAsync(cancellationToken);
        return result is null ? NotFound(new ApiError("PLAYER_NOT_FOUND", "The player profile is unavailable.")) : Ok(result);
    }
}
