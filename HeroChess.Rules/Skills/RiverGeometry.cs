// Phase 2.4: River geometry definition for Vạn Cọc and Binh Lâm.
// Implements U-GEO = A: 1 river-crossing path = 1 board column.
// River columns are 4, 5, 6 (indices from the left edge of the 9-column board).
// 3 consecutive paths = 3 consecutive column indices.

namespace HeroChess.Rules.Skills;
using HeroChess.Rules.Effects;

/// <summary>
/// Geometry utilities for river-path Command Skills.
/// U-GEO = A: 1 river-crossing path = 1 board column.
/// River occupies columns 4, 5, 6 (0-indexed from the left edge).
/// </summary>
public static class RiverGeometry
{
    /// <summary>River occupies these column indices (0-indexed from the left edge).</summary>
    public static readonly int[] RiverColumns = { 4, 5, 6 };

    /// <summary>
    /// Validates that the selected columns form exactly 3 consecutive river columns.
    /// </summary>
    public static bool IsValidRiverPathSelection(int[]? columns)
    {
        if (columns == null || columns.Length != 3) return false;
        Array.Sort(columns);
        // Must be exactly 3 consecutive columns within the river columns [4, 5, 6]
        return columns[0] + 1 == columns[1]
            && columns[1] + 1 == columns[2]
            && RiverColumns.Contains(columns[0])
            && RiverColumns.Contains(columns[2]);
    }

    /// <summary>
    /// Returns all board points (10 rows × column) for the given columns.
    /// Each column contains all 10 rows (0-9).
    /// </summary>
    public static IReadOnlyList<BoardPoint> ResolvePositions(int[] columns)
    {
        var points = new List<BoardPoint>(columns.Length * 10);
        foreach (var col in columns)
        {
            for (var row = 0; row < 10; row++)
                points.Add(new BoardPoint(col, row));
        }
        return points;
    }

    /// <summary>
    /// Checks whether a selected column conflicts with any existing effect's TargetPositions.
    /// A conflict occurs when the selected column overlaps any of the existing effect's columns.
    /// </summary>
    public static bool IsColumnConflicting(int column, IEnumerable<EffectInstance> existingEffects)
    {
        foreach (var effect in existingEffects)
        {
            foreach (var pos in effect.TargetPositions)
            {
                if (pos.X == column) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns all non-conflicting columns from the selection, with their board points.
    /// </summary>
    public static (int[] AppliedColumns, IReadOnlyList<BoardPoint> AppliedPositions) ResolveNonConflictingPaths(
        int[] requestedColumns,
        IEnumerable<EffectInstance> existingEffects)
    {
        var appliedCols = new List<int>();
        var appliedPoints = new List<BoardPoint>();
        foreach (var col in requestedColumns)
        {
            if (!IsColumnConflicting(col, existingEffects))
            {
                appliedCols.Add(col);
                for (var row = 0; row < 10; row++)
                    appliedPoints.Add(new BoardPoint(col, row));
            }
        }
        return (appliedCols.ToArray(), (IReadOnlyList<BoardPoint>)appliedPoints);
    }

    /// <summary>
    /// Extracts a 3-element int array from a JsonElement target.
    /// The JsonElement should have a "paths" property with an array of 3 integers.
    /// </summary>
    public static int[]? TryParsePaths(System.Text.Json.JsonElement target)
    {
        if (target.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        if (!target.TryGetProperty("paths", out var pathsProp)) return null;
        if (pathsProp.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
        var list = new List<int>();
        foreach (var el in pathsProp.EnumerateArray())
        {
            if (el.ValueKind != System.Text.Json.JsonValueKind.Number || !el.TryGetInt32(out var val)) return null;
            list.Add(val);
        }
        return list.Count == 3 ? list.ToArray() : null;
    }

    /// <summary>
    /// Extracts a single effectId Guid from a JsonElement target.
    /// </summary>
    public static Guid? TryParseEffectId(System.Text.Json.JsonElement target)
    {
        if (target.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        if (!target.TryGetProperty("effectId", out var idProp)) return null;
        if (idProp.ValueKind != System.Text.Json.JsonValueKind.String) return null;
        return Guid.TryParse(idProp.GetString(), out var guid) ? guid : null;
    }

    /// <summary>
    /// Extracts an (effectId, position) pair from a JsonElement target for Phản Kỳ.
    /// </summary>
    public static (Guid? effectId, BoardPoint? position) TryParseEffectIdAndPosition(System.Text.Json.JsonElement target)
    {
        if (target.ValueKind != System.Text.Json.JsonValueKind.Object) return (null, null);
        var effectId = TryParseEffectId(target);
        if (!target.TryGetProperty("position", out var posProp)) return (effectId, null);
        if (posProp.ValueKind != System.Text.Json.JsonValueKind.Object) return (effectId, null);
        if (!posProp.TryGetProperty("x", out var xProp) || !xProp.TryGetInt32(out var x)) return (effectId, null);
        if (!posProp.TryGetProperty("y", out var yProp) || !yProp.TryGetInt32(out var y)) return (effectId, null);
        return (effectId, new BoardPoint(x, y));
    }

    /// <summary>
    /// Validates that a BoardPoint is within the board.
    /// </summary>
    public static bool IsOnBoard(BoardPoint point) =>
        point.X >= 0 && point.X <= 8 && point.Y >= 0 && point.Y <= 9;
}
