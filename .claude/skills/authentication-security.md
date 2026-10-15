# Authentication & Security Skills

## JWT Access Tokens
- Service: `JwtTokenService` implements `ITokenService`
- Algorithm: HMAC-SHA256 (explicitly locked via `ValidAlgorithms`)
- Claims: `sub`, `jti`, `iat`, `role`, `device_id`, `force_number`
- Expiry: 15 min (configurable via `JwtOptions.AccessTokenExpirationMinutes`)
- Config: `JwtOptions` in `Jwt` section

## Refresh Tokens
- 64-byte cryptographically random, stored as SHA-256 hash
- Family-based rotation with `FamilyId`
- Reuse detection: revoked token replayed → entire family revoked
- Device binding verified on every refresh
- Repository: `IRefreshTokenRepository` with `RevokeFamilyAsync()`

## Password Hashing
- Service: `PasswordHashingService` implements `IPasswordHashingService`
- Algorithm: PBKDF2-SHA256, 100K iterations, 16-byte salt, 32-byte hash
- Format: `{iterations}.{salt_base64}.{hash_base64}`
- Constant-time comparison: `CryptographicOperations.FixedTimeEquals`

## OTP
- Service: `OtpService` implements `IOtpService`
- 6-digit cryptographic random, SHA-256 hashed before storage
- Purpose isolation (Registration, Login, DeviceRegistration)
- Max 5 attempts, 5-min expiry, 60s resend cooldown (all configurable)
- Config: `SecurityOptions` in `Security` section

## Device Binding
- Single device per user (enforced in `MobileAuthService.LoginAsync`)
- Device identifier hashed (SHA-256) before storage
- New device → revokes previous device + all its refresh tokens
- Optional public key binding for future challenge-response

## AES-256 Encryption
- Service: `AesEncryptionService` implements `IEncryptionService`
- AES-256-CBC, random IV per operation, IV prepended to ciphertext
- Key from `EncryptionOptions.Key` (Base64-encoded 32-byte key)

## Authorization Policies
| Policy | Roles Allowed |
|--------|--------------|
| RequireITAdmin | ITAdmin, SystemAdmin |
| RequireNodalOfficer | NodalOfficer, SystemAdmin |
| RequireSystemAdmin | SystemAdmin |
