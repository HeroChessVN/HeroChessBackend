// Phase 2.4: Skill implementation key constants.
// These match the implementation_key values in the database catalog.
namespace HeroChess.Rules.Skills;

/// <summary>
/// Canonical implementation key strings for the four Command Skills.
/// These values must match the team_skill.implementation_key values in the database.
/// </summary>
public static class SkillKeys
{
    /// <summary>Vạn Cọc Trấn Giang.</summary>
    public const string VanCocTranGiang = "van_coc_tran_giang";

    /// <summary>Phản Kỳ Đoạt Thế.</summary>
    public const string PhanKyDoatThe = "phan_ky_doat_the";

    /// <summary>Phá Trận Đoạt Phong.</summary>
    public const string PhaTranDoatPhong = "pha_tran_doat_phong";

    /// <summary>Binh Lâm Thủy Hiểm.</summary>
    public const string BinhLamThuyHien = "binh_lam_thuy_hien";
}
