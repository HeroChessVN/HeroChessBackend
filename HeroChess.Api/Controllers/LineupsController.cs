// Vai trò file: HTTP CRUD đội hình; actor lấy từ token, không nhận playerId tự khai trong body.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController]
[Authorize(Policy = AccountPolicies.Player)]
[Route("api/v1/lineups")]
public sealed class LineupsController(LineupService service, ICurrentUser current) : ControllerBase
{
    [HttpGet]
    // List: GET danh sách lineup của actor.
    public async Task<ActionResult<IReadOnlyList<LineupDto>>> List(CancellationToken cancellationToken) => Ok(await service.ListAsync(current.UserId, cancellationToken));

    [HttpPost]
    // Create: POST lineup đã validate; trả 201 kèm URL tài nguyên.
    public async Task<ActionResult<LineupDto>> Create(SaveLineupRequest request, CancellationToken cancellationToken)
    {
        var lineup = await service.CreateAsync(current.UserId, request, cancellationToken);
        return Created($"/api/v1/lineups/{lineup.Id}", lineup);
    }

    [HttpPut("{id:guid}")]
    // Update: PUT lineup theo ID và expectedRevision.
    public async Task<ActionResult<LineupDto>> Update(Guid id, SaveLineupRequest request, CancellationToken cancellationToken) =>
        Ok(await service.UpdateAsync(current.UserId, id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    // Delete: DELETE lineup đúng owner/revision; thành công trả 204.
    public async Task<IActionResult> Delete(Guid id, [FromQuery] int expectedRevision, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(current.UserId, id, expectedRevision, cancellationToken);
        return NoContent();
    }
}
