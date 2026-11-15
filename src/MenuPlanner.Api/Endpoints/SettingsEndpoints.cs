using MenuPlanner.Application;
using MenuPlanner.Domain;

namespace MenuPlanner.Api.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pantry", (ISettingsRepository repo, CancellationToken ct) => repo.GetPantryAsync(ct))
            .WithTags("Despensa");

        app.MapPut("/api/pantry", async (Pantry pantry, ISettingsRepository repo, IIngredientRepository ingredients, CancellationToken ct) =>
            {
                await EnsureIngredientsExist(pantry.IngredientIds, ingredients, ct);
                await repo.SavePantryAsync(pantry, ct);
                return Results.Ok(pantry);
            })
            .WithTags("Despensa")
            .WithSummary("Ingredientes que siempre tienes; se separan de la lista de compras.");

        app.MapGet("/api/basics", (ISettingsRepository repo, CancellationToken ct) => repo.GetBasicsAsync(ct))
            .WithTags("Básicos semanales");

        app.MapPut("/api/basics", async (BasicsConfig basics, ISettingsRepository repo, IIngredientRepository ingredients, CancellationToken ct) =>
            {
                if (basics.Items.Any(i => i.QtyPerDay <= 0 || i.DaysPerWeek is < 1 or > 7))
                {
                    throw new BadRequestException("qtyPerDay debe ser > 0 y daysPerWeek entre 1 y 7.");
                }

                await EnsureIngredientsExist(basics.Items.Select(i => i.IngredientId), ingredients, ct);
                await repo.SaveBasicsAsync(basics, ct);
                return Results.Ok(basics);
            })
            .WithTags("Básicos semanales")
            .WithSummary("Desayunos/cenas habituales que se suman a la lista (además de la colación del plan).");

        return app;
    }

    private static async Task EnsureIngredientsExist(IEnumerable<string> ids, IIngredientRepository ingredients, CancellationToken ct)
    {
        var catalog = (await ingredients.ListAsync(ct)).Select(i => i.Id).ToHashSet();
        var missing = ids.Where(id => !catalog.Contains(id)).Distinct().ToList();
        if (missing.Count > 0)
        {
            throw new BadRequestException("Hay ingredientes que no existen en el catálogo.", missing);
        }
    }
}
