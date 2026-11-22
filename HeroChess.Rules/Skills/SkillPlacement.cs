namespace HeroChess.Rules.Skills;

/// <summary>
/// SkillPlacement: nguồn sự thật duy nhất cho việc một ô có đặt được vật cản skill hay không.
///
/// Enemy hidden snares cannot affect placement: rejection would reveal their positions.
///
/// Server là nơi phán quyết (mã lỗi CELL_OCCUPIED / INVALID_TARGET); client chỉ tô và nhận ô theo cùng luật.
/// </summary>
public static class SkillPlacement
{
    /// <summary>
    /// IsCellFree: ô trống cho mọi vật cản skill (Cọc, Thành, Rào) và bẫy.
    /// </summary>
    public static bool IsCellFree(GameState state, BoardPoint position, Side actor) =>
        !state.Pieces.Any(piece => piece.Status == PieceStatus.Alive && piece.Position == position) &&
        !state.Obstacles.Any(obstacle => obstacle.Position == position) &&
        !state.RiverSnares.Any(snare => snare.Owner == actor && snare.Position == position && snare.ExpiresAt > state.TurnIndex);
}
