using System.Text;
using MenuPlanner.Domain;

namespace MenuPlanner.Application.Menus;

/// <summary>Resumen legible del menú (para terminal, WhatsApp o, más adelante, el servidor MCP).</summary>
public static class MenuTextFormatter
{
    public static string Format(MenuView menu)
    {
        var sb = new StringBuilder();
        var status = menu.Status == MenuStatus.Confirmed ? "confirmado" : "borrador";
        var protein = menu.Dishes.Select(d => d.Portioned.ProteinPerServingGrams).FirstOrDefault(p => p > 0);

        sb.AppendLine($"Menú semana del {MenuService.FormatWeek(menu.WeekOf)} ({status}, plan v{menu.PlanVersion}, proteína {protein:0} g en cocido por plato)");
        sb.AppendLine();

        var index = 1;
        foreach (var dish in menu.Dishes)
        {
            var p = dish.Portioned;
            sb.AppendLine($"{index++}. {p.Name}");
            sb.AppendLine($"   Lote ({p.Servings} porciones): {string.Join(" · ", p.Batch.Select(l => $"{l.Display} {l.Name.ToLowerInvariant()}"))}");
            sb.AppendLine($"   Tu porción: {string.Join(" + ", p.Serving.Select(s => s.Text))}");
            foreach (var suggestion in p.Suggestions)
            {
                sb.AppendLine($"   Sugerencia: {suggestion}");
            }
        }

        var s = menu.Summary;
        sb.AppendLine();
        var legumeOk = s.LegumeServings >= s.LegumeMinServings && s.LegumeServings <= s.LegumeMaxServings ? "✔" : "⚠";
        sb.AppendLine($"Leguminosa: {s.LegumeServings} de {s.TotalServings} porciones (≈ {s.LegumeMealsPerWeek:0.#} de {s.MealsPerWeek} comidas por semana) {legumeOk}");
        sb.AppendLine(s.SurplusServings > 0
            ? $"Cobertura: {s.TotalServings} porciones para {s.MealsPerWeek} comidas → sobran {s.SurplusServings} para congelar o la siguiente semana"
            : $"Cobertura: {s.TotalServings} porciones para {s.MealsPerWeek} comidas");

        if (s.Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Avisos:");
            foreach (var warning in s.Warnings)
            {
                sb.AppendLine($"- {warning}");
            }
        }

        return sb.ToString();
    }
}
