using MenuPlanner.Application.Menus;
using MenuPlanner.Application.Shopping;
using MenuPlanner.Infrastructure.Mongo;
using MenuPlanner.Portioning;
using Microsoft.Extensions.Logging.Abstractions;

namespace MenuPlanner.IntegrationTests;

/// <summary>
/// Usa tu servidor MongoDB local en una base temporal <c>menuplanner_test_*</c> que se elimina al terminar.
/// Cadena de conexión: variable <c>MENUPLANNER_TEST_MONGO</c> (p. ej. mongodb://localhost:27017).
/// Si no está definida o el servidor no responde, las pruebas se marcan como omitidas.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "MENUPLANNER_TEST_MONGO";

    private readonly string? _connectionString = Environment.GetEnvironmentVariable(EnvironmentVariable);
    private readonly string _database = $"menuplanner_test_{Guid.NewGuid():N}";

    public MongoContext? Context { get; private set; }

    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            SkipReason = $"Define {EnvironmentVariable} (p. ej. mongodb://localhost:27017) para correr las pruebas de integración.";
            return;
        }

        var separator = _connectionString.Contains('?') ? "&" : "?";
        var context = new MongoContext($"{_connectionString}{separator}serverSelectionTimeoutMS=3000", _database);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        if (!await context.PingAsync(cts.Token))
        {
            SkipReason = $"No se pudo conectar a MongoDB en {_connectionString}.";
            return;
        }

        var seed = Path.Combine(AppContext.BaseDirectory, "seed");
        var services = new SingleServiceProvider(context);
        await Infrastructure.DependencyInjection.InitializeDatabaseAsync(services, seed, NullLogger.Instance);
        Context = context;
    }

    public async Task DisposeAsync()
    {
        if (Context is not null)
        {
            await Context.Client.DropDatabaseAsync(_database);
        }
    }

    public MenuService CreateMenuService()
    {
        var db = Context!;
        var converter = new UnitConverter();
        return new MenuService(
            new PlanRepository(db),
            new RecipeRepository(db),
            new IngredientRepository(db),
            new MenuRepository(db),
            new SettingsRepository(db),
            new PortionCalculator(converter),
            new MenuGenerator(),
            new ShoppingListBuilder(converter, TimeProvider.System),
            TimeProvider.System);
    }

    public MenuPlanner.Application.Imports.ImportService CreateImportService()
    {
        var db = Context!;
        return new MenuPlanner.Application.Imports.ImportService(
            new ImportRepository(db),
            new DishRepository(db),
            new IngredientRepository(db),
            new RecipeRepository(db),
            new SettingsRepository(db),
            TimeProvider.System);
    }

    private sealed class SingleServiceProvider(MongoContext context) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(MongoContext) ? context : null;
    }
}

[CollectionDefinition(Name)]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>
{
    public const string Name = "mongo";
}
