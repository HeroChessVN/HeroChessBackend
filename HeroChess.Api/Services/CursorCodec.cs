using System.Text;
using HeroChess.Api.Infrastructure;

namespace HeroChess.Api.Services;

internal static class CursorCodec
{
    public static (DateTimeOffset At, Guid Id) Parse(string cursor)
    {
        string[] parts;
        try { parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|'); }
        catch (FormatException) { throw new ApiException(400, "INVALID_CURSOR", "The cursor is invalid."); }
        if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || ticks < DateTimeOffset.MinValue.Ticks || ticks > DateTimeOffset.MaxValue.Ticks ||
            !Guid.TryParse(parts[1], out var id)) throw new ApiException(400, "INVALID_CURSOR", "The cursor is invalid.");
        return (new DateTimeOffset(ticks, TimeSpan.Zero), id);
    }

    public static string Format(DateTimeOffset at, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{at.UtcTicks}|{id}")));

    // ParseRating: Cursor xếp hạng gồm elo và playerId; elo giảm dần, playerId tăng dần làm tie-break.
    public static (int Elo, Guid Id) ParseRating(string cursor)
    {
        string[] parts;
        try { parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|'); }
        catch (FormatException) { throw new ApiException(400, "INVALID_CURSOR", "The cursor is invalid."); }
        if (parts.Length != 2 || !int.TryParse(parts[0], out var elo) || !Guid.TryParse(parts[1], out var id))
            throw new ApiException(400, "INVALID_CURSOR", "The cursor is invalid.");
        return (elo, id);
    }

    public static string FormatRating(int elo, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{elo}|{id}")));
}
