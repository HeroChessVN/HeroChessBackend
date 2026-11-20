using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController, Authorize(Policy = AccountPolicies.Player), Route("api/v1")]
public sealed class PieceCosmeticsController(PieceCosmeticService pieces, ICurrentUser current) : ControllerBase
{
    [HttpGet("piece-cosmetics")]
    public Task<PieceCosmeticCatalogDto> Catalog(CancellationToken ct) => pieces.CatalogAsync(current.UserId, ct);

    [HttpPut("me/piece-cosmetic")]
    public Task<PieceCosmeticDto> Equip(EquipPieceCosmeticRequest request, CancellationToken ct) =>
        pieces.EquipAsync(current.UserId, request, ct);
}
