using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController, Authorize(Policy = AccountPolicies.Player), Route("api/v1")]
public sealed class BoardCosmeticsController(BoardCosmeticService boards, ICurrentUser current) : ControllerBase
{
    [HttpGet("board-cosmetics")]
    public Task<BoardCosmeticCatalogDto> Catalog(CancellationToken ct) => boards.CatalogAsync(current.UserId, ct);

    [HttpPut("me/board-cosmetic")]
    public Task<BoardCosmeticDto> Equip(EquipBoardCosmeticRequest request, CancellationToken ct) =>
        boards.EquipAsync(current.UserId, request, ct);
}
