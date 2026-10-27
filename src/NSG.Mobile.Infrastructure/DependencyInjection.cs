using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NSG.Mobile.Application.Authentication.Interfaces;
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
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.Configure<OtpOptions>(configuration.GetSection(OtpOptions.SectionName));
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
        services.Configure<BlindIndexOptions>(configuration.GetSection(BlindIndexOptions.SectionName));

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

                    bool valid = Guid.TryParse(principal.FindFirst(DomainConstants.MobileClaims.SessionId)?.Value, out var sessionId)
                        && long.TryParse(principal.FindFirst(DomainConstants.MobileClaims.ProfileId)?.Value, out var profileId)
                        && await context.HttpContext.RequestServices.GetRequiredService<IMobileRefreshTokenRepository>()
                            .IsSessionActiveAsync(sessionId, profileId, context.HttpContext.RequestAborted);
                    if (!valid) context.Fail("Mobile session is not active.");
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
        services.AddScoped<IBlindIndexService, HmacBlindIndexService>();
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
        services.AddScoped<IStateRepository, StateRepository>();
        services.AddScoped<ICityRepository, CityRepository>();
        services.AddScoped<IMobileUserProfileRepository, MobileUserProfileRepository>();
        services.AddScoped<IApproveMobileUserRepository, ApproveMobileUserRepository>();
        services.AddScoped<IMobileAuthGrantRepository, MobileAuthGrantRepository>();
        services.AddScoped<IMobileRefreshTokenRepository, MobileRefreshTokenRepository>();
        services.AddScoped<IAdminProfileRepository, AdminProfileRepository>();
        services.AddScoped<IAdminRoleRepository, AdminRoleRepository>();
        services.AddScoped<IAdminRefreshTokenRepository, AdminRefreshTokenRepository>();
        services.AddScoped<IGrievanceRepository, GrievanceRepository>();
        services.AddScoped<IDgMessageRepository, DgMessageRepository>();
        services.AddScoped<ILeaveRepository, LeaveRepository>();
    }

    private static void AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IReviewerIdentityResolver, ReviewerIdentityResolver>();
        services.AddScoped<IFileStorageService, LocalFileStorageService>();
    }

    private static void AddExternalIntegrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.SectionName));
        services.Configure<FcmOptions>(configuration.GetSection(FcmOptions.SectionName));
        services.Configure<HrmsOptions>(configuration.GetSection(HrmsOptions.SectionName));
        services.Configure<PayrollOptions>(configuration.GetSection(PayrollOptions.SectionName));
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddR2StorageWhenConfigured(configuration);

        // Placeholder implementations — replace with actual integrations when API contracts are available
        services.AddScoped<INicSmsService, NicSmsServicePlaceholder>();
        services.AddScoped<IPushNotificationService, FcmPushNotificationServicePlaceholder>();
        services.AddScoped<IHrmsService, HrmsServicePlaceholder>();
        services.AddScoped<IPayrollService, PayrollServicePlaceholder>();
    }

    /// <summary>
    /// Switches <see cref="IFileStorageService"/> from the local filesystem to private Cloudflare R2 when <c>R2Storage:ServiceUrl</c> holds a real
    /// value. When R2 is chosen, a missing bucket or credential stops the application at startup with a message naming the missing settings.
    /// With no real service URL the local storage stays in place and nothing else is validated.
    /// </summary>
    private static void AddR2StorageWhenConfigured(this IServiceCollection services, IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(R2Options.SectionName);
        R2Options r2 = section.Get<R2Options>() ?? new R2Options();
        // Grievance documents may only be read or written when storage is private R2, or when running in Development.
        bool r2Active = r2.IsEnabled;
        services.AddSingleton<IStorageAvailability>(sp => new StorageAvailability(r2Active || sp.GetRequiredService<IHostEnvironment>().IsDevelopment()));
        if (!r2Active) return;

        r2.EnsureValid();
        services.Configure<R2Options>(section);

        // R2 specifics: path-style addressing, region "auto", and no SDK-added checksums (R2 rejects the newer default trailers).
        services.TryAddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            new BasicAWSCredentials(r2.AccessKey, r2.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = r2.ServiceUrl,
                ForcePathStyle = true,
                AuthenticationRegion = "auto",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            }));
        services.Replace(ServiceDescriptor.Scoped<IFileStorageService, R2FileStorageService>());
    }
}
