// Vai trò file: Entity riêng của module Admin (quyền, gift code, ban). Bảng gốc vẫn nằm ở Entities.cs.
using System.Text.Json;

namespace HeroChess.Api.Data;

// AdminPermission: Quyền fine-grained của một tài khoản admin. Không có dòng = admin toàn quyền (tương thích ngược).
public sealed class AdminPermission
{
    public Guid AdminUserId { get; set; }
    public string[] Permissions { get; set; } = Array.Empty<string>();
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

// GiftCode: Mã quy tặng do admin tạo; phần thưởng là coin, hero hoặc cosmetic.
public sealed class GiftCode
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string RewardKind { get; set; } = "";
    public int RewardQuantity { get; set; }
    public Guid? RewardHeroId { get; set; }
    public Guid? RewardCosmeticId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int MaxRedemptions { get; set; }
    public int MaxPerPlayer { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// GiftCodeRedemption: Một lần quy đổi; redemption_no để giữ giới hạn nhiều lần cho mỗi player.
public sealed class GiftCodeRedemption
{
    public Guid Id { get; set; }
    public Guid GiftCodeId { get; set; }
    public Guid PlayerId { get; set; }
    public int RedemptionNo { get; set; }
    public string RewardKind { get; set; } = "";
    public int RewardQuantity { get; set; }
    public long? CoinBalanceAfter { get; set; }
    public DateTimeOffset RedeemedAt { get; set; }
}
