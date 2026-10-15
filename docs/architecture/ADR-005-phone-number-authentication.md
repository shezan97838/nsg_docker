# ADR-005: Phone Number Authentication

**Status**: Accepted  
**Date**: 2025-08-01  
**SOW Reference**: §3.7 — Registration with Mobile Number, OTP verification

## Context
The SOW specifies mobile number as the registration and login identifier, with OTP verification via NIC SMS gateway.

## Decision
- Mobile number is the user's login identifier
- Numbers normalized to E.164 format (+91XXXXXXXXXX) for consistent storage and lookup
- Unique index on normalized mobile number prevents duplicate registration
- OTP hashed with SHA-256 before storage
- OTP is purpose-specific (Registration, Login, DeviceRegistration, etc.) — prevents cross-purpose reuse
- OTP expiration: 5 minutes (configurable)
- Max verification attempts: 5 (configurable)
- Resend cooldown: 60 seconds (configurable)
- OTP never appears in: logs, API responses, audit logs

## Consequences
- Requires reliable SMS delivery (NIC gateway dependency)
- Mobile number normalization handles common input variations
- Purpose isolation prevents OTP replay across different flows
