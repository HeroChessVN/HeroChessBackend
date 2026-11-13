// Vai trò file: HTTP quản lý tài khoản admin và quyền fine-grained. Cần quyền 'admin'.
using HeroChess.Api.Auth;
using HeroChess.Api.Services;
using HeroChess.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HeroChess.Api.Controllers;

[ApiController]
[Authorize(Policy = AccountPolicies.Admin)]
[Route("api/v1/admin/accounts")]
public sealed class AdminAccountController(AdminAccountService accounts, ICurrentUser current) : ControllerBase
{
    // Accounts: Danh sách tài khoản admin kèm quyền.
    [HttpGet]
    [Authorize(Policy = AccountPolicies.AdminRead)]
    public async Task<ActionResult<AdminAccountPageDto>> Accounts(CancellationToken ct = default) =>
        Ok(await accounts.ListAsync(ct));

    // CreateAccount: Tạo tài khoản admin mới (đường cấp admin ở production).
    [HttpPost]
    [Authorize(Policy = AccountPolicies.AdminWrite)]
    public async Task<ActionResult<AdminAccountDto>> CreateAccount(AdminCreateAccountRequest request, CancellationToken ct = default) =>
        Ok(await accounts.CreateAsync(current.UserId, request, ct));

    // UpdatePermissions: Thay toàn bộ quyền của một tài khoản admin.
    [HttpPut("{id:guid}/permissions")]
    [Authorize(Policy = AccountPolicies.AdminWrite)]
    public async Task<ActionResult<AdminAccountDto>> UpdatePermissions(Guid id, AdminUpdatePermissionsRequest request,
        CancellationToken ct = default) =>
        Ok(await accounts.UpdatePermissionsAsync(current.UserId, id, request, ct));

    // PermissionCatalog: Danh sách quyền hợp lệ để AdminWeb dựng form phân quyền.
    [HttpGet("/api/v1/admin/permissions")]
    [Authorize(Policy = AccountPolicies.AdminRead)]
    public ActionResult<IReadOnlyList<AdminPermissionDto>> Permissions() => Ok(AdminPermissions.Describe());
}