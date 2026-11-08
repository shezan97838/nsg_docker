# ADR-024: PIS Admin block / restore of mobile access, and the `SURAKSHA_1001` code

Status: Accepted (implemented; migration `Add_Mobile_Access_Block` applied to the **local** `nsg_mobile_dev` only, not to any shared, test, UAT or production database)
Date: 2026-11-06. Builds on ADR-014 (mobile profile), ADR-015 (sessions), ADR-017 (Admin Portal). All timestamps are IST (`CLAUDE.md`, "Timestamp Convention").

## Context
A PIS Admin must be able to cut a person off from the mobile app by NSG number: every session ends at once, the person cannot log in again
until a PIS Admin restores access, and the app is told - with one machine-readable code - that the cause is an administrative block, so it can
clear its local session and cached personnel data. Before this ADR nothing wrote `mobile_user_profiles.is_active`, the response envelope had no
machine-readable code at all (the error code had been deliberately removed from the body), and a rejected access token could not say why.

## Decision

### Data model (the minimum)
Three nullable columns on `mobile_user_profiles` and one check constraint:

| Column | Type | Meaning |
|---|---|---|
| `access_blocked_at_ist` | `datetime(6) NULL` | **The marker.** Non-null = blocked through this mechanism (when, IST). |
| `access_blocked_by` | `int NULL` | Admin Portal profile id of the PIS Admin who blocked (no FK, like `created_by`). |
| `access_block_reason` | `varchar(500) NULL` | Optional reason. |

`ck_mobile_user_profiles_access_block`: the three columns are all NULL, or `access_blocked_at_ist` is set and `is_active = 0`. A block therefore
**always** means "inactive", and the database refuses a reason or an admin without a block.

- `is_active` keeps its meaning and every existing guard (login, refresh, access-token session check, MPIN change, profile and salary-slip reads).
  A block sets it to false; restore sets it to true **only if the marker is set**. A profile that is inactive for any other reason is neither
  blocked nor restored here (409), so restore can never reactivate an account that was disabled for an unrelated reason.
- `MobileAppActivationStatus` (onboarding/approval, on `users_pis`) is unchanged by block and restore.
- Block history beyond the current block lives in the audit trail (`MobileAccessBlocked` / `MobileAccessRestored`); no history table.
- Revocation reason `AdminBlocked` is added to `MobileSessionRevokeReasons` (`revoke_reason` is `varchar(50)`, no schema change).

### Block and restore
- **Block** (one transaction, `MobileAccessRepository.BlockAsync`): a conditional `UPDATE ... WHERE is_active AND access_blocked_at_ist IS NULL`
  claims the row (of simultaneous requests exactly one changes it; it also takes the profile row lock), then every live refresh token of the
  profile is revoked with reason `AdminBlocked`. A repeated block changes nothing but still clears a stray live token.
- **Restore** (one conditional statement, `RestoreAsync`): `UPDATE ... WHERE access_blocked_at_ist IS NOT NULL` sets `is_active = 1` and clears the three
  columns. It never touches `mobile_refresh_tokens`: revoked sessions stay revoked and the person must log in again. A pre-existing lockout is left alone.

### Race safety (login and refresh versus block)
- `StartSessionAsync` (login) first runs a conditional update on the profile row (`IsActive` and not blocked), which waits for a block that is still
  running, then **reads** the committed state in the same transaction; if the profile is inactive or blocked it stores nothing and reports
  `AdminBlocked` / `ProfileInactive`. A block that has committed can therefore never be followed by a stored session. A block that commits after
  the session was stored revokes it (its token update runs after the profile row lock).
- The profile marker is authoritative: `GetSessionStateAsync` reports `AdminBlocked` for a **live** token on a blocked profile, so even the instant
  between a committed profile change and the revocation reaching the token row yields no usable token. `RefreshAsync` re-reads the profile after
  rotating and revokes the new token with `AdminBlocked` (never relabelled `ProfileInactive`).

### `SURAKSHA_1001`
Reserved exclusively for an explicit PIS Admin block. It is returned, with the message "Your mobile access has been blocked by the PIS Admin. Please
contact the PIS cell.", in exactly these places:

| Flow | Status | How |
|---|---|---|
| Authenticated request with an existing access token | 401 | `OnTokenValidated` finds `MobileSessionState.AdminBlocked`, leaves the code in `HttpContext.Items`; `GlobalExceptionHandlingMiddleware.WriteBodyForBareErrorAsync` writes it (for a 401 only) |
| `POST mobile-auth/token/refresh` | 403 | the presented token was revoked with `AdminBlocked`, or the profile is blocked after rotation; no token is issued |
| `POST mobile-auth/login` | 403 | checked before the MPIN (as the inactive check always was): no token, no session, no failed-attempt count, block untouched |
| `POST mobile-auth/login/check` | 200 | the usual `data.status = ACCESS_UNAVAILABLE` plus the code and the block message |

Never for: logout, token expiry, invalid/unknown token, MPIN change or reset, token reuse, a new login replacing the session (that has its own code, `SURAKSHA_1002`, see ADR-025), lockout, or a profile that
is inactive for another reason. Tests assert each.

### Envelope: an optional `code`
`ApiResponse<T>` gains `string? Code` (omitted from the JSON when null, so every existing body is byte-for-byte unchanged). It is filled only from
`ApiErrorCodes.PublicCodes`, which contains `SURAKSHA_1001` and (ADR-025) `SURAKSHA_1002`; `ApiResponse.Fail/Ok` drop any other value, and `ApiEnvelope.ForProblem` reads the code
from the problem's `code` extension (set by `MobileAuthProblem` for this error only), never from `Title`. Success bodies carry it through
`IApiMessageCarrier.Code` (used by login/check). The 401 of a rejected token is written by the middleware, which already owns bare error bodies.

```json
{ "success": false, "message": "Your mobile access has been blocked by the PIS Admin. Please contact the PIS cell.", "data": null, "code": "SURAKSHA_1001" }
```

### Admin API
`/api/v1/admin/mobile-access` with the new policy **`RequirePisAdminPortal`**: `AdminBearer` scheme + role `PIS_ADMIN`. `RequirePisAdmin` also admits a
staff Bearer path, so it is not used here. The acting admin is the `admin_profile_id` of the token; a request without one is refused. See
`docs/MOBILE-ACCESS-MANAGEMENT.md`.

### Audit
`AuditAction.MobileAccessBlocked = 120` and `MobileAccessRestored = 121`, written after the change committed: admin id (`user_id`), entity
`MobileUserProfile`/profile id, correlation id, IP, IST timestamp (`created_at`), and `additional_data` JSON `{action, result, nsgNumber, mobileProfileId,
adminProfileId, actorSource: "AdminPortal", reason}`. **An audit failure never undoes or hides the change**: `MobileAccessAdminService` catches it and
logs an error with the ids; `AuditService` itself no longer swallows silently - it logs the failure (action, result, entity, correlation id; never the
payload) at error level.

## Consequences
- A stale access token of a session that was ended by a block keeps answering `SURAKSHA_1001` after a restore (its session stays revoked as
  `AdminBlocked`) until the app discards it, which is the intended signal. A fresh login is the only way back.
- Each authenticated mobile request now makes two small indexed queries (profile marker, newest token of the session) instead of one join.
- A block is refused (409) for a profile that is already inactive for another reason, so that restore stays unambiguous.
- Deadlock between a rotation (token row lock first) and a block (profile lock first) is possible in principle under MySQL; the database tests of the
  race did not hit one, and a deadlock victim would receive a 500 and retry. Not mitigated beyond that.
- The server cannot erase data on a phone: clearing local data happens only when the app receives the code (see the module document).
