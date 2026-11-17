namespace HeroChess.Rules.Skills;

/// <summary>
/// SkillPlacement: nguồn sự thật duy nhất cho việc một ô có đặt được vật cản skill hay không.
///
/// Ô bị chặn khi trên đó có quân sống, có vật cản (Cọc, Thành, Rào, cọc Vạn Cọc) hoặc có bẫy Bạch Đằng.
/// Luật này áp dụng giống nhau cho cả hai phe: không phe nào đặt chồng lên ô đã bị chiếm.
///
/// Server là nơi phán quyết (mã lỗi CELL_OCCUPIED / INVALID_TARGET); client chỉ tô và nhận ô theo cùng luật.
/// </summary>
public static class SkillPlacement
{
    /// <summary>
    /// IsCellFree: ô trống cho mọi vật cản skill (Cọc, Thành, Rào) và bẫy.
    /// hiddenBlocked dùng cho bot: bẫy của địch bị ẩn khỏi tìm kiếm bot nhưng ô đó vẫn chặn đặt vật cản.
    /// </summary>
    public static bool IsCellFree(GameState state, BoardPoint position, IReadOnlyCollection<BoardPoint>? hiddenBlocked = null) =>
        !state.Pieces.Any(piece => piece.Status == PieceStatus.Alive && piece.Position == position) &&
        !state.Obstacles.Any(obstacle => obstacle.Position == position) &&
        !state.RiverSnares.Any(snare => snare.Position == position && snare.ExpiresAt > state.TurnIndex) &&
        (hiddenBlocked is null || !hiddenBlocked.Contains(position));
}
