using System.Text.Json.Nodes;
using MenuPlanner.Application;
using MenuPlanner.Application.Catalog;
using MenuPlanner.Application.Validation;
using MenuPlanner.Domain;

namespace MenuPlanner.Api.Endpoints;

public static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/plans").WithTags("Plan de alimentación");

        group.MapGet("/", (IPlanRepository plans, CancellationToken ct) => plans.ListAsync(ct))
            .WithSummary("Lista las versiones del plan.");

        group.MapGet("/active", async (IPlanRepository plans, CancellationToken ct) =>
                await plans.GetActiveAsync(ct) ?? throw new NotFoundException("No hay un plan activo."))
            .WithSummary("Plan activo.");

        group.MapGet("/{id}", async (string id, IPlanRepository plans, CancellationToken ct) =>
                await plans.GetAsync(id, ct) ?? throw new NotFoundException($"No existe el plan '{id}'."))
            .WithSummary("Obtiene una versión del plan.");

        group.MapPut("/{id}", async (string id, JsonNode body, SchemaValidator validator, IPlanRepository plans, IIngredientRepository ingredients, CancellationToken ct) =>
            {
                var plan = ValidatedJson.Read<NutritionPlan>(validator, SchemaNames.NutritionPlan, body, id);
                var catalog = (await ingredients.ListAsync(ct)).ToDictionary(i => i.Id);
                var errors = CatalogValidator.ValidatePlan(plan, catalog);
                if (errors.Count > 0)
                {
                    throw new BadRequestException("El plan hace referencia a ingredientes inexistentes.", errors);
                }

                await plans.UpsertAsync(plan, ct);
                if (plan.Active)
                {
                    await plans.ActivateAsync(plan.Id, ct);
                }

                return Results.Ok(plan);
            })
            .WithSummary("Crea o reemplaza una versión del plan (validado con schemas/nutrition-plan.schema.json).");

        group.MapPost("/{id}/activate", async (string id, IPlanRepository plans, CancellationToken ct) =>
            {
                await plans.ActivateAsync(id, ct);
                return Results.NoContent();
            })
            .WithSummary("Activa una versión del plan y desactiva las demás.");

        app.MapGet("/api/schemas/{name}", (string name, SchemaValidator validator) =>
                validator.GetRaw(name) is { } raw
                    ? Results.Text(raw, "application/schema+json")
                    : Results.NotFound())
            .WithTags("Schemas")
            .WithSummary("JSON Schema para formularios dinámicos: nutrition-plan, recipe, ingredient.");

        return app;
    }
}
