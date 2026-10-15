using System.Threading.RateLimiting;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using NSG.Mobile.API.Middleware;
using NSG.Mobile.API.Security;
using NSG.Mobile.Application;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Infrastructure;
using Serilog;

// ──────────────────────────────────────────────
// NSG Mobile Application — ASP.NET Core 8 Web API
// SOW Version: 2.0 | August 2025 | IT Cell, NSG
// ──────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// ── Serilog Structured Logging ──
builder.Host.UseSerilog((context, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "NSG.Mobile.API")
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}")
        .WriteTo.File("logs/nsg-api-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));

// ── Secrets: refuse to start with a missing, placeholder or weak JWT / encryption / blind-index key ──
NSG.Mobile.Infrastructure.Security.StartupSecretsValidator.EnsureValid(builder.Configuration);

// ── OTP mode guard: the fixed demo OTP can never start in Production (or any unapproved environment) ──
NSG.Mobile.Infrastructure.Security.OtpModeGuard.EnsureAllowed(
    builder.Configuration.GetSection(NSG.Mobile.Infrastructure.Options.OtpOptions.SectionName).Get<NSG.Mobile.Infrastructure.Options.OtpOptions>() ?? new NSG.Mobile.Infrastructure.Options.OtpOptions(),
    builder.Environment.EnvironmentName);

NSG.Mobile.Infrastructure.Security.OtpModeGuard.EnsureDemoDatabase(
    builder.Configuration.GetSection(NSG.Mobile.Infrastructure.Options.OtpOptions.SectionName).Get<NSG.Mobile.Infrastructure.Options.OtpOptions>() ?? new NSG.Mobile.Infrastructure.Options.OtpOptions(),
    builder.Environment.EnvironmentName,
    builder.Configuration.GetConnectionString("MySql"));

// ── File storage: outside Development the root must be an absolute path set by the environment (never a committed Windows path) ──
if (!builder.Environment.IsDevelopment())
{
    string? fileStorageRoot = builder.Configuration["FileStorage:BasePath"];
    if (string.IsNullOrWhiteSpace(fileStorageRoot) || !Path.IsPathRooted(fileStorageRoot))
        throw new InvalidOperationException("FileStorage:BasePath must be an absolute path for this platform (set the FileStorage__BasePath environment variable, for example /var/lib/nsg/files).");
}

// ── Forwarded headers: trusted only from explicitly listed reverse proxies; otherwise never ──
var forwardedHeaders = ForwardedHeadersSetup.Build(
    builder.Configuration.GetSection(TrustedProxySettings.SectionName).Get<TrustedProxySettings>() ?? new TrustedProxySettings());

// ── Application Layer DI ──
builder.Services.AddApplication();

// ── Infrastructure Layer DI (DB, JWT, Redis, Repos, Security, External) ──
builder.Services.AddInfrastructure(builder.Configuration);

// ── HTTP Context Accessor (required by CurrentUserService) ──
builder.Services.AddHttpContextAccessor();

// ── Controllers ──
builder.Services.AddControllers(options =>
{
    // Require HTTPS in production
    if (!builder.Environment.IsDevelopment())
    {
        options.Filters.Add(new RequireHttpsAttribute());
    }
})
.ConfigureApiBehaviorOptions(options =>
{
    // Custom validation error response using ProblemDetails
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        var problemDetails = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation Failed",
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Instance = context.HttpContext.Request.Path
        };

        return new BadRequestObjectResult(problemDetails);
    };
});

// ── FluentValidation auto-validation ──
builder.Services.AddFluentValidationAutoValidation();

// ── Authorization Policies ──
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(DomainConstants.Policies.RequireITAdmin, policy =>
        policy.RequireRole(DomainConstants.Roles.ITAdmin, DomainConstants.Roles.SystemAdmin));

    options.AddPolicy(DomainConstants.Policies.RequireNodalOfficer, policy =>
        policy.RequireRole(DomainConstants.Roles.NodalOfficer, DomainConstants.Roles.SystemAdmin));

    options.AddPolicy(DomainConstants.Policies.RequireSystemAdmin, policy =>
        policy.RequireRole(DomainConstants.Roles.SystemAdmin));

    // Mobile-app users: only a token issued by mobile-auth/login carries this claim.
    options.AddPolicy(DomainConstants.Policies.RequireMobileProfile, policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim(DomainConstants.MobileClaims.TokenUse, DomainConstants.MobileClaims.MobileAccess)
              .RequireClaim(DomainConstants.MobileClaims.ProfileId));
});

// ── TEMPORARY: authorization bypass for local development while the token/login flow is built ──
// Off by default. Only honoured in Development; any other environment with the flag set fails fast at startup.
bool authorizationDisabled = builder.Configuration.GetValue<bool>(DevelopmentAuthorizationBypassHandler.SettingName);
if (authorizationDisabled)
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            $"{DevelopmentAuthorizationBypassHandler.SettingName} may only be enabled in the Development environment.");

    builder.Services.AddSingleton<IAuthorizationHandler, DevelopmentAuthorizationBypassHandler>();

    // TEMP-DEV-REVIEWER: with no IT Admin login yet, approval/rejection is recorded as reviewer "AsmarTest". Remove with the bypass.
    builder.Services.Configure<NSG.Mobile.Infrastructure.Services.DevelopmentReviewerOptions>(o => o.Enabled = true);
}

// ── Rate Limiting ──
// Per-client-IP limits of the "auth" (per minute) and "otp" (per 5 minutes) policies. Defaults are the production values; override with
// RateLimiting__AuthPermitLimit / RateLimiting__OtpPermitLimit (for example behind a shared NAT, or in automated tests).
int authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 10);
int otpPermitLimit = builder.Configuration.GetValue("RateLimiting:OtpPermitLimit", 5);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global rate limiter — protect against general abuse
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Strict rate limiter for auth endpoints
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromMinutes(1)
            }));

    // OTP endpoint rate limiter
    options.AddPolicy("otp", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = otpPermitLimit,
                Window = TimeSpan.FromMinutes(5)
            }));
});

// ── Swagger / OpenAPI ──
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NSG Mobile API",
        Version = "v1",
        Description = "National Security Guard Mobile Application API. SOW Version 2.0.",
        Contact = new OpenApiContact { Name = "IT Cell, NSG" }
    });

    // JWT Bearer authentication in Swagger
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT access token"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ── Health Checks ──
builder.Services.AddHealthChecks()
    .AddMySql(
        builder.Configuration.GetConnectionString("MySql") ?? "",
        name: "mysql",
        tags: new[] { "ready" })
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
        tags: new[] { "live" });

// ── CORS ──
builder.Services.AddCors(options =>
{
    options.AddPolicy("MobileApp", policy =>
        policy.AllowAnyOrigin() // TODO: Restrict to actual mobile app origins in production
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// ── HSTS ──
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromDays(365);
        options.IncludeSubDomains = true;
    });
}

var app = builder.Build();

// ── Middleware Pipeline (order matters) ──

if (authorizationDisabled)
    app.Logger.LogWarning("AUTHORIZATION IS DISABLED ({Setting}=true, Development only). Endpoints accept requests without a token. Do not use outside local development.", DevelopmentAuthorizationBypassHandler.SettingName);
if (builder.Configuration.GetValue<NSG.Mobile.Infrastructure.Options.OtpMode>("Otp:Mode") == NSG.Mobile.Infrastructure.Options.OtpMode.Demo)
    app.Logger.LogWarning("DEMO OTP MODE IS ACTIVE (Otp:Mode=Demo). No SMS is sent and the OTP is a fixed code. For approved non-production presentations only.");

// Forwarded headers first, so the scheme and client address seen by HTTPS redirection, rate limiting and logging are the real ones.
if (forwardedHeaders != null) app.UseForwardedHeaders(forwardedHeaders);


// Correlation ID first — ensures all subsequent middleware/handlers have trace context
app.UseMiddleware<CorrelationIdMiddleware>();

// Security headers
app.UseMiddleware<SecurityHeadersMiddleware>();

// Global exception handling
app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

// HTTPS redirection and HSTS
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();

// Swagger — development only by default
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "NSG Mobile API v1");
        options.RoutePrefix = "swagger";
    });
}

// CORS
app.UseCors("MobileApp");

// Rate limiting
app.UseRateLimiter();

// Serilog request logging
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.Items["CorrelationId"]?.ToString());
        diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
    };
});

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Health checks
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Controllers
app.MapControllers();

// ── Run ──
Log.Information("NSG Mobile API starting...");
app.Run();

// Exposes the entry point to WebApplicationFactory in integration tests.
public partial class Program { }
