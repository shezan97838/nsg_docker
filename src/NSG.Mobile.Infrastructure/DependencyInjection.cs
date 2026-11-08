using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NSG.Mobile.Application.Authentication.Interfaces;
using NSG.Mobile.Application.Banners;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;
using NSG.Mobile.Infrastructure.External;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Repositories;
using NSG.Mobile.Infrastructure.Security;
using NSG.Mobile.Infrastructure.Services;
using NSG.Mobile.Infrastructure.Services.Storage;

namespace NSG.Mobile.Infrastructure;

/// <summary>
/// Infrastructure layer DI registration.
/// Registers DbContext, repositories, security services, external integrations, and cache.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDatabase(configuration);
        services.AddAuthenticationInfrastructure(configuration);
        services.AddCaching(configuration);
        services.AddRepositories();
        services.AddInfrastructureServices();
        services.AddExternalIntegrations(configuration);

        return services;
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MySql");
        ServerVersion serverVersion;
        try
        {
            serverVersion = ServerVersion.AutoDetect(connectionString);
        }
        catch
        {
            serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(
                connectionString,
                serverVersion,
                my =>
                {
                    my.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    my.EnableRetryOnFailure(3);
                }));
    }

    private static void AddAuthenticationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind options
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<PublicApiOptions>(configuration.GetSection(PublicApiOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));

        // Mobile registration / login tunables: lockout defaults come from Security, MobileAuth overrides them.
        var securityOptions = new SecurityOptions();
        configuration.GetSection(SecurityOptions.SectionName).Bind(securityOptions);
        var mobileAuthSettings = new MobileAuthSettings
        {
            MaxFailedAttempts = securityOptions.MaxFailedLoginAttempts,
            LockoutMinutes = securityOptions.LockoutDurationMinutes,
            RefreshTokenDays = configuration.GetSection(JwtOptions.SectionName).GetValue("RefreshTokenExpirationDays", 7)
        };
        configuration.GetSection("MobileAuth").Bind(mobileAuthSettings);
        services.AddSingleton(mobileAuthSettings);

        // Salary slip module limits (SalarySlips section; defaults in SalarySlipSettings).
        var salarySlipSettings = new NSG.Mobile.Application.SalarySlips.SalarySlipSettings();
        configuration.GetSection("SalarySlips").Bind(salarySlipSettings);
        services.AddSingleton(salarySlipSettings);

        // Form 16 module limits (Form16Storage section; defaults in Form16Settings).
        var form16Settings = new NSG.Mobile.Application.Form16.Form16Settings();
        configuration.GetSection("Form16Storage").Bind(form16Settings);   // limits only (MaxPdfSizeBytes); the location is Storage:RootPath
        services.AddSingleton(form16Settings);

        // Digital Library limits (DigitalLibrary section; defaults in DigitalLibrarySettings). Limits only: the location is Storage:RootPath.
        var digitalLibrarySettings = new NSG.Mobile.Application.DigitalLibrary.DigitalLibrarySettings();
        configuration.GetSection("DigitalLibrary").Bind(digitalLibrarySettings);
        services.AddSingleton(digitalLibrarySettings);

        // Leave module limits (Leave section; defaults in LeaveSettings).
        var leaveSettings = new NSG.Mobile.Application.Leave.LeaveSettings();
        configuration.GetSection("Leave").Bind(leaveSettings);
        services.AddSingleton(leaveSettings);

        // JWT Authentication
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var secret = jwtSection.GetValue<string>("Secret") ?? throw new InvalidOperationException("JWT Secret is not configured.");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSection.GetValue<string>("Issuer"),
                ValidAudience = jwtSection.GetValue<string>("Audience"),
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ClockSkew = TimeSpan.FromSeconds(30),
                // Reject tokens using unexpected algorithms
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
            };

            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    // A mobile access token is only as good as its session: logout, a changed or reset MPIN, a new login or a
                    // deactivated profile end the session, and the token stops working at once instead of at its expiry.
                    var principal = context.Principal;
                    if (principal?.FindFirst(DomainConstants.MobileClaims.TokenUse)?.Value != DomainConstants.MobileClaims.MobileAccess) return;

                    MobileSessionState state = MobileSessionState.Revoked;
                    if (Guid.TryParse(principal.FindFirst(DomainConstants.MobileClaims.SessionId)?.Value, out var sessionId)
                        && long.TryParse(principal.FindFirst(DomainConstants.MobileClaims.ProfileId)?.Value, out var profileId))
                    {
                        state = await context.HttpContext.RequestServices.GetRequiredService<IMobileRefreshTokenRepository>()
                            .GetSessionStateAsync(sessionId, profileId, context.HttpContext.RequestAborted);
                    }

                    if (state == MobileSessionState.Active) return;

                    // The token is refused either way. Only a PIS Admin block (SURAKSHA_1001) and a replacement by a newer login
                    // (SURAKSHA_1002) leave a code behind for the 401 body; every other reason stays the plain 401 it always was.
                    if (state == MobileSessionState.AdminBlocked)
                        context.HttpContext.Items[DomainConstants.AuthFailure.CodeItemKey] = ApiErrorCodes.MobileAccessBlocked;
                    else if (state == MobileSessionState.Replaced)
                        context.HttpContext.Items[DomainConstants.AuthFailure.CodeItemKey] = ApiErrorCodes.MobileSessionReplaced;
                    context.Fail("Mobile session is not active.");
                },
                OnAuthenticationFailed = context =>
                {
                    // Don't expose authentication failure details
                    return Task.CompletedTask;
                }
            };
        })
        // Admin Portal tokens (ADR-017): a second scheme with its own audience. A mobile token fails here on the audience, and an
        // admin token fails the default scheme above for the same reason, so neither kind is accepted in the other's place.
        .AddJwtBearer(DomainConstants.AdminClaims.Scheme, options =>
        {
            var adminJwt = new JwtOptions();
            jwtSection.Bind(adminJwt);
            if (string.Equals(adminJwt.EffectiveAdminAudience, adminJwt.Audience, StringComparison.Ordinal))
                throw new InvalidOperationException("Jwt:AdminAudience must differ from Jwt:Audience, otherwise admin and mobile tokens would be interchangeable.");

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = adminJwt.Issuer,
                ValidAudience = adminJwt.EffectiveAdminAudience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
            };

            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    // An admin access token is only as good as its session and its account: logout, a new sign-in, deactivation or a
                    // changed role end it at once, not at its expiry. Anything that is not exactly an admin access token is refused.
                    var principal = context.Principal;
                    bool valid = principal?.FindFirst(DomainConstants.AdminClaims.TokenUse)?.Value == DomainConstants.AdminClaims.AdminAccess
                        && Guid.TryParse(principal.FindFirst(DomainConstants.AdminClaims.SessionId)?.Value, out var sessionId)
                        && long.TryParse(principal.FindFirst(DomainConstants.AdminClaims.ProfileId)?.Value, out var adminProfileId)
                        && principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value is { Length: > 0 } roleCode
                        && await context.HttpContext.RequestServices.GetRequiredService<IAdminRefreshTokenRepository>()
                            .IsSessionActiveAsync(sessionId, adminProfileId, roleCode, context.HttpContext.RequestAborted);
                    if (!valid) context.Fail("Admin session is not active.");
                }
            };
        });

        // Security services
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IAdminTokenService, AdminJwtTokenService>();
        services.AddScoped<IPasswordHashingService, PasswordHashingService>();
        services.AddScoped<IEncryptionService, AesEncryptionService>();
    }

    private static void AddCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));

        var redisConnectionString = configuration.GetSection(RedisOptions.SectionName).GetValue<string>("ConnectionString");

        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = configuration.GetSection(RedisOptions.SectionName).GetValue<string>("InstanceName") ?? "NSG:";
            });
        }
        else
        {
            // Fallback to in-memory distributed cache for development
            services.AddDistributedMemoryCache();
        }

        services.AddScoped<ICacheService, RedisCacheService>();
    }

    private static void AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IOtpRequestRepository, OtpRequestRepository>();
        services.AddScoped<IRankHierarchyRepository, RankHierarchyRepository>();
        services.AddScoped<INsgUnitRepository, NsgUnitRepository>();
        services.AddScoped<IParentDepartmentRankRepository, ParentDepartmentRankRepository>();
        services.AddScoped<IReligionRepository, ReligionRepository>();
        services.AddScoped<IEducationQualificationRepository, EducationQualificationRepository>();
        services.AddScoped<INsgTradeRepository, NsgTradeRepository>();
        services.AddScoped<IPdTradeRepository, PdTradeRepository>();
        services.AddScoped<IUserPisRepository, UserPisRepository>();
        services.AddScoped<IPisOwnershipRepository, PisOwnershipRepository>();
        services.AddScoped<IPisImportRepository, PisImportRepository>();
        services.AddScoped<IPisAdminQueryRepository, PisAdminQueryRepository>();
        services.AddScoped<IStateRepository, StateRepository>();
        services.AddScoped<ICityRepository, CityRepository>();
        services.AddScoped<IMobileUserProfileRepository, MobileUserProfileRepository>();
        services.AddScoped<IApproveMobileUserRepository, ApproveMobileUserRepository>();
        services.AddScoped<IMobileAuthGrantRepository, MobileAuthGrantRepository>();
        services.AddScoped<IMobileRefreshTokenRepository, MobileRefreshTokenRepository>();
        services.AddScoped<IMobileAccessRepository, MobileAccessRepository>();
        services.AddScoped<IAdminProfileRepository, AdminProfileRepository>();
        services.AddScoped<IAdminRoleRepository, AdminRoleRepository>();
        services.AddScoped<IAdminRefreshTokenRepository, AdminRefreshTokenRepository>();
        services.AddScoped<IGrievanceRepository, GrievanceRepository>();
        services.AddScoped<IDigitalLibraryRepository, DigitalLibraryRepository>();

        services.AddScoped<IDgMessageRepository, DgMessageRepository>();
        services.AddScoped<IBannerRepository, BannerRepository>();
        services.AddScoped<IBannerImageUrlBuilder, BannerImageUrlBuilder>();
        services.AddScoped<ILeaveRepository, LeaveRepository>();
        services.AddScoped<ISalarySlipRepository, SalarySlipRepository>();
        services.AddScoped<ISalarySlipQueryRepository, SalarySlipQueryRepository>();
        services.AddScoped<ISalarySlipMobileRepository, SalarySlipMobileRepository>();
        services.AddScoped<IForm16Repository, Form16Repository>();
    }

    private static void AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IReviewerIdentityResolver, ReviewerIdentityResolver>();

    }

    private static void AddExternalIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.SectionName));
        services.Configure<FcmOptions>(configuration.GetSection(FcmOptions.SectionName));
        services.Configure<HrmsOptions>(configuration.GetSection(HrmsOptions.SectionName));
        services.Configure<PayrollOptions>(configuration.GetSection(PayrollOptions.SectionName));
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IStorageRoot, StorageRoot>();
        services.AddHostedService<StorageStartupValidator>();
        services.AddFileStorageProvider();

        // Form 16, salary slips and the digital library use local disk under the common storage root (Storage:RootPath).
        services.AddScoped<IForm16StorageService, Form16StorageService>();
        services.AddScoped<IDigitalLibraryStorageService, DigitalLibraryStorageService>();

        // (independent of the IFileStorageService selection above)
        services.AddScoped<ISalarySlipStorageService, SalarySlipStorageService>();
        services.AddScoped<ISalarySlipPdfExtractionService, SalarySlipPdfExtractionService>();
        services.AddScoped<ISalarySlipPdfGenerationService, SalarySlipPdfGenerationService>();

        // Placeholder implementations — replace with actual integrations when API contracts are available
        services.AddScoped<INicSmsService, NicSmsServicePlaceholder>();
        services.AddScoped<IPushNotificationService, FcmPushNotificationServicePlaceholder>();
        services.AddScoped<IHrmsService, HrmsServicePlaceholder>();
        services.AddScoped<IPayrollService, PayrollServicePlaceholder>();
    }

    /// <summary>
    /// Registers <see cref="LocalFileStorageService"/> as the only <see cref="IFileStorageService"/> (grievance documents, DG message images,
    /// banner images). It stores under the common storage root (<c>Storage:RootPath</c>), which <see cref="Services.Storage.StorageStartupValidator"/>
    /// checks at startup in every environment.
    /// </summary>
    private static void AddFileStorageProvider(this IServiceCollection services)
    {
        // The root is validated at startup, so by the time a request arrives storage is usable.
        services.AddSingleton<IStorageAvailability>(new StorageAvailability(true));
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
    }
}
