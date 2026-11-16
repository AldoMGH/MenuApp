using MenuPlanner.Application;
using MenuPlanner.Application.Seeding;
using MenuPlanner.Infrastructure.Mongo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace MenuPlanner.Infrastructure;

public static class DependencyInjection
{
    public const string MemoryProvider = "Memory";

    /// <summary><c>Storage:Provider</c>: <c>Mongo</c> (por defecto) o <c>Memory</c> (sin base de datos; para probar y demos).</summary>
    public static bool UsesMemoryStorage(IConfiguration configuration) =>
        string.Equals(configuration["Storage:Provider"], MemoryProvider, StringComparison.OrdinalIgnoreCase);

    /// <summary>Ruta de una carpeta de datos (<c>seed</c>, <c>schemas</c>): configurada, junto al ejecutable o en la raíz del repo.</summary>
    public static string ResolveDataPath(string? configured, string folder)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var local = Path.Combine(AppContext.BaseDirectory, folder);
        if (Directory.Exists(local))
        {
            return local;
        }

        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, folder);
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "MenuApp.sln")))
            {
                return candidate;
            }
        }

        return local;
    }

    public static string SeedPath(IConfiguration configuration) => ResolveDataPath(configuration["Seed:Path"], "seed");

    public static IServiceCollection AddMenuPlannerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ => Application.Validation.SchemaValidator.FromDirectory(ResolveDataPath(configuration["Schemas:Path"], "schemas")));

        if (UsesMemoryStorage(configuration))
        {
            services.AddSingleton(_ => Memory.InMemoryStore.FromSeed(SeedData.Load(SeedPath(configuration))));
            services.AddSingleton<IPlanRepository>(sp => sp.GetRequiredService<Memory.InMemoryStore>());
            services.AddSingleton<IIngredientRepository>(sp => sp.GetRequiredService<Memory.InMemoryStore>());
            services.AddSingleton<IRecipeRepository>(sp => sp.GetRequiredService<Memory.InMemoryStore>());
            services.AddSingleton<IMenuRepository>(sp => sp.GetRequiredService<Memory.InMemoryStore>());
            services.AddSingleton<ISettingsRepository>(sp => sp.GetRequiredService<Memory.InMemoryStore>());
            services.AddSingleton<IDishRepository>(sp => sp.GetRequiredService<Memory.InMemoryStore>());
            services.AddSingleton<IImportRepository>(sp => sp.GetRequiredService<Memory.InMemoryStore>());
            return services;
        }

        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.Section));
        services.AddSingleton<MongoContext>();
        services.AddSingleton<IPlanRepository, PlanRepository>();
        services.AddSingleton<IIngredientRepository, IngredientRepository>();
        services.AddSingleton<IRecipeRepository, RecipeRepository>();
        services.AddSingleton<IMenuRepository, MenuRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<IDishRepository, DishRepository>();
        services.AddSingleton<IImportRepository, ImportRepository>();
        return services;
    }

    /// <summary>
    /// Crea índices y carga el seed en las colecciones vacías (idempotente: nunca sobrescribe tus datos).
    /// </summary>
    public static async Task InitializeDatabaseAsync(IServiceProvider services, string? seedDirectory, ILogger logger, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<MongoContext>();
        await db.EnsureIndexesAsync(ct);

        if (string.IsNullOrWhiteSpace(seedDirectory) || !Directory.Exists(seedDirectory))
        {
            logger.LogInformation("Seed deshabilitado o no encontrado ({Directory}).", seedDirectory);
            return;
        }

        var seed = SeedData.Load(seedDirectory);

        if (await db.Ingredients.EstimatedDocumentCountAsync(cancellationToken: ct) == 0 && seed.Ingredients.Count > 0)
        {
            await db.Ingredients.InsertManyAsync(seed.Ingredients, cancellationToken: ct);
            logger.LogInformation("Seed: {Count} ingredientes cargados.", seed.Ingredients.Count);
        }

        if (await db.Plans.EstimatedDocumentCountAsync(cancellationToken: ct) == 0 && seed.Plans.Count > 0)
        {
            await db.Plans.InsertManyAsync(seed.Plans, cancellationToken: ct);
            logger.LogInformation("Seed: {Count} planes cargados.", seed.Plans.Count);
        }

        if (await db.Recipes.EstimatedDocumentCountAsync(cancellationToken: ct) == 0 && seed.Recipes.Count > 0)
        {
            await db.Recipes.InsertManyAsync(seed.Recipes, cancellationToken: ct);
            logger.LogInformation("Seed: {Count} recetas cargadas.", seed.Recipes.Count);
        }

        if (await db.Dishes.EstimatedDocumentCountAsync(cancellationToken: ct) == 0 && seed.Dishes.Count > 0)
        {
            await db.Dishes.InsertManyAsync(seed.Dishes, cancellationToken: ct);
            logger.LogInformation("Seed: {Count} platillos del diccionario de Keep cargados.", seed.Dishes.Count);
        }

        if (await db.ImportRules.EstimatedDocumentCountAsync(cancellationToken: ct) == 0)
        {
            seed.ImportRules.Id = "default";
            await db.ImportRules.InsertOneAsync(seed.ImportRules, cancellationToken: ct);
        }
    }
}
