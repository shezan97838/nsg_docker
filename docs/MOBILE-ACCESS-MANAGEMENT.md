# Mobile access management (PIS Admin block / restore, `SURAKSHA_1001`)

Decision record: `docs/architecture/ADR-024-mobile-access-block.md`. Implemented 2026-11-06.

## Who and what
A **PIS Admin** (Admin Portal, role `PIS_ADMIN`) can look a person up by NSG number, block their mobile access, and restore it. Blocking ends every session
at once and the person cannot log in until access is restored. The mobile endpoints answer a blocked person with the code **`SURAKSHA_1001`**, which the
app uses to clear its local session and cached data.

Authentication: Admin Portal access token (`AdminBearer`), policy `RequirePisAdminPortal`. A mobile token, a staff Bearer token (even with the `PisAdmin` role),
any other admin role (for example `SUPER_ADMIN`) and an anonymous caller are refused (401/403). The acting admin comes from the token only.

## Admin API

### `GET /api/v1/admin/mobile-access/{nsgNumber}`
Current status; nothing sensitive (no MPIN hash, date of birth, Aadhaar, PAN, PRAN or salary account; the mobile number is masked).

```json
{
  "success": true,
  "message": "Mobile access status retrieved successfully.",
  "data": {
    "nsgNumber": "NSG-1001", "fullName": "Test Person", "rank": "Inspector", "unit": "Unit One",
    "maskedMobileNumber": "XXXXXX6799",
    "pisRecordExists": true, "mobileProfileExists": true,
    "status": "ACTIVE", "isActive": true,
    "lastLoginAt": "2026-11-06T14:02:11.123456+05:30",
    "isLockedOut": false, "hasActiveSession": true
  }
}
```
`status`: `ACTIVE`, `ADMIN_BLOCKED` (also returns `accessBlockedAt`, `accessBlockedBy`, `accessBlockReason`), `INACTIVE` (inactive for another reason; not
restorable here), `NO_MOBILE_PROFILE` (person is in PIS but has not registered; 200 with "Personnel record found, but no mobile profile has been created for
this NSG number."). Unknown NSG number: 404 "No personnel record or mobile profile was found for this NSG number." Blank / over 30 characters: 400.

### `PUT /api/v1/admin/mobile-access/{nsgNumber}/block`
Body optional: `{ "reason": "Device reported lost" }` (at most 500 characters, else 400 "Reason must not exceed 500 characters.").

```json
{ "success": true, "message": "Mobile access blocked successfully. All active sessions have been terminated.",
  "data": { "nsgNumber": "NSG-1001", "status": "ADMIN_BLOCKED", "isActive": false, "hasActiveSession": false,
            "accessBlockedAt": "2026-11-06T14:05:00.000000+05:30", "accessBlockedBy": 3, "accessBlockReason": "Device reported lost", "...": "..." } }
```
Already blocked: 200 "Mobile access is already blocked." (nothing changes, no new audit entry). Inactive for another reason: 409 "Mobile access is already
disabled for another reason, so it cannot be blocked here." No mobile profile: 404 "A personnel record exists for this NSG number, but the person has not
registered in the mobile app, so there is no mobile access to change."

### `PUT /api/v1/admin/mobile-access/{nsgNumber}/restore`
Body optional: `{ "reason": "Blocked by mistake" }`. 200 "Mobile access restored successfully. The user must log in again." (status `ACTIVE`, `hasActiveSession`
false). Already active: 200 "Mobile access is already active." Inactive but not blocked through this API: 409 "Mobile access was not blocked by a PIS Admin, so it
cannot be restored here." Old sessions are never revived.

## What the app receives when blocked

| Flow | HTTP | Body |
|---|---|---|
| Any authenticated request with an existing access token | 401 | `{ "success": false, "message": "Your mobile access has been blocked by the PIS Admin. Please contact the PIS cell.", "data": null, "code": "SURAKSHA_1001" }` |
| `POST /api/v1/mobile-auth/token/refresh` | 403 | same body |
| `POST /api/v1/mobile-auth/login` | 403 | same body; no tokens, no session, no failed-attempt count |
| `POST /api/v1/mobile-auth/login/check` | 200 | `{ "success": true, "message": "<same message>", "data": { "status": "ACCESS_UNAVAILABLE" }, "code": "SURAKSHA_1001" }` |

The `code` field exists **only** in these responses; every other response is unchanged. Key on `code`, not on the HTTP status.

### Flutter contract
On `code == "SURAKSHA_1001"` the app must: clear the access and refresh tokens; clear securely stored credentials and session data; clear locally cached
personnel data and any user-specific cache; stop all further authenticated calls (including queued/in-flight ones); log out and go to the login screen; show the
message. **Backend responsibility:** revoke sessions, refuse tokens, refuse login and refresh, return the code consistently. **App responsibility:** the actual
clearing. The server cannot reach into a phone: a phone that is offline, or never calls the API again, keeps its data until it next contacts the server. The
code is *not* returned for logout, token expiry, invalid or reused tokens, MPIN change/reset, a replacing login, lockout, or an account that is inactive for another reason.

## Behaviour summary
- Block: `is_active = 0`, `access_blocked_at_ist/by/reason` set, **all** live refresh tokens revoked with reason `AdminBlocked`, one transaction.
- Existing access token: rejected on its next request (the session check runs on every request; it does not wait for the 15-minute expiry).
- Restore: `is_active = 1`, block columns cleared; tokens untouched (still revoked). The person logs in again with the same MPIN. A lockout from failed attempts, if any, is unchanged.
- Audit: `MobileAccessBlocked` / `MobileAccessRestored` in `audit_logs` (admin id, profile id, NSG number, reason, IST time, result, correlation id, IP). An audit failure is logged and never undoes the change.

## Tests
`MobileAccessAdminServiceTests`, `MobileAccessBlockFlowTests`, `MobileAccessEnvelopeTests` (no database) and `MobileAccessDatabaseTests` (real MySQL through the real pipeline, including
the login/block and refresh/block races). The pinned schema (`ExpectedDatabaseSchema`) lists the three columns.

## Known limits
See OQ-048 in `docs/OPEN-QUESTIONS.md`.
