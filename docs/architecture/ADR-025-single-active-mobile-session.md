# ADR-025: Single active mobile session - latest successful login wins, and `SURAKSHA_1002`

Status: accepted (2026-11-06). Builds on ADR-014 (mobile auth) and ADR-024 (`SURAKSHA_1001`).

## Rule
A mobile user has at most one usable session. The **latest successful login wins**: signing in again, from any device (including the one that was signed in before), ends every earlier session. A failed login ends nothing. No device identity is stored or needed: the session family (`sid`) already identifies a session.

## Mechanism (unchanged)
`MobileRefreshTokenRepository.StartSessionAsync` runs one transaction: it takes the profile's row lock, re-reads the profile (blocked or inactive: rollback, nothing stored), revokes every live refresh token of the profile with reason `NewLogin`, and inserts the new first token. Concurrent logins serialize on the profile row lock; the update is a current read, so each login revokes the row a committed earlier login inserted. The reason `NewLogin` is written only here, so it means exactly "replaced by a newer login". No migration: the value fits the existing `revoke_reason` `varchar(50)`.

## `SURAKSHA_1002`
Message: "Your session has been terminated because your account was signed in on another device."

| Caller | HTTP | Body |
|---|---|---|
| Old access token on any authenticated request | 401 | `{ "success": false, "message": "<above>", "data": null, "code": "SURAKSHA_1002" }` |
| Old refresh token (`token/refresh`) | 403 | same body; no token is issued and nothing is written |
| Latest login | 200 | normal login response, no `code` |

- `GetSessionStateAsync` returns the new state `Replaced` when the family's latest row is revoked with `NewLogin` **and the profile is not blocked**. A blocked profile never reports 1002: the block's own sessions answer `SURAKSHA_1001`, and a session replaced earlier answers the plain 401 while the block lasts (as before).
- `OnTokenValidated` puts the code in `HttpContext.Items`; the bare-401 middleware writes it, through one message map for the two public codes, only for a 401.
- `RotateAsync`: a used (rotated) token is reuse only if the family still had a live token (the revoking update is the decision). If nothing was live and the family's latest row is `NewLogin`, the answer is `Invalid` + `NewLogin` = replaced; nothing is changed (the reuse stamp and the reason are not written). `RefreshAsync` turns `NewLogin` into 1002, unless the profile is blocked. Expired-then-replaced is replaced too (the row was revoked with `NewLogin`); expired alone stays `TOKEN_EXPIRED`.
- Unchanged: logout, MPIN change/reset, expiry alone, genuine reuse, unknown token (no code). Admin Portal and staff authentication use other schemes and tables.
- `ApiErrorCodes.PublicCodes = { SURAKSHA_1001, SURAKSHA_1002 }`; any other code is still dropped; the optional `code` is omitted when null.

## Concurrency (reasoned, see OQ-049 for what was run)
- Login vs login: the second waits on the profile row lock, then revokes the first's committed row. One live session, whichever commits last.
- Login vs refresh: rotation first - the login's update then revokes the new rotated row; login first - the rotation claim matches nothing and answers replaced. A refresh response issued just before the login commits is dead on its next request.
- Login vs block: unchanged (ADR-024).

## Flutter handling (app responsibility)
Identify the **`code`**, not the HTTP status (401 on an access token, 403 on refresh). On `SURAKSHA_1002`: stop retrying and refreshing, clear the access and refresh tokens and any cached session data, show the message, and send the user to login. On `SURAKSHA_1001` keep the ADR-024 handling (clear local data, PIS cell notice). Any other 401 keeps the existing refresh-then-login behaviour.

## Consequences / limitations
- A session replaced while its refresh token had already expired reports 1002 afterwards.
- "Latest" is commit order at the profile row lock, not request order.
- The MySQL integration tests were written but not run in the session that built this (no `NSG_TEST_MYSQL`).
