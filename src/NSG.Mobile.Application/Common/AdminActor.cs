using NSG.Mobile.Application.Interfaces.Services;

namespace NSG.Mobile.Application.Common;

/// <summary>
/// The authenticated Admin Portal admin acting on the current request: <see cref="Id"/> is <c>admin_profiles.id</c> from the validated token's
/// <c>admin_profile_id</c> claim; <see cref="Key"/> is the same value as the <c>int</c> the integer audit and provenance columns
/// (<c>audit_logs.user_id</c>, <c>users_pis.created_by</c>, <c>users_pis.updated_by</c>) can hold.
/// </summary>
public readonly record struct AdminActor(long Id, int Key)
{
    /// <summary>
    /// Builds the actor from an admin profile id that the caller already holds (for example one passed down from the controller or read from a verified
    /// account). False for zero, a negative id, or an id above <see cref="int.MaxValue"/>: it must never be stored in an integer column.
    /// </summary>
    public static bool TryFrom(long adminProfileId, out AdminActor actor)
    {
        if (adminProfileId is > 0 and <= int.MaxValue)
        {
            actor = new AdminActor(adminProfileId, (int)adminProfileId);
            return true;
        }

        actor = default;
        return false;
    }
}

/// <summary>
/// The one place that turns the request's Admin Portal identity into an <see cref="AdminActor"/>. The identity is read from
/// <see cref="ICurrentUserService"/> on every call (it is request-scoped; nothing is cached or shared), validated, and never converted
/// silently: a missing id, an id of zero or less, or an id above <see cref="int.MaxValue"/> is refused, so no NULL, truncated or wrapped value
/// can be stored in an integer column.
/// </summary>
public static class AdminActorExtensions
{
    /// <summary>Message of the 401 returned when the acting administrator cannot be identified (missing, not positive, or too large for the integer columns).</summary>
    public const string AdminNotIdentifiedMessage = "The administrator could not be identified.";

    /// <summary>
    /// Resolves the acting Admin Portal admin. False when the caller has no usable <c>admin_profile_id</c> (a legacy staff or mobile token carries
    /// none) or when it does not fit the integer columns; the caller must then answer 401 with <see cref="AdminNotIdentifiedMessage"/>
    /// before reading or writing anything.
    /// </summary>
    public static bool TryGetAdminActor(this ICurrentUserService currentUser, out AdminActor actor)
    {
        if (currentUser.AdminProfileId is long id)
            return AdminActor.TryFrom(id, out actor);

        actor = default;
        return false;
    }

    /// <summary>
    /// For services whose endpoints still admit legacy staff tokens next to Admin Portal tokens (the PIS lookup masters): true when the caller IS an
    /// Admin Portal admin (an <c>admin_profile_id</c> claim is present) but the id is unusable, so the operation must be refused before any write.
    /// A legacy staff token carries no such claim and is not affected.
    /// </summary>
    public static bool HasUnusableAdminId(this ICurrentUserService currentUser)
        => currentUser.AdminProfileId is long id && (id <= 0 || id > int.MaxValue);

    /// <summary>
    /// The value for <c>audit_logs.user_id</c>. An Admin Portal admin is identified by the admin profile id (never by the unparsable legacy
    /// <c>sub</c>); only a caller without an Admin Portal identity falls back to the legacy staff user id. Call
    /// <see cref="HasUnusableAdminId"/> first so an unusable admin id never reaches this point.
    /// </summary>
    public static int? AuditUserId(this ICurrentUserService currentUser)
        => currentUser.TryGetAdminActor(out AdminActor actor) ? actor.Key : currentUser.AdminProfileId == null ? currentUser.UserId : null;
}
