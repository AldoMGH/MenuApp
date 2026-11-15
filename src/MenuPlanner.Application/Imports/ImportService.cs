using MenuPlanner.Domain;
using MenuPlanner.Importers;

namespace MenuPlanner.Application.Imports;

public sealed record ReviewMapping(string Name, string? DishId = null, string? NewDishName = null, bool Ignore = false);

public sealed record ReviewRequest(IReadOnlyList<ReviewMapping> Mappings);

public sealed record PantrySuggestion(
    string? IngredientId,
    string Name,
    NoteSection Section,
    int WeeksBought,
    int WeeksInWindow,
    double Share,
    DateTime LastBought,
    double? TypicalQty,
    Unit? TypicalUnit,
    bool InPantry,
    bool InBasics);

/// <summary>
/// Importa tu historial de Keep (texto de Google Docs), asigna platillos con el diccionario, guarda el historial
/// de compras y recalcula las estadísticas que usa el generador. Reimportar el mismo texto no duplica nada.
/// </summary>
public sealed class ImportService(
    IImportRepository imports,
    IDishRepository dishes,
    IIngredientRepository ingredients,
    IRecipeRepository recipes,
    ISettingsRepository settings,
    TimeProvider time)
{
    public const int MaxTextLength = 5_000_000;

    public async Task<ImportJob> ImportKeepAsync(string text, string? fileName, int? latestYear, bool dryRun, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BadRequestException("El archivo está vacío.");
        }

        if (text.Length > MaxTextLength)
        {
            throw new BadRequestException("El archivo es demasiado grande (máximo ~5 MB de texto).");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var parsed = KeepNotesParser.Parse(text, latestYear, time.GetLocalNow().DateTime);
        if (parsed.Notes.Count == 0)
        {
            throw new BadRequestException(
                "No encontré notas. Cada nota debe empezar con un título como \"Noviembre 1\" y tener secciones como \"Comidas\".");
        }

        var rules = await settings.GetImportRulesAsync(ct);
        var dishList = await dishes.ListAsync(ct);
        var matcher = new DishMatcher(dishList, rules);
        var ingredientMatcher = new IngredientMatcher(await ingredients.ListAsync(ct), rules);

        var job = new ImportJob
        {
            Id = $"import-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..4]}",
            CreatedAt = now,
            FileName = fileName,
            DryRun = dryRun,
            LatestYear = parsed.LatestYear,
            TotalNotes = parsed.Notes.Count,
            FirstDate = parsed.Notes.Min(n => n.Date),
            LastDate = parsed.Notes.Max(n => n.Date),
        };
        job.Warnings.AddRange(parsed.Warnings);

        var existing = await imports.GetExistingNoteIdsAsync(parsed.Notes.Select(n => n.Hash), ct);
        var notes = new List<ImportedNote>();
        var occurrences = new List<DishOccurrence>();
        var purchases = new List<PurchaseRecord>();

        foreach (var note in parsed.Notes)
        {
            if (existing.Contains(note.Hash) || notes.Any(n => n.Id == note.Hash))
            {
                job.DuplicateNotes++;
                continue;
            }

            notes.Add(new ImportedNote { Id = note.Hash, ImportId = job.Id, Title = note.Title, Date = note.Date, Position = note.Position });
            var index = 0;
            foreach (var item in note.Items)
            {
                index++;
                if (item.Section == NoteSection.Comidas)
                {
                    var match = matcher.Match(item.Text);
                    occurrences.Add(new DishOccurrence
                    {
                        Id = $"{note.Hash}-{index}",
                        NoteId = note.Hash,
                        ImportId = job.Id,
                        Date = note.Date,
                        NoteTitle = note.Title,
                        RawName = item.Text,
                        Normalized = match.Normalized,
                        DishId = match.DishId,
                        Side = match.Side,
                        Status = match.Status,
                        Confidence = match.Confidence,
                    });
                }
                else
                {
                    var quantity = QuantityParser.Parse(item.Text);
                    purchases.Add(new PurchaseRecord
                    {
                        Id = $"{note.Hash}-{index}",
                        NoteId = note.Hash,
                        ImportId = job.Id,
                        Date = note.Date,
                        Section = item.Section,
                        Raw = item.Text,
                        Name = quantity.Name,
                        Qty = quantity.Qty,
                        Unit = quantity.Unit,
                        IngredientId = ingredientMatcher.Match(quantity.Name),
                    });
                }
            }
        }

        job.NewNotes = notes.Count;
        job.PurchaseRecords = purchases.Count;
        job.PurchasesMatched = purchases.Count(p => p.IngredientId is not null);
        Summarize(job, occurrences, matcher);

        if (!dryRun)
        {
            await imports.SaveImportAsync(job, notes, occurrences, purchases, ct);
            await RecomputeStatsAsync(ct);
        }

        return job;
    }

    /// <summary>Aplica tus decisiones sobre nombres sin asignar o aproximados y vuelve a asignar todo el historial.</summary>
    public async Task<ImportJob> ReviewAsync(string importId, ReviewRequest request, CancellationToken ct = default)
    {
        var job = await imports.GetJobAsync(importId, ct) ?? throw new NotFoundException($"No existe la importación '{importId}'.");
        if (request.Mappings.Count == 0)
        {
            throw new BadRequestException("No enviaste ninguna asignación.");
        }

        var rules = await settings.GetImportRulesAsync(ct);
        var dishList = await dishes.ListAsync(ct);
        var byId = dishList.ToDictionary(d => d.Id, StringComparer.Ordinal);
        var normalizer = new DishMatcher(dishList, rules);
        var changed = new Dictionary<string, Dish>(StringComparer.Ordinal);

        foreach (var mapping in request.Mappings)
        {
            var name = normalizer.Normalize(mapping.Name);
            if (name.Length == 0)
            {
                throw new BadRequestException("Cada asignación necesita 'name'.");
            }

            if (mapping.Ignore)
            {
                if (!rules.Ignore.Contains(name, StringComparer.Ordinal))
                {
                    rules.Ignore.Add(name);
                }
            }
            else if (!string.IsNullOrWhiteSpace(mapping.DishId))
            {
                var dish = byId.GetValueOrDefault(mapping.DishId) ?? throw new BadRequestException($"No existe el platillo '{mapping.DishId}'.");
                AddAlias(dish, name);
                changed[dish.Id] = dish;
            }
            else if (!string.IsNullOrWhiteSpace(mapping.NewDishName))
            {
                var id = UniqueId(TextNormalizer.Slug(mapping.NewDishName), byId);
                var dish = new Dish { Id = id, Name = mapping.NewDishName.Trim(), Aliases = [name] };
                byId[id] = dish;
                changed[id] = dish;
            }
            else
            {
                throw new BadRequestException($"'{mapping.Name}': indica dishId, newDishName o ignore.");
            }
        }

        await settings.SaveImportRulesAsync(rules, ct);
        await dishes.UpsertManyAsync(changed.Values, ct);
        await RematchAllAsync(ct);

        var matcher = new DishMatcher(await dishes.ListAsync(ct), rules);
        Summarize(job, await imports.ListOccurrencesAsync(importId, ct), matcher);
        await imports.SaveJobAsync(job, ct);
        return job;
    }

    /// <summary>Vuelve a asignar todo el historial con el diccionario actual (tras editar platillos o reglas).</summary>
    public async Task<int> RematchAllAsync(CancellationToken ct = default)
    {
        var matcher = new DishMatcher(await dishes.ListAsync(ct), await settings.GetImportRulesAsync(ct));
        var updated = new List<DishOccurrence>();
        foreach (var occurrence in await imports.ListOccurrencesAsync(ct: ct))
        {
            var match = matcher.Match(occurrence.RawName);
            if (match.DishId != occurrence.DishId || match.Status != occurrence.Status || match.Normalized != occurrence.Normalized)
            {
                occurrence.DishId = match.DishId;
                occurrence.Status = match.Status;
                occurrence.Confidence = match.Confidence;
                occurrence.Normalized = match.Normalized;
                occurrence.Side = match.Side;
                updated.Add(occurrence);
            }
        }

        await imports.UpdateOccurrencesAsync(updated, ct);
        await RecomputeStatsAsync(ct);
        return updated.Count;
    }

    /// <summary>Recalcula estadísticas de platillos y el historial de cada receta ligada.</summary>
    public async Task RecomputeStatsAsync(CancellationToken ct = default)
    {
        var occurrences = await imports.ListOccurrencesAsync(ct: ct);
        var counted = occurrences
            .Where(o => o.DishId is not null && o.Status is MatchStatus.Matched or MatchStatus.Fuzzy)
            .GroupBy(o => o.DishId!)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var dishList = await dishes.ListAsync(ct);
        var history = new Dictionary<string, (int Count, DateTime? Last, int[] Months)>(StringComparer.Ordinal);
        foreach (var dish in dishList)
        {
            var items = counted.GetValueOrDefault(dish.Id) ?? [];
            var months = new int[12];
            foreach (var item in items)
            {
                months[item.Date.Month - 1]++;
            }

            dish.Stats = new DishStats
            {
                TimesCooked = items.Count,
                FirstCooked = items.Count == 0 ? null : items.Min(i => i.Date),
                LastCooked = items.Count == 0 ? null : items.Max(i => i.Date),
                Months = [.. months],
            };

            if (dish.RecipeId is null || items.Count == 0)
            {
                continue;
            }

            var current = history.GetValueOrDefault(dish.RecipeId, (0, null, new int[12]));
            var last = current.Last is null || dish.Stats.LastCooked > current.Last ? dish.Stats.LastCooked : current.Last;
            history[dish.RecipeId] = (current.Count + items.Count, last, current.Months.Zip(months, (a, b) => a + b).ToArray());
        }

        await dishes.UpsertManyAsync(dishList, ct);
        await recipes.SetHistoryAsync(
            history.ToDictionary(h => h.Key, h => new RecipeHistory(h.Value.Count, h.Value.Last, h.Value.Months)),
            ct);
    }

    /// <summary>Ingredientes que compras con frecuencia (según tus listas de Keep) para proponer tu despensa y básicos.</summary>
    public async Task<List<PantrySuggestion>> GetPantrySuggestionsAsync(int months, double minShare, CancellationToken ct = default)
    {
        var purchases = await imports.ListPurchasesAsync(ct);
        if (purchases.Count == 0)
        {
            return [];
        }

        var catalog = await ingredients.ListAsync(ct);
        var names = catalog.ToDictionary(i => i.Id, i => i.Name, StringComparer.Ordinal);
        var matcher = new IngredientMatcher(catalog, await settings.GetImportRulesAsync(ct));
        var pantry = (await settings.GetPantryAsync(ct)).IngredientIds.ToHashSet(StringComparer.Ordinal);
        var basics = (await settings.GetBasicsAsync(ct)).Items.Select(b => b.IngredientId).ToHashSet(StringComparer.Ordinal);

        var latest = purchases.Max(p => p.Date);
        var from = latest.AddMonths(-Math.Clamp(months, 1, 60));
        var window = purchases.Where(p => p.Date > from).ToList();
        var weeks = window.Select(p => p.NoteId).Distinct().Count();
        if (weeks == 0)
        {
            return [];
        }

        return window
            .Select(p => (Record: p, IngredientId: matcher.Match(p.Name)))
            .GroupBy(x => x.IngredientId ?? "?" + matcher.Normalize(x.Record.Name))
            .Select(g =>
            {
                var ingredientId = g.First().IngredientId;
                var weeksBought = g.Select(x => x.Record.NoteId).Distinct().Count();
                var quantities = g.Where(x => x.Record.Qty is not null).Select(x => x.Record).ToList();
                var unit = quantities.GroupBy(q => q.Unit).OrderByDescending(u => u.Count()).Select(u => u.Key).FirstOrDefault();
                var typical = Median(quantities.Where(q => q.Unit == unit).Select(q => q.Qty!.Value).ToList());
                var name = ingredientId is not null ? names[ingredientId] : g.First().Record.Name;
                return new PantrySuggestion(
                    ingredientId,
                    name,
                    g.GroupBy(x => x.Record.Section).OrderByDescending(s => s.Count()).First().Key,
                    weeksBought,
                    weeks,
                    Math.Round(weeksBought / (double)weeks, 2),
                    g.Max(x => x.Record.Date),
                    typical,
                    typical is null ? null : unit,
                    ingredientId is not null && pantry.Contains(ingredientId),
                    ingredientId is not null && basics.Contains(ingredientId));
            })
            .Where(s => s.Share >= minShare)
            .OrderByDescending(s => s.Share)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private static void Summarize(ImportJob job, IReadOnlyList<DishOccurrence> occurrences, DishMatcher matcher)
    {
        job.DishOccurrences = occurrences.Count;
        job.Matched = occurrences.Count(o => o.Status == MatchStatus.Matched);
        job.Fuzzy = occurrences.Count(o => o.Status == MatchStatus.Fuzzy);
        job.Ignored = occurrences.Count(o => o.Status == MatchStatus.Ignored);
        job.Unmatched = occurrences
            .Where(o => o.Status == MatchStatus.Unmatched)
            .GroupBy(o => o.Normalized)
            .Select(g => new UnmatchedName
            {
                Name = g.Key,
                Count = g.Count(),
                Examples = g.Select(o => o.RawName).Distinct().Take(3).ToList(),
                Suggestions = [.. matcher.Match(g.First().RawName).Suggestions],
            })
            .OrderByDescending(u => u.Count)
            .ToList();
        job.FuzzyMatches = occurrences
            .Where(o => o.Status == MatchStatus.Fuzzy)
            .GroupBy(o => (o.Normalized, o.DishId))
            .Select(g => new FuzzyMatch { Name = g.Key.Normalized, DishId = g.Key.DishId!, Confidence = g.First().Confidence, Count = g.Count() })
            .OrderByDescending(f => f.Count)
            .ToList();
    }

    private static void AddAlias(Dish dish, string alias)
    {
        if (!dish.Aliases.Contains(alias, StringComparer.Ordinal))
        {
            dish.Aliases.Add(alias);
        }
    }

    private static string UniqueId(string slug, IReadOnlyDictionary<string, Dish> existing)
    {
        var id = string.IsNullOrEmpty(slug) ? "platillo" : slug;
        var candidate = id;
        for (var i = 2; existing.ContainsKey(candidate); i++)
        {
            candidate = $"{id}-{i}";
        }

        return candidate;
    }

    private static double? Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }
}
