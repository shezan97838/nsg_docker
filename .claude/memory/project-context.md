# Project Context

## Identity
- **Project**: NSG Mobile Application API
- **Client**: National Security Guard (NSG), IT Cell
- **SOW Version**: 2.0, August 2025
- **Architecture**: ASP.NET Core 8 Web API, Clean Architecture
- **Tenancy**: Single-tenant (NSG only)
- **Scale**: 10,000+ users, Android + iOS
- **Current Phase**: Foundation (auth, onboarding, profile, device)

## Solution
- `NSG.Mobile.API.sln` at `d:\Projects\2026\NSG\`
- 4 projects: Domain, Application, Infrastructure, API
- Build status: ✅ 0 warnings, 0 errors
- Git: Not initialized

## Current State
Foundation layer complete:
- Auth endpoints (register, verify-otp, resend-otp, login, refresh, logout)
- Onboarding endpoints (pending list, approve, reject)
- Profile endpoint (current user)
- Device endpoint (current device)
- Security infrastructure (JWT, password hashing, OTP, encryption, device binding)
- Middleware pipeline (correlation, security headers, exception handling)

## Not Yet Implemented
- EF Core initial migration
- Password setup flow (SOW §3.7 Step 3)
- Leave Management (SOW §3.2): IMPLEMENTED 2026-10-26, see docs/LEAVE-MANAGEMENT.md (migration applied to local nsg_mobile_dev only; not functionally tested on MySQL)
- Salary Integration (SOW §3.3)
- Form-16 (SOW §3.4)
- Engagement & Communications (SOW §3.5)
- Digital Library (SOW §3.6)
- Grievance Management (SOW §3.8)
- Automated tests (no test projects)
- Git repository initialization
- Production deployment configuration
