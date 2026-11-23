namespace HeroChess.Contracts;

public sealed record CustomRoomDto(Guid RoomId, string Code, int MemberCount, int Capacity, string Status,
    bool IsOwner, Guid? MatchId, Guid? LastMatchId, bool OwnerOnline,
    IReadOnlyList<CustomRoomMemberDto> Members);

public sealed record CustomRoomMemberDto(int Seat, bool IsSelf, bool IsOwner, PlayerProfileDto Player);

public sealed record JoinCustomRoomRequest(string? Code);
public sealed record LeaveCustomRoomRequest(string? Reason);
public sealed record RematchCustomRoomRequest(Guid PreviousMatchId, Guid RequestId);
