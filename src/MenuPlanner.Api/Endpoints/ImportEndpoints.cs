using System.Text.Json.Nodes;
using MenuPlanner.Application;
using MenuPlanner.Application.Imports;
using MenuPlanner.Application.Seeding;
using MenuPlanner.Application.Validation;
using MenuPlanner.Domain;

namespace MenuPlanner.Api.Endpoints;

public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var imports = app.MapGroup("/api/imports").WithTags("Importar Keep");

        imports.MapPost("/keep", async (HttpRequest request, int? latestYear, bool? dryRun, ImportService service, CancellationToken ct) =>
            {
                var (text, fileName) = await ReadTextAsync(request, ct);
                return await service.ImportKeepAsync(text, fileName, latestYear, dryRun ?? false, ct);
            })
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data", "text/plain", "text/markdown")
            .WithSummary("Importa tus notas de Keep copiadas a Google Docs (descárgalo como .md o .txt). dryRun=true solo muestra el resumen.");

        imports.MapGet("/", (int? limit, IImportRepository repo, CancellationToken ct) => repo.ListJobsAsync(limit ?? 20, ct));

        imports.MapGet("/{id}", async (string id, IImportRepository repo, CancellationToken ct) =>
            await repo.GetJobAsync(id, ct) ?? throw new NotFoundException($"No existe la importación '{id}'."));

        imports.MapPost("/{id}/review", (string id, ReviewRequest request, ImportService service, CancellationToken ct) =>
                service.ReviewAsync(id, request, ct))
            .WithSummary("Asigna nombres sin asignar: {mappings:[{name, dishId | newDishName | ignore}]}.");

        var dishes = app.MapGroup("/api/dishes").WithTags("Platillos (historial)");

        dishes.MapGet("/", (string? sort, DishService service, CancellationToken ct) => service.ListAsync(sort, ct))
            .WithSummary("Tus platillos con frecuencia, última vez y receta ligada (sort=frequency|name).");

        dishes.MapGet("/{id}", async (string id, IDishRepository repo, CancellationToken ct) =>
            await repo.GetAsync(id, ct) ?? throw new NotFoundException($"No existe el platillo '{id}'."));

        dishes.MapPut("/{id}", (string id, JsonNode body, SchemaValidator validator, DishService service, CancellationToken ct) =>
                service.UpdateAsync(ValidatedJson.Read<Dish>(validator, SchemaNames.Dish, WithoutStats(body), id), ct))
            .WithSummary("Edita alias, receta ligada o compatibilidad (validado con schemas/dish.schema.json).");

        app.MapGet("/api/pantry/suggestions", (int? months, double? minShare, ImportService service, CancellationToken ct) =>
                service.GetPantrySuggestionsAsync(months ?? 6, minShare ?? 0.25, ct))
            .WithTags("Despensa")
            .WithSummary("Lo que compras con frecuencia según tus listas de Keep (para despensa y básicos).");

        app.MapPost("/api/seed/sync", (SeedSyncService service, IConfiguration configuration, CancellationToken ct) =>
                service.SyncAsync(SeedData.Load(MenuPlanner.Infrastructure.DependencyInjection.SeedPath(configuration)), ct))
            .WithTags("Seed")
            .WithSummary("Agrega lo nuevo del seed a una base existente y quita las recetas de arranque inventadas.");

        return app;
    }

    private static async Task<(string Text, string? FileName)> ReadTextAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault() ?? throw new BadRequestException("Adjunta el archivo en el campo 'file'.");
            using var reader = new StreamReader(file.OpenReadStream());
            return (await reader.ReadToEndAsync(ct), file.FileName);
        }

        using var body = new StreamReader(request.Body);
        return (await body.ReadToEndAsync(ct), null);
    }

    private static JsonNode? WithoutStats(JsonNode? body)
    {
        if (body is JsonObject obj)
        {
            obj.Remove("stats");
        }

        return body;
    }
}
