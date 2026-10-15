# NSG Mobile Application — API

Production-grade ASP.NET Core 8 Web API for the National Security Guard Mobile Application.

> **SOW Version**: 2.0 | August 2025 | Prepared By: IT Cell, NSG

## Architecture

Clean Architecture with strict dependency direction:

```
API → Application → Domain
Infrastructure → Application + Domain
```

| Layer | Project | Responsibility |
|-------|---------|----------------|
| Domain | `NSG.Mobile.Domain` | Entities, Enums, Constants, Domain Exceptions |
| Application | `NSG.Mobile.Application` | Interfaces, DTOs, Services, Validators, Mappings |
| Infrastructure | `NSG.Mobile.Infrastructure` | EF Core, MySQL 8.x, Redis, JWT, SMS, FCM |
| API | `NSG.Mobile.API` | Controllers, Middleware, Program.cs, Swagger |

## Quick Start

### Prerequisites
- .NET 8 SDK
- MySQL 8.x (local instance or Docker container)
- Redis (optional — falls back to in-memory cache)

### Run
```bash
cd src/NSG.Mobile.API
dotnet run
```

### Swagger UI
Navigate to `https://localhost:<port>/swagger` in development mode.

### Health Checks
- **Liveness**: `GET /health/live`
- **Readiness**: `GET /health/ready`

## Foundation API Endpoints

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/v1/mobile-auth/login` | Anonymous | MobileAuth: NSG number + mobile + DOB + MPIN login (see `MobileAuthController` for the full flow) |
| GET | `/api/v1/profile` | Mobile Bearer | Basic PIS details of the signed-in mobile user (identity from the token only) |
| GET | `/api/v1/devices/current` | Bearer | Current device info |
| GET | `/api/v1/onboarding/pending` | ITAdmin | Pending registrations |
| POST | `/api/v1/onboarding/approve/{id}` | ITAdmin | Approve registration |
| POST | `/api/v1/onboarding/reject/{id}` | ITAdmin | Reject registration |

## Security Controls

- JWT access tokens (15-minute expiry, HMAC-SHA256)
- Refresh token rotation with reuse detection
- PBKDF2-SHA256 password hashing (100K iterations)
- OTP hash storage with attempt limits and cooldown
- Single device binding per user
- Rate limiting (global + per-endpoint)
- Policy-based authorization
- Security response headers
- Global exception handling (ProblemDetails)
- Correlation ID tracing
- Comprehensive audit logging

## Configuration

See `appsettings.json` for all configuration sections.
**Never commit real secrets to source control.**

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Security](docs/SECURITY.md)
- [Requirement Traceability](docs/REQUIREMENT-TRACEABILITY.md)
- [Open Questions](docs/OPEN-QUESTIONS.md)
- [Architecture Decision Records](docs/architecture/)

## License

Internal NSG use only.
