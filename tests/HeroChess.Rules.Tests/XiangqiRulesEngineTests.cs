// Vai trò file: Unit test luật thuần bộ nhớ, không cần PostgreSQL hoặc API.
using HeroChess.Rules;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace HeroChess.Rules.Tests;

public sealed class XiangqiRulesEngineTests
{
    private readonly XiangqiRulesEngine _rules = new();

    [Fact]
    // General_stays_inside_palace_and_cannot_face_enemy_general: Tướng không ra cung và không được lộ mặt Tướng đối phương.
    public void General_stays_inside_palace_and_cannot_face_enemy_general()
    {
        var state = State(Piece(PieceClass.General, Side.Red, 4, 0), Piece(PieceClass.General, Side.Black, 4, 9));
        var general = state.Pieces[0];

        var moves = _rules.GenerateLegalActions(state).Where(x => x.PieceId == general.PieceId).ToArray();

        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 1));
        Assert.Contains(moves, x => x.To == new BoardPoint(3, 0));
    }

    [Fact]
    // Horse_leg_blocks_two_destinations: Chặn chân Mã loại đúng hai đích.
    public void Horse_leg_blocks_two_destinations()
    {
        var horse = Piece(PieceClass.Horse, Side.Red, 4, 4);
        var state = State(horse, Piece(PieceClass.Soldier, Side.Red, 5, 4));

        var moves = _rules.GenerateLegalActions(state).Where(x => x.PieceId == horse.PieceId).ToArray();

        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(6, 5));
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(6, 3));
        Assert.Contains(moves, x => x.To == new BoardPoint(3, 6));
    }

    [Fact]
    // Advisor_moves_one_diagonal_step_inside_palace: Sĩ chỉ chéo một ô trong cung.
    public void Advisor_moves_one_diagonal_step_inside_palace()
    {
        var advisor = Piece(PieceClass.Advisor, Side.Red, 4, 1);
        var state = State(advisor, Piece(PieceClass.General, Side.Red, 4, 0));

        var moves = _rules.GenerateLegalActions(state).Where(x => x.PieceId == advisor.PieceId).ToArray();

        Assert.Contains(moves, x => x.To == new BoardPoint(3, 0));
        Assert.Contains(moves, x => x.To == new BoardPoint(5, 2));
        Assert.DoesNotContain(moves, x => x.To.X < 3 || x.To.X > 5 || x.To.Y > 2);
    }

    [Fact]
    // Rook_stops_at_friendly_piece_and_captures_first_enemy_on_ray: Xe dừng ở vật cản và chỉ ăn quân địch đầu tiên.
    public void Rook_stops_at_friendly_piece_and_captures_first_enemy_on_ray()
    {
        var rook = Piece(PieceClass.Rook, Side.Red, 0, 0);
        var enemy = Piece(PieceClass.Soldier, Side.Black, 2, 0);
        var state = State(rook, enemy, Piece(PieceClass.Soldier, Side.Red, 0, 2));

        var moves = _rules.GenerateLegalActions(state).Where(x => x.PieceId == rook.PieceId).ToArray();

        Assert.Contains(moves, x => x.To == new BoardPoint(2, 0) && x.CapturedPieceId == enemy.PieceId);
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(0, 2));
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(0, 3));
    }

    [Fact]
    // Elephant_requires_clear_eye_and_stays_home: Tượng cơ bản không qua sông và không nhảy mắt bị chặn.
    public void Elephant_requires_clear_eye_and_stays_home()
    {
        var elephant = Piece(PieceClass.Elephant, Side.Red, 2, 2);
        var state = State(elephant, Piece(PieceClass.Soldier, Side.Red, 3, 3));

        var moves = _rules.GenerateLegalActions(state).Where(x => x.PieceId == elephant.PieceId).ToArray();

        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 4));
        Assert.DoesNotContain(moves, x => x.To.Y > 4);
        Assert.Contains(moves, x => x.To == new BoardPoint(0, 4));
    }

    [Fact]
    // Cannon_capture_requires_exactly_one_screen: Pháo ăn khi có đúng một ngòi.
    public void Cannon_capture_requires_exactly_one_screen()
    {
        var cannon = Piece(PieceClass.Cannon, Side.Red, 1, 2);
        var screen = Piece(PieceClass.Soldier, Side.Red, 1, 4);
        var target = Piece(PieceClass.Rook, Side.Black, 1, 7);
        var state = State(cannon, screen, target);

        var moves = _rules.GenerateLegalActions(state).Where(x => x.PieceId == cannon.PieceId).ToArray();

        Assert.Contains(moves, x => x.To == new BoardPoint(1, 7) && x.CapturedPieceId == target.PieceId);
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(1, 5));
    }

    [Fact]
    // Soldier_moves_sideways_only_after_crossing_river: Tốt chỉ được đi ngang sau qua sông.
    public void Soldier_moves_sideways_only_after_crossing_river()
    {
        var before = Piece(PieceClass.Soldier, Side.Red, 4, 4);
        var beforeState = State(before);
        var after = Piece(PieceClass.Soldier, Side.Red, 4, 5);
        var afterState = State(after);

        Assert.DoesNotContain(_rules.GenerateLegalActions(beforeState), x => x.PieceId == before.PieceId && x.To.X != 4);
        Assert.Contains(_rules.GenerateLegalActions(afterState), x => x.PieceId == after.PieceId && x.To == new BoardPoint(3, 5));
        Assert.DoesNotContain(_rules.GenerateLegalActions(afterState), x => x.PieceId == after.PieceId && x.To == new BoardPoint(4, 4));
    }

    [Fact]
    // Black_soldier_moves_toward_decreasing_y: Tốt Black đi theo chiều y giảm.
    public void Black_soldier_moves_toward_decreasing_y()
    {
        var soldier = Piece(PieceClass.Soldier, Side.Black, 4, 6);
        var state = State(soldier);
        state.SideToMove = Side.Black;

        var moves = _rules.GenerateLegalActions(state).Where(x => x.PieceId == soldier.PieceId).ToArray();

        Assert.Contains(moves, x => x.To == new BoardPoint(4, 5));
        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 7));
    }

    [Fact]
    // No_legal_actions_ends_match_without_capturing_general: Hết nước hợp lệ kết thúc trận mà không cần ăn Tướng.
    public void No_legal_actions_ends_match_without_capturing_general()
    {
        var winningSoldier = Piece(PieceClass.Soldier, Side.Red, 4, 7);
        var state = State(
            Piece(PieceClass.General, Side.Red, 3, 0),
            Piece(PieceClass.General, Side.Black, 4, 9),
            winningSoldier,
            Piece(PieceClass.Rook, Side.Red, 3, 8),
            Piece(PieceClass.Rook, Side.Red, 5, 8),
            Piece(PieceClass.Rook, Side.Red, 4, 6));

        var result = _rules.ApplyMove(state, new(winningSoldier.PieceId, new BoardPoint(4, 8)));

        Assert.True(result.Accepted);
        Assert.Equal("red_win", result.State.Result);
        Assert.Equal("checkmate", result.State.EndReason);
        Assert.Equal(PieceStatus.Alive, result.State.Pieces.Single(x => x.Class == PieceClass.General && x.Side == Side.Black).Status);
    }

    [Fact]
    // Move_exposing_own_general_is_rejected: Nước khiến Tướng mình bị chiếu bị từ chối.
    public void Move_exposing_own_general_is_rejected()
    {
        var blocker = Piece(PieceClass.Rook, Side.Red, 4, 5);
        var state = State(
            Piece(PieceClass.General, Side.Red, 4, 0),
            blocker,
            Piece(PieceClass.Rook, Side.Black, 4, 8),
            Piece(PieceClass.General, Side.Black, 3, 9));

        var result = _rules.ApplyMove(state, new(blocker.PieceId, new BoardPoint(5, 5)));

        Assert.False(result.Accepted);
        Assert.Equal("ILLEGAL_MOVE", result.Error!.Code);
    }

    [Fact]
    // Home_diagonal_handler_moves_one_or_two_without_jumping: Tượng special đi chéo 1–2 và không xuyên vật cản.
    public void Home_diagonal_handler_moves_one_or_two_without_jumping()
    {
        var hero = Piece(PieceClass.Elephant, Side.Red, 2, 0, "elephant.diagonal_range");
        var state = State(hero, Piece(PieceClass.Soldier, Side.Red, 3, 1));

        var moves = _rules.GenerateLegalActions(state).Where(x => x.PieceId == hero.PieceId).ToArray();

        Assert.DoesNotContain(moves, x => x.To == new BoardPoint(4, 2));
        Assert.Contains(moves, x => x.To == new BoardPoint(1, 1));
        Assert.Contains(moves, x => x.To == new BoardPoint(0, 2));
    }

    [Fact]
    // Wild_elephant_cannot_repeat_last_distance_and_state_round_trips_on_clone: Dã Tượng không lặp độ dài trước và clone giữ trait state độc lập.
    public void Wild_elephant_cannot_repeat_last_distance_and_state_round_trips_on_clone()
    {
        var hero = Piece(PieceClass.Elephant, Side.Red, 2, 0, "elephant.alternating_distance");
        var state = State(hero);

        var first = _rules.ApplyMove(state, new(hero.PieceId, new BoardPoint(4, 2)));
        Assert.True(first.Accepted);
        var moved = first.State.Pieces.Single(x => x.PieceId == hero.PieceId);
        Assert.Equal(2, moved.TraitState["lastMoveDistance"]);

        first.State.SideToMove = Side.Red;
        var moves = _rules.GenerateLegalActions(first.State).Where(x => x.PieceId == hero.PieceId).ToArray();
        Assert.DoesNotContain(moves, x => Math.Abs(x.To.X - 4) == 2);
        Assert.Equal(2, first.State.Clone().Pieces.Single(x => x.PieceId == hero.PieceId).TraitState["lastMoveDistance"]);
    }

    [Fact]
    // Wild_elephant_trait_state_round_trips_through_json_snapshot: Serialize/deserialize không làm mất lastMoveDistance.
    public void Wild_elephant_trait_state_round_trips_through_json_snapshot()
    {
        var hero = Piece(PieceClass.Elephant, Side.Red, 2, 0, "elephant.alternating_distance");
        var moved = _rules.ApplyMove(State(hero), new(hero.PieceId, new BoardPoint(4, 2)));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

        var restored = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(moved.State, options), options)!;
        restored.SideToMove = Side.Red;

        Assert.Equal(2, restored.Pieces.Single(x => x.PieceId == hero.PieceId).TraitState["lastMoveDistance"]);
        Assert.DoesNotContain(_rules.GenerateLegalActions(restored).Where(x => x.PieceId == hero.PieceId), x => Math.Abs(x.To.X - 4) == 2);
    }

    [Fact]
    // Unimplemented_skill_does_not_mutate_state: Skill chưa triển khai trả lỗi và không sửa input.
    public void Unimplemented_skill_does_not_mutate_state()
    {
        var state = State(Piece(PieceClass.Soldier, Side.Red, 0, 3));

        var result = _rules.ApplyTeamSkill(state, 1);

        Assert.False(result.Accepted);
        Assert.Equal("SKILL_NOT_IMPLEMENTED", result.Error!.Code);
        Assert.Same(state, result.State);
        Assert.Equal(0, state.Version);
    }

    [Fact]
    // Unknown_special_movement_never_silently_falls_back_to_base_piece_rules: Key special không biết không âm thầm dùng luật cơ bản.
    public void Unknown_special_movement_never_silently_falls_back_to_base_piece_rules()
    {
        var hero = Piece(PieceClass.Rook, Side.Red, 0, 0, "hero.not_implemented");
        var state = State(hero);

        var moves = _rules.GenerateLegalActions(state).Where(x => x.PieceId == hero.PieceId).ToArray();

        Assert.Empty(moves);
    }

    [Fact]
    // Apply_move_is_deterministic_and_does_not_mutate_input: Cùng state/action cho cùng kết quả và giữ nguyên input.
    public void Apply_move_is_deterministic_and_does_not_mutate_input()
    {
        var soldier = Piece(PieceClass.Soldier, Side.Red, 0, 3);
        var state = State(soldier);

        var a = _rules.ApplyMove(state, new(soldier.PieceId, new BoardPoint(0, 4)));
        var b = _rules.ApplyMove(state, new(soldier.PieceId, new BoardPoint(0, 4)));

        Assert.True(a.Accepted);
        Assert.Equal(a.State.Pieces.Single(x => x.PieceId == soldier.PieceId).Position,
            b.State.Pieces.Single(x => x.PieceId == soldier.PieceId).Position);
        Assert.Equal(new BoardPoint(0, 3), state.Pieces.Single(x => x.PieceId == soldier.PieceId).Position);
        Assert.Equal(1, a.State.Version);
    }

    // State: Tạo bàn cờ test, bổ sung Tướng khi danh sách chưa có.
    private static GameState State(params PieceState[] pieces)
    {
        var state = new GameState();
        state.Pieces.AddRange(pieces);
        if (!pieces.Any(x => x.Class == PieceClass.General && x.Side == Side.Red))
            state.Pieces.Add(Piece(PieceClass.General, Side.Red, 3, 0));
        if (!pieces.Any(x => x.Class == PieceClass.General && x.Side == Side.Black))
            state.Pieces.Add(Piece(PieceClass.General, Side.Black, 5, 9));
        return state;
    }

    // Piece: Tạo quân test với side/class/vị trí và handler tùy chọn.
    private static PieceState Piece(PieceClass pieceClass, Side side, int x, int y, string? handler = null) => new()
    {
        PieceId = Guid.NewGuid(),
        HeroId = Guid.NewGuid(),
        Class = pieceClass,
        Side = side,
        Position = new BoardPoint(x, y),
        StartPosition = new BoardPoint(x, y),
        SetupPoints = 1,
        MovementImplementationKey = handler
    };
}
