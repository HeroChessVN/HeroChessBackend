// Phase 2.4: Skill implementation key constants.
// These match the implementation_key values in the database catalog.
namespace HeroChess.Rules.Skills;

/// <summary>
/// Canonical implementation key strings for the four Command Skills.
/// These values must match the team_skill.implementation_key values in the database.
/// </summary>
public static class SkillKeys
{
    // ---- Command Skills ----
    /// <summary>Vạn Cọc Trấn Giang.</summary>
    public const string VanCocTranGiang = "van_coc_tran_giang";

    /// <summary>Phản Kỳ Đoạt Thế.</summary>
    public const string PhanKyDoatThe = "phan_ky_doat_the";

    /// <summary>Phá Trận Đoạt Phong.</summary>
    public const string PhaTranDoatPhong = "pha_tran_doat_phong";

    /// <summary>Binh Lâm Thủy Hiểm.</summary>
    public const string BinhLamThuyHien = "binh_lam_thuy_hien";

    // ---- Hero Skills ----
    // Quang Trung cooldown-ready special movement.
    public const string QuangTrungSpecialMove = "quang_trung.special_move";
    // Quang Trung: cooldown starts at this value when cooldown resets.
    public const int QuangTrungCooldownTurns = 3;
    // TraitState key for Quang Trung cooldown-ready indicator.
    public const string QuangTrungCooldownKey = "cooldownReady";
    public const string QuangTrungCooldownRemainingKey = "quangTrungCooldownRemaining";

    // Phạm Ngũ Lão "hoành sóc giang sơn" — charge granted after capture.
    public const string HoanhSoc = "rook.hoanh_soc";
    // TraitState key for hoành sóc charge.
    public const string HoanhSocChargedKey = "hoanhSocCharged";

    // ---- New Command Skills (Step 6) ----
    /// <summary>Thành — Command Skill: temporary fortification, only destroyed by Cannon.</summary>
    public const string Thanh = "thanh";

    /// <summary>Rào — Command Skill: temporary barrier behaving like a Soldier for destruction.</summary>
    public const string Rao = "rao";
    public const string Khien = "khien";
    public const string ShieldEffect = "shield";

    /// <summary>Trần Hưng Đạo — Tượng Hero Skill: place a Cọc on the river.</summary>
    public const string ThDTuongCoc = "tran_hung_dao_tuong.coc";
    public const string HeroCooldownRemainingKey = "heroCooldownRemaining";

    /// <summary>Trần Hưng Đạo — Tượng Hero Skill cooldown: 5 shared turns.</summary>
    public const int ThDTuongCocCooldownTurns = 5;

    // ---- New Hero Trait Keys (Step 6) ----
    /// <summary>
    /// Lý Thường Kiệt — Xe passive: bypass Thành / Rào / Cọc obstacles.
    /// Stored as MovementImplementationKey on the piece.
    /// </summary>
    public const string LyThuongKietXe = "rook.ly_thuong_kiet";

    /// <summary>
    /// Lý Thường Kiệt — Pháo passive: can destroy Thành and enter its cell.
    /// Stored as MovementImplementationKey on the piece.
    /// </summary>
    public const string LyThuongKietPhao = "cannon.ly_thuong_kiet";

    // Obstacle kinds introduced in Step 6.
    /// <summary>ObstacleKind for a Thành fortification.</summary>
    public const string ObstacleKindThanh = "than_h";
    /// <summary>ObstacleKind for a Rào barrier.</summary>
    public const string ObstacleKindRao = "rao";
    /// <summary>ObstacleKind for a Trần Hưng Đạo — Tượng Cọc stake.</summary>
    public const string ObstacleKindThDTuongCoc = "thd_tuong_coc";
}
