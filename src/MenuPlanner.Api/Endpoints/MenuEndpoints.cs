using MenuPlanner.Application.Menus;
using MenuPlanner.Application.Shopping;

namespace MenuPlanner.Api.Endpoints;

public static class MenuEndpoints
{
    public static IEndpointRouteBuilder MapMenuEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/menus").WithTags("Menú semanal");

        group.MapPost("/generate", async (GenerateMenuRequest request, string? format, MenuService service, CancellationToken ct) =>
                Render(await service.GenerateAsync(request, ct), format))
            .WithSummary("Genera un menú en borrador con N platillos de 4 porciones (format=text para resumen legible).");

        group.MapGet("/", (int? limit, MenuService service, CancellationToken ct) => service.ListAsync(limit ?? 20, ct));

        group.MapGet("/{id}", async (string id, string? format, MenuService service, CancellationToken ct) =>
            Render(await service.GetAsync(id, ct), format));

        group.MapPost("/{id}/dishes", async (string id, AddDishRequest request, string? format, MenuService service, CancellationToken ct) =>
                Render(await service.AddDishAsync(id, request.RecipeId, ct), format))
            .WithSummary("Agrega un platillo al borrador.");

        group.MapDelete("/{id}/dishes/{recipeId}", async (string id, string recipeId, string? format, MenuService service, CancellationToken ct) =>
                Render(await service.RemoveDishAsync(id, recipeId, ct), format))
            .WithSummary("Quita un platillo del borrador.");

        group.MapPost("/{id}/dishes/{recipeId}/swap", async (string id, string recipeId, SwapCriteria? criteria, string? format, MenuService service, CancellationToken ct) =>
                Render(await service.SwapDishAsync(id, recipeId, criteria, ct), format))
            .WithSummary("Cambia un platillo por otro (opcional: proteinFamily = pescado, pollo…; tag).");

        group.MapPost("/{id}/confirm", async (string id, string? format, MenuService service, CancellationToken ct) =>
                Render(await service.ConfirmAsync(id, ct), format))
            .WithSummary("Confirma el menú: congela recetas y lista de compras y actualiza el historial.");

        group.MapGet("/{id}/shopping-list", async (string id, string? format, MenuService service, CancellationToken ct) =>
            {
                var list = await service.GetShoppingListAsync(id, ct);
                if (IsText(format))
                {
                    var menu = await service.GetAsync(id, ct);
                    return Results.Text(
                        ShoppingListBuilder.ToText(list, $"Lista de compras · semana del {MenuService.FormatWeek(menu.WeekOf)}"),
                        "text/plain; charset=utf-8");
                }

                return Results.Ok(list);
            })
            .WithSummary("Lista de compras agrupada por pasillo (format=text para copiar a WhatsApp/Keep).");

        return app;
    }

    private static IResult Render(MenuView menu, string? format) =>
        IsText(format) ? Results.Text(MenuTextFormatter.Format(menu), "text/plain; charset=utf-8") : Results.Ok(menu);

    private static bool IsText(string? format) => string.Equals(format, "text", StringComparison.OrdinalIgnoreCase);
}
