# Known Limitations

## Infrastructure
- **No EF Core migrations created** — database schema exists only in code, not applied to any database
- **No git repository** — `.git` not initialized
- **Redis optional** — falls back to in-memory distributed cache (not suitable for multi-instance production)
- **CORS wide open** — `AllowAnyOrigin()` with TODO to restrict for production

## External Integrations
- **NIC SMS Gateway** — placeholder that logs OTP to console (MUST disable in production)
- **FCM/APNs** — placeholder that logs notifications
- **HRMS API** — placeholder that returns "found" for all verification
- **Payroll API** — empty placeholder, no methods defined

## Testing
- **No test projects** — no unit tests, no integration tests
- **No automated test coverage** — validation relies on build + manual + review

## Security
- **Dev config has weak secrets** — `appsettings.Development.json` has non-production JWT secret and encryption key
- **Biometric verification not fully implemented** — API receives device proof but doesn't validate it
- **Password setup flow not implemented** — SOW §3.7 Step 3 (user sets password after IT approval)
- **RequireActiveAccount policy defined but not enforced** — policy constant exists but no authorization handler
- **Rate limit policies defined but not applied to controllers** — `auth` and `otp` policies exist but controllers don't use `[EnableRateLimiting]`

## Business Logic
- **Resend OTP creates a new OTP** — returns a new ReferenceId (original becomes stale)
- **VerifyOtp state transition incomplete** — verifies OTP but doesn't update User status from PendingOtpVerification to PendingITApproval in the same operation
- **OnboardingService approval/rejection** — sends no notification to the user (TODO noted)
- **No password set endpoint** — user can login but password set flow after IT approval is not implemented
