using MenuPlanner.Api;
using MenuPlanner.Api.Endpoints;
using MenuPlanner.Application;
using MenuPlanner.Application.Validation;
using MenuPlanner.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => AppJson.Configure(o.SerializerOptions));
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o => AppJson.Configure(o.JsonSerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SupportNonNullableReferenceTypes());

builder.Services.AddSingleton(
    builder.Configuration.GetSection("MenuGeneration").Get<MenuPlanner.Application.Menus.GenerationOptions>()
    ?? new MenuPlanner.Application.Menus.GenerationOptions());
builder.Services.AddMenuPlannerApplication();
builder.Services.AddMenuPlannerInfrastructure(builder.Configuration);

var memoryStorage = MenuPlanner.Infrastructure.DependencyInjection.UsesMemoryStorage(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (memoryStorage)
{
    // El almacén en memoria no es concurrente: en este modo las peticiones a la API se atienden de una en una.
    app.Logger.LogWarning("Modo memoria: los datos se pierden al cerrar la app (Storage:Provider = Memory).");
    var gate = new SemaphoreSlim(1, 1);
    app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next();
            return;
        }

        await gate.WaitAsync(context.RequestAborted);
        try
        {
            await next();
        }
        finally
        {
            gate.Release();
        }
    });
}

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", true))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!memoryStorage && app.Configuration.GetValue("Seed:Enabled", true))
{
    var seedPath = MenuPlanner.Infrastructure.DependencyInjection.SeedPath(app.Configuration);
    try
    {
        await MenuPlanner.Infrastructure.DependencyInjection.InitializeDatabaseAsync(app.Services, seedPath, app.Logger);
    }
    catch (TimeoutException ex)
    {
        app.Logger.LogCritical(
            ex,
            "No se pudo conectar a MongoDB ({ConnectionString}). Revisa que el servidor esté corriendo o ajusta Mongo:ConnectionString.",
            app.Configuration["Mongo:ConnectionString"]);
        throw;
    }
}

// App web (Blazor WebAssembly) servida desde la misma API: mismo origen, sin CORS.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapPlanEndpoints();
app.MapCatalogEndpoints();
app.MapMenuEndpoints();
app.MapSettingsEndpoints();
app.MapImportEndpoints();
app.MapFallback("/api/{**rest}", () => Results.NotFound()).ExcludeFromDescription();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
