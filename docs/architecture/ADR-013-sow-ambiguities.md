# ADR-013: SOW Ambiguities

**Status**: Open  
**Date**: 2025-08-01  
**SOW Reference**: Multiple sections

## Context
The SOW contains ambiguities and contradictions that cannot be silently resolved. Each is recorded here with the current architectural approach.

## Ambiguities

### 1. Salary Archive Duration
- **§3.3**: "Minimum 7-year archive accessible through the app"
- **§6 Compliance Matrix**: "Payroll API integration; 24-month archive"
- **Approach**: Configurable `SalaryArchiveMonths` setting. Default not hard-coded.
- **Action Required**: Client must confirm intended retention period.

### 2. OAuth 2.0 vs JWT
- **§3.8 / §4**: "JWT/OAuth 2.0 based authentication"
- **Issue**: No OAuth authorization server specified
- **Approach**: JWT with refresh tokens. No full OAuth AS unless directed.
- **Action Required**: Confirm if JWT + refresh tokens suffices.

### 3. MFA Login Sequence
- **§3.8**: "Password + OTP + Biometric"
- **§3.7 Step 3**: Password set → Biometric setup → Device binding
- **Issue**: Does every login require all three factors? Or is biometric an alternative to OTP?
- **Approach**: Configurable authentication state machine. Currently: Password + device binding on every login; OTP configurable.
- **Action Required**: Confirm exact per-login MFA requirements.

### 4. Biometric Server Verification
- **§3.7**: "Biometric setup ... is initiated on the device"
- **Issue**: Server-side biometric data storage/verification not specified
- **Approach**: Device-only biometric, server receives cryptographic proof. See ADR-007.
- **Action Required**: Confirm biometric stays on-device.

### 5. Device Binding Mechanism
- **§3.7 Step 3**: "IMEI + device token"
- **Issue**: IMEI restricted on modern mobile OS
- **Approach**: Platform-safe identifier + hash. See ADR-006.
- **Action Required**: Confirm acceptable identification method.

### 6. Role Definitions
- **Mentioned in SOW**: IT Cell, Nodal Officer, Reviewing Officer, Admin
- **Issue**: No formal RBAC matrix provided
- **Approach**: Minimal set: Employee, ITAdmin, NodalOfficer, SystemAdmin. Extensible.
- **Action Required**: Confirm roles and permission mapping per module.

### 7. HRMS/Payroll/NIC SMS API Contracts
- **Issue**: Not provided in SOW
- **Approach**: Clean interfaces with placeholder implementations. See ADR-011.
- **Action Required**: Provide API documentation for each external system.

### 8. Grievance Routing Rules
- **§3.8**: "Routing to designated Nodal Officer or HQ NSG"
- **Issue**: Exact routing logic not specified
- **Approach**: Configurable routing with Nodal Officer assignment
- **Action Required**: Define routing rules by category/location.
