using MenuPlanner.Application.Menus;
using MenuPlanner.Application.Shopping;
using MenuPlanner.Portioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MenuPlanner.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddMenuPlannerApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<UnitConverter>();
        services.AddSingleton<PortionCalculator>();
        services.AddSingleton<MenuGenerator>();
        services.AddSingleton<ShoppingListBuilder>();
        services.TryAddSingleton(new GenerationOptions());
        services.AddScoped<MenuService>();
        services.AddScoped<DishResolver>();
        services.AddScoped<Suggestions.AvailableIngredients>();
        services.AddScoped<Suggestions.PantryCookability>();
        services.AddScoped<Suggestions.RecipeDraftService>();
        services.AddScoped<Suggestions.SuggestionContextBuilder>();
        services.AddScoped<Imports.ImportService>();
        services.AddScoped<Imports.DishService>();
        services.AddScoped<Seeding.SeedSyncService>();
        return services;
    }
}
