using NSG.Mobile.Application.Common;

namespace NSG.Mobile.Tests;

/// <summary>
/// The shared Admin Portal identity resolver: one place that turns the request's <c>admin_profile_id</c> into the actor stored in
/// integer provenance and audit columns, and never converts it silently. No database; the current user is a fake.
/// </summary>
public class AdminActorTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(4242L)]
    [InlineData((long)int.MaxValue)]
    public void A_usable_admin_profile_id_becomes_the_actor_exactly(long id)
    {
        FakeCurrentUser user = new() { AdminProfileId = id, UserId = 9001 };

        Assert.True(user.TryGetAdminActor(out AdminActor actor));
        Assert.Equal((id, (int)id), (actor.Id, actor.Key));
        Assert.True(AdminActor.TryFrom(id, out AdminActor same));
        Assert.Equal(actor, same);
        Assert.False(user.HasUnusableAdminId());
        Assert.Equal((int)id, user.AuditUserId());   // the admin profile id, never the legacy 9001
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData((long)int.MaxValue + 1)]
    [InlineData(long.MaxValue)]
    public void A_missing_zero_negative_or_overflowing_admin_profile_id_is_refused(long? id)
    {
        FakeCurrentUser user = new() { AdminProfileId = id, UserId = 9001 };

        Assert.False(user.TryGetAdminActor(out AdminActor actor));
        Assert.Equal(default, actor);
        Assert.Equal(AdminActorExtensions.AdminNotIdentifiedMessage, "The administrator could not be identified.");
        if (id != null) Assert.False(AdminActor.TryFrom(id.Value, out _));
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(-7L, true)]
    [InlineData((long)int.MaxValue + 1, true)]
    [InlineData(1L, false)]
    [InlineData((long)int.MaxValue, false)]
    public void Services_that_still_admit_legacy_tokens_refuse_only_an_admin_id_that_is_present_but_unusable(long id, bool unusable)
        => Assert.Equal(unusable, new FakeCurrentUser { AdminProfileId = id }.HasUnusableAdminId());

    [Fact]
    public void A_legacy_staff_token_has_no_admin_id_so_it_is_not_refused_and_keeps_its_legacy_audit_user()
    {
        FakeCurrentUser legacy = new() { AdminProfileId = null, UserId = 9001 };

        Assert.False(legacy.HasUnusableAdminId());
        Assert.Equal(9001, legacy.AuditUserId());
        Assert.Null(new FakeCurrentUser().AuditUserId());                                  // an anonymous caller has no actor to record
        Assert.Null(new FakeCurrentUser { AdminProfileId = (long)int.MaxValue + 1, UserId = 9001 }.AuditUserId());   // never the legacy id for an Admin Portal caller
    }

    [Fact]
    public void Two_requests_resolve_their_own_identity_with_nothing_shared()
    {
        FakeCurrentUser a = new() { AdminProfileId = 10 };
        FakeCurrentUser b = new() { AdminProfileId = 20 };

        Assert.True(a.TryGetAdminActor(out AdminActor actorA));
        Assert.True(b.TryGetAdminActor(out AdminActor actorB));
        a.AdminProfileId = 30;   // a later change on one request's user must not leak into the other, or into an actor already resolved
        Assert.True(a.TryGetAdminActor(out AdminActor actorA2));

        Assert.Equal((10, 20, 30), (actorA.Key, actorB.Key, actorA2.Key));
    }
}
