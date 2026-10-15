# ADR-007: Biometric Authentication Boundary

**Status**: Accepted  
**Date**: 2025-08-01  
**SOW Reference**: §3.7 Step 3 — "Biometric setup (fingerprint or Face ID) is initiated on the device", §3.8 — "MFA: Password + OTP + Biometric"

## Context
The SOW requires biometric verification but does not specify server-side biometric data processing.

## Decision
- Biometric authentication is handled **entirely on the mobile device** using the platform's secure biometric mechanism (Android BiometricPrompt / iOS LocalAuthentication)
- The API **does NOT** receive raw fingerprint or face data
- The API receives a **device-bound cryptographic proof** or a **signed assertion** that biometric verification succeeded on the device
- No biometric templates or raw biometric data stored in the API database
- If advanced device attestation (SafetyNet/Play Integrity / App Attest) is supported and approved, it can be layered in as an additional verification

## Rationale
- Storing raw biometric data server-side creates extreme security and privacy liability
- Platform secure enclaves are purpose-built for biometric protection
- This approach aligns with industry best practices (FIDO2/WebAuthn concepts)

## Consequences
- Biometric verification depends on device integrity
- Server trusts the device's biometric assertion (acceptable for this threat model)
- If stronger server-side verification is required, FIDO2/WebAuthn can be integrated
