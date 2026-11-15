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

builder.Services.AddMenuPlannerApplication();
builder.Services.AddMenuPlannerInfrastructure(builder.Configuration);

var schemasPath = ResolvePath(builder.Configuration["Schemas:Path"], "schemas");
builder.Services.AddSingleton(SchemaValidator.FromDirectory(schemasPath));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", true))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (app.Configuration.GetValue("Seed:Enabled", true))
{
    var seedPath = ResolvePath(app.Configuration["Seed:Path"], "seed");
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

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapPlanEndpoints();
app.MapCatalogEndpoints();
app.MapMenuEndpoints();
app.MapSettingsEndpoints();

app.Run();

// Busca primero junto al ejecutable (archivos copiados al compilar) y luego en la raíz del repo.
static string ResolvePath(string? configured, string folder)
{
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return Path.GetFullPath(configured);
    }

    var local = Path.Combine(AppContext.BaseDirectory, folder);
    return Directory.Exists(local) ? local : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", folder));
}

public partial class Program;
