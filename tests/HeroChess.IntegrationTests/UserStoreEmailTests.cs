using HeroChess.Api.Auth;
using HeroChess.Api.Data;
using Xunit;

namespace HeroChess.IntegrationTests;

public sealed class UserStoreEmailTests
{
    [Fact]
    public async Task Email_confirmation_starts_false_and_tracks_changes()
    {
        using var store = new UserStore(null!, null!, null!);
        var user = new Player();
        Assert.False(await store.GetEmailConfirmedAsync(user, default));
        await store.SetEmailConfirmedAsync(user, true, default);
        Assert.True(await store.GetEmailConfirmedAsync(user, default));
        await store.SetEmailConfirmedAsync(user, false, default);
        Assert.False(await store.GetEmailConfirmedAsync(user, default));
    }
}
