using System.Text;
using MenuPlanner.Domain;
using MenuPlanner.Portioning;

namespace MenuPlanner.Application.Shopping;

/// <summary>
/// Suma lo que piden las recetas ya porcionadas más los básicos semanales, convierte a unidad de compra
/// redondeando hacia arriba, separa lo que ya tienes en la despensa y agrupa por pasillo.
/// </summary>
public sealed class ShoppingListBuilder(UnitConverter converter, TimeProvider time)
{
    public ShoppingList Build(
        IEnumerable<PortionedRecipe> recipes,
        IEnumerable<WeeklyBasic> basics,
        IReadOnlyDictionary<string, Ingredient> ingredients,
        IEnumerable<string> pantry)
    {
        var totals = new Dictionary<string, (double Qty, SortedSet<string> Sources)>(StringComparer.Ordinal);

        void Add(string ingredientId, double qty, string source)
        {
            if (qty <= 0)
            {
                return;
            }

            if (!totals.TryGetValue(ingredientId, out var entry))
            {
                entry = (0, new SortedSet<string>(StringComparer.CurrentCulture));
            }

            entry.Sources.Add(source);
            totals[ingredientId] = (entry.Qty + qty, entry.Sources);
        }

        foreach (var recipe in recipes)
        {
            foreach (var line in recipe.Batch)
            {
                Add(line.IngredientId, line.Quantity, recipe.Name);
            }
        }

        foreach (var basic in basics)
        {
            if (!ingredients.TryGetValue(basic.IngredientId, out var ingredient))
            {
                continue;
            }

            var qty = converter.ToBase(ingredient, basic.QtyPerDay * basic.DaysPerWeek, basic.Unit, basic.State);
            Add(ingredient.Id, qty, $"Básicos ({basic.Meal})");
        }

        var pantrySet = new HashSet<string>(pantry, StringComparer.Ordinal);
        var list = new ShoppingList { GeneratedAt = time.GetUtcNow().UtcDateTime };

        foreach (var (id, (qty, sources)) in totals)
        {
            if (!ingredients.TryGetValue(id, out var ingredient))
            {
                continue;
            }

            var purchaseUnit = ingredient.Purchase.Unit;
            var purchaseQty = QuantityFormatter.RoundUpToStep(
                converter.Convert(ingredient, qty, ingredient.BaseUnit, purchaseUnit),
                ingredient.Purchase.Step);

            var item = new ShoppingItem
            {
                IngredientId = id,
                Name = ingredient.Name,
                Aisle = ingredient.Aisle,
                NeededQuantity = Math.Round(qty, 2),
                NeededUnit = ingredient.BaseUnit,
                NeededDisplay = QuantityFormatter.Format(qty, ingredient.BaseUnit),
                PurchaseQuantity = purchaseQty,
                PurchaseUnit = purchaseUnit,
                PurchaseDisplay = QuantityFormatter.Format(purchaseQty, purchaseUnit, Label(ingredient, purchaseQty)),
                Sources = [.. sources],
            };

            (pantrySet.Contains(id) ? list.AlreadyHave : list.Items).Add(item);
        }

        list.Items = Sort(list.Items);
        list.AlreadyHave = Sort(list.AlreadyHave);
        return list;
    }

    public static string ToText(ShoppingList list, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🛒 {title}");
        foreach (var group in list.Items.GroupBy(i => i.Aisle))
        {
            sb.AppendLine();
            sb.AppendLine($"*{group.Key}*");
            foreach (var item in group)
            {
                sb.AppendLine($"- [ ] {item.Name}: {item.PurchaseDisplay}");
            }
        }

        if (list.AlreadyHave.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("*Ya tienes (revisa que alcance)*");
            foreach (var item in list.AlreadyHave)
            {
                sb.AppendLine($"- {item.Name}: {item.NeededDisplay}");
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string? Label(Ingredient ingredient, double qty)
    {
        var label = ingredient.Purchase.Label;
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        // Etiquetas simples en singular ("manojo", "cabeza"): plural básico en español.
        return qty > 1 && !label.EndsWith('s') ? label + "s" : label;
    }

    private static List<ShoppingItem> Sort(List<ShoppingItem> items) =>
        [.. items
            .OrderBy(i => AisleIndex(i.Aisle))
            .ThenBy(i => i.Name, StringComparer.CurrentCulture)];

    private static int AisleIndex(string aisle)
    {
        for (var i = 0; i < Aisles.Order.Count; i++)
        {
            if (string.Equals(Aisles.Order[i], aisle, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return Aisles.Order.Count;
    }
}
