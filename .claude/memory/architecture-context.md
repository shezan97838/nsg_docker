# Architecture Context

## Layer Map

| Layer | Project | Key Files |
|-------|---------|-----------|
| Domain | `NSG.Mobile.Domain` | Entities/{User,Device,RefreshToken,AuditLog,OtpRequest}.cs, Enums/*.cs, Constants/DomainConstants.cs, Exceptions/DomainExceptions.cs |
| Application | `NSG.Mobile.Application` | Authentication/{DTOs,Interfaces,Services,Validators,Mappings}/*, Common/{Result,ApiErrorCodes,PaginatedResponse,MobileNumberNormalizer}.cs, Interfaces/{Repositories,Services,Infrastructure}/* |
| Infrastructure | `NSG.Mobile.Infrastructure` | Persistence/{AppDbContext,Configurations/*}.cs, Repositories/*.cs, Security/{JwtTokenService,PasswordHashingService,OtpService,AesEncryptionService}.cs, Services/*.cs, External/*.cs, Options/*.cs |
| API | `NSG.Mobile.API` | Controllers/{Auth,Onboarding,Profile,Device}Controller.cs, Middleware/*.cs, Program.cs |

## DI Registration
- `AddApplication()` → MobileAuthService, OnboardingService, FluentValidation validators
- `AddInfrastructure(config)` → DbContext, JWT, Redis, Repositories, Security services, External placeholders

## Database Entities
| Entity | PK | Key Fields |
|--------|----|-----------|
| User | int Id | Name, Rank, ForceNumber, MobileNumber, PasswordHash, Status, Role, RowVersion |
| Device | int Id | UserId, Platform, DeviceIdentifierHash, Status, PushToken |
| RefreshToken | int Id | UserId, DeviceId, TokenHash, FamilyId, ExpiresAt, RevokedAt |
| AuditLog | long Id | UserId, Action, EntityType, EntityId, Result, IpAddress, CorrelationId |
| OtpRequest | int Id | ReferenceId, MobileNumber, OtpHash, Purpose, AttemptCount, ExpiresAt |

## Configuration Sections
Jwt, Security, Redis, Encryption, Sms, Fcm, Hrms, Payroll, FileStorage

## External Dependencies (All Placeholder)
NIC SMS Gateway, FCM/APNs, HRMS API, Payroll API
