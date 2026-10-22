using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NSG.Mobile.Application.Admin.Services;
using NSG.Mobile.Application.EducationQualifications.Services;
using NSG.Mobile.Application.Geography.Services;
using NSG.Mobile.Application.Grievances.Services;
using NSG.Mobile.Application.DgMessages.Services;
using NSG.Mobile.Application.Lookups.Services;
using NSG.Mobile.Application.MobileAuth.Services;
using NSG.Mobile.Application.Onboarding.Services;
using NSG.Mobile.Application.Profile.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSG.Mobile.Application.Pis.Services;
using NSG.Mobile.Application.Trades.Services;
using NSG.Mobile.Application.Ranks.Services;

namespace NSG.Mobile.Application;

/// <summary>
/// Application layer DI registration.
/// Registers application services, validators, and FluentValidation.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Application services
        services.AddScoped<IOnboardingService, OnboardingService>();
        services.AddScoped<IRankHierarchyService, RankHierarchyService>();
        services.AddScoped<INsgUnitService, NsgUnitService>();
        services.AddScoped<IParentDepartmentRankService, ParentDepartmentRankService>();
        services.AddScoped<IReligionService, ReligionService>();
        services.AddScoped<IEducationQualificationService, EducationQualificationService>();
        services.AddScoped<INsgTradeService, NsgTradeService>();
        services.AddScoped<IPdTradeService, PdTradeService>();
        services.AddScoped<IUserPisService, UserPisService>();
        services.AddScoped<IStateService, StateService>();
        services.AddScoped<ICityService, CityService>();
        services.AddScoped<IMobileAuthService, MobileAuthService>();
        services.AddScoped<IMobileApprovalService, MobileApprovalService>();
        services.AddScoped<IMobileProfileService, MobileProfileService>();
        services.AddScoped<IAdminAccountService, AdminAccountService>();
        services.AddScoped<IAdminActivationService, AdminActivationService>();
        services.AddScoped<IAdminAuthService, AdminAuthService>();
        services.AddScoped<IAdminBootstrapService, AdminBootstrapService>();
        services.AddScoped<GrievanceDocumentAccess>();
        services.AddScoped<IGrievanceService, GrievanceService>();
        services.AddScoped<IGrievanceAdminService, GrievanceAdminService>();
        services.AddScoped<IGrievanceCategoryService, GrievanceCategoryService>();
        services.AddScoped<IGrievanceDashboardService, GrievanceDashboardService>();
        services.AddScoped<IDgMessageService, DgMessageService>();
        services.TryAddSingleton(TimeProvider.System);

        // FluentValidation — auto-discover all validators in this assembly
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
