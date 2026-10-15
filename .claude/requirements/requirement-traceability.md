# Requirement Traceability (Foundation)

> Traces implemented requirements: SOW → Architecture → API → Database → Security → Validation

## Registration (SOW §3.7 Step 1)
- **Architecture**: `MobileAuthService.RegisterAsync()`
- **API**: POST `/api/v1/mobile-auth/registration/submit`
- **Database**: User (PendingOtpVerification), OtpRequest
- **Security**: Rate limiting, mobile normalization, duplicate check, OTP hash storage
- **Validation**: `RegisterRequestValidator` (name, rank, force number, Indian mobile format)

## OTP Verification (SOW §3.7 Step 1)
- **Architecture**: `MobileAuthService.VerifyOtpAsync()`, `OtpService.VerifyAsync()`
- **API**: POST `/api/v1/mobile-auth/otp/verify`
- **Database**: OtpRequest (AttemptCount, IsVerified, VerifiedAt)
- **Security**: Hash comparison, purpose isolation, attempt limits, expiry
- **Validation**: `VerifyOtpRequestValidator` (reference ID, 6-digit code)

## IT Approval (SOW §3.7 Step 2)
- **Architecture**: `OnboardingService.ApproveRegistrationAsync()`
- **API**: POST `/api/v1/onboarding/approve/{id}` (RequireITAdmin)
- **Database**: User (Status → Active, ApprovedBy, ApprovedAt)
- **Security**: ITAdmin policy authorization, audit logging
- **Validation**: User state check (must be PendingITApproval)

## Login (SOW §3.8)
- **Architecture**: `MobileAuthService.LoginAsync()`
- **API**: POST `/api/v1/mobile-auth/login`
- **Database**: User (FailedLoginAttempts, LastLoginAt), Device, RefreshToken
- **Security**: Password verify, lockout, device binding, token generation, audit
- **Validation**: `LoginRequestValidator` (mobile, password, device info)

## Token Refresh (SOW §3.8)
- **Architecture**: `MobileAuthService.RefreshTokenAsync()`
- **API**: POST `/api/v1/mobile-auth/token/refresh`
- **Database**: RefreshToken (rotation chain, FamilyId)
- **Security**: Hash lookup, reuse detection, family revocation, device verify, audit
- **Validation**: `RefreshTokenRequestValidator` (token not empty)

## Logout (SOW §3.8)
- **Architecture**: `MobileAuthService.LogoutAsync()`
- **API**: POST `/api/v1/mobile-auth/logout` (Authorize)
- **Database**: RefreshToken (RevokedAt, RevokeReason)
- **Security**: Token revocation, audit logging
- **Validation**: `LogoutRequestValidator` (token not empty)

## Profile (SOW §3.1)
- **Architecture**: `ProfileController` → `IUserRepository`
- **API**: GET `/api/v1/profile` (Authorize)
- **Database**: User (read-only via `GetByIdAsync`)
- **Security**: User ID from JWT claims (IDOR protection), DTO projection
- **Validation**: JWT authentication required
