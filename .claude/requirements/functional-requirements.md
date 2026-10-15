# Functional Requirements (from SOW)

## Implemented (Foundation)

### FR-001: User Registration (SOW §3.7 Step 1)
User submits Name, Rank, Force Number, Mobile Number → OTP sent via NIC SMS → OTP verified.
- **API**: POST `/api/v1/mobile-auth/registration/submit`, POST `/api/v1/mobile-auth/otp/verify`, POST `/api/v1/mobile-auth/otp/send`
- **Entities**: User, OtpRequest

### FR-002: IT Cell Approval (SOW §3.7 Step 2)
IT Cell verifies identity (against HRMS), approves or rejects. User notified upon approval.
- **API**: GET `/api/v1/onboarding/pending`, POST `/api/v1/onboarding/approve/{id}`, POST `/api/v1/onboarding/reject/{id}`
- **Note**: HRMS verification is placeholder. Push notification on approval is TODO.

### FR-003: Login with MFA (SOW §3.8)
Password + OTP + Biometric verification. Single device binding.
- **API**: POST `/api/v1/mobile-auth/login`
- **Note**: Biometric is device-side. OTP during login not fully wired (configurable).

### FR-004: Session Management (SOW §3.8)
JWT access tokens, refresh token rotation, configurable timeout, secure logout.
- **API**: POST `/api/v1/mobile-auth/token/refresh`, POST `/api/v1/mobile-auth/logout`

### FR-005: User Profile (SOW §3.1)
Display employee personal details (read-only).
- **API**: GET `/api/v1/profile`

### FR-006: Device Information (SOW §3.8)
Current device status and information.
- **API**: GET `/api/v1/devices/current`

## Not Implemented (Phase 2)

### FR-007: Password Setup (SOW §3.7 Step 3)
After IT approval, user sets password and configures biometric.

### FR-008: PIS Change Requests (SOW §3.1)
User requests changes to personal info, requires approval.

### FR-009: Leave Management (SOW §3.2)
View balance, transaction history, monthly updates, admin bulk upload.

### FR-010: Salary Integration (SOW §3.3)
Monthly salary breakdown, salary slip viewing, archive access.

### FR-011: Form-16 (SOW §3.4)
Tax document viewing/download, 7-year archive.

### FR-012: Engagement & Communications (SOW §3.5)
Push notifications, newsletters, events calendar, polls, feedback.

### FR-013: Digital Library (SOW §3.6)
Document repository, search, offline access, admin upload, versioning.

### FR-014: Grievance Management (SOW §3.8)
Category-based submission, document upload, routing, status tracking, officer dashboard.
