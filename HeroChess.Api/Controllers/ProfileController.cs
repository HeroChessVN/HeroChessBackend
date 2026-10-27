// Vai trò file: Hồ sơ tài khoản chung; chỉ Player có thêm ví/Elo, Admin không giả lập dữ liệu game.
using HeroChess.Api.Auth;
using HeroChess.Api.Data;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Controllers;

[ApiController]
[Authorize(Policy = AccountPolicies.Account)]
[Route("api/v1")]
public sealed class ProfileController(AppDbContext db, ICurrentUser current, PlayerProfileService profiles) : ControllerBase
{
    [HttpGet("me")]
    // Me: userId có ở cả hai loại; playerId/coin/Elo là null cho Admin. Policy kiểm tra status hiện tại.
    public async Task<ActionResult<MeDto>> Me(CancellationToken cancellationToken)
    {
        var account = await db.Users.AsNoTracking().SingleAsync(x => x.Id == current.UserId, cancellationToken);
        if (account is Admin) return Ok(new MeDto(account.Id, account.DisplayName, account.Role, account.Status));
        return Ok(await profiles.MeAsync(current.UserId, cancellationToken));
    }

    // UpdateMe: PATCH hồ sơ hiển thị (tên, ảnh, khung); Admin không có dữ liệu game nên bị chặn.
    [HttpPatch("me"), Authorize(Policy = AccountPolicies.Player)]
    public async Task<ActionResult<MeDto>> UpdateMe(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        Ok(await profiles.UpdateAsync(current.UserId, request, cancellationToken));

    // UploadAvatar: POST ảnh đại diện tải lên từ máy; multipart với field "file".
    [HttpPost("me/avatar"), Authorize(Policy = AccountPolicies.Player), RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<ActionResult<MeDto>> UploadAvatar(IFormFile? file, CancellationToken cancellationToken) =>
        Ok(await profiles.UploadAvatarAsync(current.UserId, file, cancellationToken));
}
