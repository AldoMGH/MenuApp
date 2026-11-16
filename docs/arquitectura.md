# Arquitectura: planificador de menú semanal según el plan de alimentación

## Contexto

Quieres una app que, a partir de tu plan de alimentación (PDF, "Plan 2") y de tu historial de menús en Google Keep (2 años), genere un menú semanal de N platillos de **comida** (4 porciones cada uno, todas para ti), la lista de compras y las recetas con cantidades ajustadas al plan. Debe permitir agregar/quitar platillos y, como extra, proponer platillos nuevos con lo que sueles tener en casa.

Objetivos: **controlar porciones** y **reducir el tiempo de armar la lista del súper**.

Decisiones confirmadas: el generador cubre **solo Comida**; desayuno, colación, snack y cena se manejan como **básicos semanales** en la lista de compras. Las 4 porciones son para ti (4 comidas).

Directorio de trabajo vacío → proyecto nuevo. Stack: .NET 8 / C#, **MongoDB como única persistencia** (sin Redis ni caché). Solo corre **en local**, sin Docker: API + Blazor con `dotnet run` contra **tu servidor MongoDB local** (`mongodb://localhost:27017`, base `menuplanner`, configurable).

Entrega: repo **AldoMGH/MenuApp** (GitHub), rama de trabajo con push.

---

## 1. Lo que dice tu plan y qué cambia en el diseño

El PDF **no trae macros ni equivalentes numéricos**. Trae una **estructura por tiempo de comida** con porciones en **medidas caseras y gramos de proteína cocida**, más ejemplos:

| Tiempo | Estructura (porción para ti) |
|---|---|
| **Comida** | Verdura base **2 tz** + **150–200 g proteína magra cocida** (pollo, cerdo, res, pescado) + **½ tz leguminosa 2–3 veces/semana** + **1 porción cereal/tubérculo** + verdura al gusto (+ ¼ aguacate en varios ejemplos) |
| Porción de cereal = | 2 tortillas de maíz · 4 de nopal · ½ tz pasta cocida · 2 tostadas horneadas · ½ tz arroz o quinoa cocidos · ½ pza papa/camote cocido |
| Desayuno | ~90–100 g proteína + verduras + 2 tortillas / 1–2 paquetes de salmas + ¼ aguacate |
| Colación | 1 tz fruta + ½ tz yogurt natural sin azúcar + chía (1–2 cditas) |
| Snack (opc.) | Verdura + ½ puñito cacahuate · requesón/cottage + canela · salmas + hummus · panela + verdura |
| Cena | ~90–100 g proteína (pollo, pavo, atún, salmón, panela) + verdura + cereal pequeño + grasa |

Reglas cualitativas: proteínas magras, evitar fritos/capeados/empanizados, bajos en azúcar, grasa saturada y sodio; aumentar verdura poco a poco. Guía visual: **½ verdura, ¼ proteína, ¼ cereal o leguminosa + grasa saludable**. El plan es flexible: los platillos se pueden repetir y cambiar por **equivalentes** del mismo grupo. Hay próxima cita, así que **el plan va a cambiar** → hay que versionarlo.

### Consecuencias para la arquitectura (cambios contra la primera propuesta)
1. **El plan es una plantilla de componentes, no un objetivo de macros.** Se modela como `MealTemplate` → `ComponentSlot` (Verdura, Proteína, Leguminosa, Cereal, Grasa), cada uno con **rango** y unidad casera.
2. **Tablas de intercambio** (la lista "porción de cereal = …") son datos de primera clase: un componente se cumple con cualquier opción de su grupo.
3. **El motor de porciones se basa en reglas, no en optimización.** `cantidad = porción del plan × 4 porciones ÷ rendimiento crudo→cocido`. Ya no hace falta OR-Tools.
4. **La conversión cocido → crudo es clave para la lista de compras.** El plan habla de "150–200 g de proteína **cocida**" y "½ tz de arroz **cocido**", pero en el súper compras crudo.
5. **La receta = guisado + acompañamientos por porción.** El guisado de Keep (ej. "lomo en salsa verde") aporta proteína y verdura; tortillas, arroz, frijolitos y aguacate son **acompañamientos** que la app agrega para completar la plantilla.
6. **Las reglas semanales cuentan porciones, no platillos.** "Leguminosa 2–3 veces/semana" se evalúa sobre las comidas que realmente te sirves.
7. **Los macros pasan a ser opcionales** (solo para mostrar), estimados con la tabla SMAE. No guían el cálculo.
8. **Capturar el plan a mano en vez de que lo lea un LLM.** Solo 2 páginas importan y cambian cada cita; un formulario dinámico (JSON Schema) es más confiable y no manda datos personales a un servicio externo. El parseo con LLM queda como opción posterior.

---

## 2. Arquitectura: monolito modular

```
           ┌──────────── Clientes ─────────────┐
           │ Blazor WASM (PWA)  │  Servidor MCP  │ ← "Dame un menú semanal con 5 platillos"
           └────────┬───────────┴───────┬────────┘
                    │ REST              │ tools
           ┌────────▼───────────────────▼────────┐
           │      ASP.NET Core 8 Minimal APIs     │
           │  NutritionPlan │ Recipes │ Portioning │
           │  MenuPlanning  │ Shopping │ Import    │
           │  AI Gateway (Keep, sugerencias)       │
           └──────┬───────────────────────┬───────┘
               MongoDB local (tu servidor)   Claude API (solo import Keep y sugerencias)
```

Todo corre en local, sin Docker: la API y la PWA con `dotnet run`, conectadas a tu Mongo. La cadena de conexión va en `appsettings.Development.json` (`Mongo:ConnectionString`, `Mongo:Database`) y se puede cambiar con `dotnet user-secrets` o con la variable `Mongo__ConnectionString`. El servidor MCP corre por **stdio** en tu máquina (Claude Desktop / Claude Code lo lanza), sin exponer nada a internet.

```
src/
  MenuPlanner.Api/             Minimal APIs, OpenAPI, DI
  MenuPlanner.Domain/          Quantity, HouseholdMeasure, ComponentGroup, MealTemplate, Recipe, WeeklyMenu
  MenuPlanner.Application/     Casos de uso, FluentValidation, validación JSON Schema (JsonSchema.Net)
  MenuPlanner.Portioning/      Motor de porciones, conversiones de unidades, rendimientos cocido/crudo
  MenuPlanner.Infrastructure/  MongoDB.Driver (repositorios, índices, seed), cliente Anthropic (fase 2)
  MenuPlanner.Importers/       Google Keep (Takeout JSON)
  MenuPlanner.Mcp/             Servidor MCP (SDK ModelContextProtocol para C#)
  MenuPlanner.Web/             Blazor WASM, formularios generados desde JSON Schema
schemas/                       nutrition-plan, recipe, ingredient (.schema.json): una sola fuente para UI, API y Mongo
seed/                          plan-2.json, recetas ejemplo del plan, ingredientes base, tabla de rendimientos
tests/                         xUnit; integración contra tu Mongo local en una base temporal
```

---

## 3. Modelo de dominio

### 3.1 Plan de alimentación (versionado)
```json
{
  "_id": "plan-2",
  "version": 2,
  "validFrom": "2026-11-xx",
  "active": true,
  "meals": {
    "comida": {
      "components": [
        { "group": "verdura",    "min": 2,   "max": 2,   "unit": "taza", "basis": "cocido_o_crudo" },
        { "group": "proteina",   "min": 150, "max": 200, "unit": "g",    "basis": "cocido", "constraint": "magra" },
        { "group": "leguminosa", "min": 0.5, "max": 0.5, "unit": "taza", "basis": "cocido",
          "weeklyFrequency": { "min": 2, "max": 3 } },
        { "group": "cereal",     "min": 1,   "max": 1,   "unit": "porcion" },
        { "group": "grasa",      "min": 0,   "max": 1,   "unit": "porcion", "optional": true }
      ]
    }
  },
  "weeklyBasics": [
    { "meal": "colacion", "perDay": [ { "item": "fruta", "qty": 1, "unit": "taza" },
                                      { "item": "yogurt-natural", "qty": 0.5, "unit": "taza" },
                                      { "item": "chia", "qty": 1.5, "unit": "cdita" } ] }
  ],
  "rules": { "forbiddenMethods": ["frito", "capeado", "empanizado"], "proteinMustBe": "magra" },
  "proteinTarget": "max"
}
```
`proteinTarget` (`min | mid | max`) elige el punto dentro del rango. Sugiero `max` por tu entrenamiento de fuerza, pero lo decides tú.

### 3.2 Tabla de intercambios (grupo → opciones con su porción)
```json
{ "group": "cereal", "options": [
  { "ingredientId": "tortilla-maiz",     "qty": 2,   "unit": "pieza" },
  { "ingredientId": "tortilla-nopal",    "qty": 4,   "unit": "pieza" },
  { "ingredientId": "pasta",             "qty": 0.5, "unit": "taza", "state": "cocido" },
  { "ingredientId": "tostada-horneada",  "qty": 2,   "unit": "pieza" },
  { "ingredientId": "arroz",             "qty": 0.5, "unit": "taza", "state": "cocido" },
  { "ingredientId": "quinoa",            "qty": 0.5, "unit": "taza", "state": "cocido" },
  { "ingredientId": "papa",              "qty": 0.5, "unit": "pieza", "state": "cocido" },
  { "ingredientId": "camote",            "qty": 0.5, "unit": "pieza", "state": "cocido" } ] }
```
Lo mismo para `grasa` (¼ aguacate, 1 cda aceite de oliva, ½ puñito de cacahuate…) y `proteina` (pollo, cerdo magro, res magra, pescado, atún). Va como parte del plan versionado, porque tu nutrióloga puede cambiarla.

### 3.3 Ingrediente
`id, nombre, alias[], grupo, unidad de compra (kg, pieza, lata, paquete), conversiones (taza ↔ g por estado), rendimiento crudo→cocido, pasillo, esBásicoDespensa, macros por 100 g (opcional, SMAE)`.

Tabla de rendimientos inicial (configurable; la vas afinando pesando una vez):
| Ingrediente | crudo → cocido |
|---|---|
| Pechuga de pollo | ~0.72 |
| Lomo de cerdo | ~0.70 |
| Res magra | ~0.68 |
| Filete de pescado | ~0.80 |
| Arroz | ½ tz cocido ≈ 33 g crudo |
| Pasta | ½ tz cocida ≈ 38 g seca |
| Frijol/lenteja/garbanzo | ½ tz cocido ≈ 30–35 g seco (o ⅓ lata) |

### 3.4 Receta (guisado + acompañamientos)
```json
{
  "_id": "lomo-salsa-verde-nopales",
  "name": "Lomo de cerdo en salsa verde con nopales y champiñones",
  "meal": "comida",
  "servings": 4,
  "cookingMethod": "guisado",
  "components": {
    "proteina":  { "ingredientId": "lomo-cerdo", "scaleToPlan": true },
    "verdura":   [ { "ingredientId": "nopal", "share": 0.5 }, { "ingredientId": "champinon", "share": 0.5 } ],
    "leguminosa": { "ingredientId": "frijol", "optional": true },
    "cereal":    { "default": "tortilla-maiz", "allowed": ["tortilla-maiz", "papa"] }
  },
  "fixedIngredients": [
    { "ingredientId": "tomate-verde", "qty": 500, "unit": "g" },
    { "ingredientId": "chile-serrano", "qty": 2, "unit": "pieza" },
    { "ingredientId": "ajo", "qty": 1, "unit": "diente" }
  ],
  "steps": ["…"],
  "source": "plan-ejemplo | keep-import | ia-sugerida | manual",
  "stats": { "timesCooked": 21, "lastCooked": "2026-10-20" }
}
```
- **Componentes** que escala el motor según el plan (proteína, verdura, cereal, leguminosa, grasa).
- **Ingredientes fijos** (salsa, condimentos) que no se escalan: son la "identidad" del platillo.
- `cookingMethod` se valida contra `rules.forbiddenMethods`. Una receta de Keep tipo "milanesa empanizada" se marca como **no compatible** y se ofrece una variante (ej. "milanesa al horno sin empanizar").

---

## 4. Módulos

### 4.1 Nutrition Plan
- Captura con un **formulario dinámico generado desde `nutrition-plan.schema.json`** (Blazor). Se siembra con `seed/plan-2.json` transcrito del PDF.
- Versiones: al cambiar el plan, las recetas no se tocan; los menús confirmados guardan un *snapshot* de lo que se calculó.

### 4.2 Import de Google Keep → inventario de platillos
- Keep **no tiene API pública para cuentas personales** → usar **Google Takeout** (un `.json` por nota).
- Pipeline (job idempotente por hash de nota):
  1. Parsear las notas → `RawWeeklyMenu { fecha, líneas[] }`.
  2. Claude normaliza nombres ("tinga", "tinga de pollo" → `tinga-de-pollo`), clasifica cada línea (platillo de comida / desayuno / cena / nota) y propone componentes: proteína principal, verduras, método de cocción.
  3. Agregación → `dishHistory` con frecuencia, última vez y estacionalidad.
  4. **Revisión en la UI**: fusionar duplicados, confirmar componentes, marcar no compatibles.
- Solo se manda a Claude el texto de las notas de menú (filtrado por etiqueta o patrón), no notas personales.

### 4.3 Motor de porciones (determinista)
Para cada receta y para la versión activa del plan:
```
por cada componente c del template "comida":
    porciónPlan   = elegir(min, max, proteinTarget)          // ej. 200 g cocido
    totalCocido   = porciónPlan × 4
    totalCompra   = totalCocido ÷ rendimiento(ingrediente)    // 800 ÷ 0.70 ≈ 1143 g
    redondear a unidad comprable/medible                      // 1.15 kg lomo
verduras: 2 tz × 4 = 8 tz → repartir por share → convertir taza → g por ingrediente
cereal:   1 porción × 4 → opción elegida de la tabla de intercambios → 8 tortillas
fijos:    se copian tal cual
```
Salida por receta:
- **Para cocinar** (lote de 4): cantidades crudas para comprar y cocinar.
- **Tu porción al servir**: "200 g de lomo cocido (≈ 1 taza) + 2 tz de verdura + 2 tortillas + ½ tz de frijoles". Esto es lo que te ayuda a controlar porciones.
- **Validación**: cada componente obligatorio está cubierto; método permitido. Si falta algo (ej. la receta no tiene verdura), propone un **acompañamiento** ("+ ensalada 2 tz") en vez de deformar el guisado.
- Macros estimados (opcional, informativo).

### 4.4 Menu Planning
Entrada: `{ dishCount: 5, weekOf, mustInclude?, exclude? }`.
1. Candidatos: recetas `meal = comida`, compatibles con el plan activo.
2. Puntaje: `frecuencia histórica × penalización si se cocinó hace < 3 semanas × bonus de temporada`.
3. Restricciones de variedad: máx. 2 platillos con la misma proteína, al menos 1 de pescado si hay ≥ 4 platillos (configurable), máx. 1 platillo laborioso.
4. **Regla de leguminosa (2–3 veces/semana) sobre porciones:** el motor decide en cuántas porciones de la semana va leguminosa (como componente del guisado, ej. garbanzos a la mexicana, o como acompañamiento de ½ tz de frijolitos) y lo refleja en la lista de compras.
5. Muestreo ponderado con semilla guardada → "regenerar" da otra opción y se puede reproducir.
6. **Cobertura:** muestra `N × 4 porciones` contra las comidas de la semana (ej. 5 platillos = 20 porciones vs. 7 comidas) y avisa cuánto sobra para congelar o pasar a la siguiente semana.
7. Estados: **Draft** (agregar, quitar, "cámbialo por otro con pescado") → **Confirmed** (genera la lista final y actualiza `lastCooked`).

### 4.5 Shopping List
1. Sumar cantidades crudas de todas las recetas escaladas.
2. Sumar **básicos semanales** (colación × 7: 7 tz de fruta, 3.5 tz de yogurt, chía; más desayunos y cenas habituales que configures).
3. Normalizar a unidad canónica (g, ml, pieza) y convertir a **unidad de compra**, redondeando hacia arriba (1 143 g lomo → "1.2 kg"; 8 + 8 tortillas → "½ kg tortillas").
4. Restar despensa ("ya tengo").
5. Agrupar por pasillo; exportar como checklist (PWA), texto para WhatsApp o nota de Keep (copiar/pegar).

### 4.6 AI Gateway: sugerir platillos nuevos (deseable)
- Entrada a Claude: plantilla "comida" del plan, tabla de intercambios, reglas, tu despensa frecuente (los ingredientes con más apariciones en Keep) y tus platillos favoritos como referencia de gusto.
- Salida: receta en formato `recipe.schema.json` (componentes + fijos), solo con `ingredientId` del catálogo (los nuevos se marcan para revisión).
- **Siempre pasa por el motor de porciones y la validación de reglas** antes de mostrarse. El LLM propone; el código calcula.
- Modelo sugerido: `claude-sonnet-5-5` para normalizar Keep y sugerir recetas.

---

## 5. Persistencia

### MongoDB
| Colección | Contenido | Índices |
|---|---|---|
| `nutritionPlans` | plan versionado + tabla de intercambios | `active` |
| `ingredients` | catálogo, conversiones, rendimientos, pasillo | texto en `alias`, `group` |
| `recipes` | guisados con componentes y fijos | `meal`, `components.proteina.ingredientId`, `stats.lastCooked` |
| `dishHistory` | ocurrencias importadas de Keep | `dishId`, `date` |
| `weeklyMenus` | Draft/Confirmed con snapshot de recetas escaladas + lista | `weekOf` |
| `pantry` | despensa habitual / actual | — |
| `importJobs` | estado e idempotencia del import | `hash` único |
| `aiResponses` | respuestas de Claude por hash de entrada (evita volver a pagar al reprocesar Keep) | `inputHash` único |

Validación: la API valida cada documento con los schemas de `schemas/` (JsonSchema.Net) y las referencias entre documentos con `CatalogValidator`. Aplicar además `$jsonSchema` como validador de colección en Mongo queda pendiente (Mongo usa un subconjunto distinto del estándar, sin `$ref`).

**Sin caché:** el motor de porciones es aritmética sobre unas decenas de documentos, así que se calcula en cada petición en milisegundos. Lo que antes iba en Redis queda así:
- Recetas escaladas → se calculan al vuelo; solo se **guardan como snapshot** dentro de `weeklyMenus` al confirmar.
- Borrador del menú en edición → documento `weeklyMenus` con `status: "Draft"` (sobrevive a reinicios).
- Respuestas de Claude → colección `aiResponses`.
- Locks e idempotencia del import → índice único `hash` en `importJobs` + actualización atómica de `status` (`FindOneAndUpdate`).
- Rate limit → no hace falta en local de un solo usuario.

Si algún día lo despliegas para más personas, se puede agregar `IDistributedCache` detrás de una interfaz sin cambiar el dominio.

---

## 6. API (borrador)
```
GET/PUT  /api/plans/active          POST /api/plans (nueva versión)
GET/POST/PUT/DELETE /api/recipes     GET /api/recipes/{id}/portioned
GET/PUT  /api/ingredients            GET/PUT /api/pantry      GET/PUT /api/basics
POST /api/imports/keep (zip Takeout) GET /api/imports/{id}    POST /api/imports/{id}/review
POST /api/menus/generate {dishCount}
POST /api/menus/{id}/dishes          DELETE /api/menus/{id}/dishes/{dishId}
POST /api/menus/{id}/dishes/{dishId}/swap {criteria}
POST /api/menus/{id}/confirm         GET /api/menus/{id}/shopping-list?format=json|text
POST /api/suggestions/dishes {useIngredients?}
```
Tools MCP: `generate_weekly_menu`, `swap_dish`, `add_dish`, `remove_dish`, `get_shopping_list`, `get_recipe`, `suggest_new_dish`.

### Ejemplo: "Dame un menú semanal con 5 platillos"
```
Menú semana 16-nov (plan v2, proteína 200 g)
1. Lomo de cerdo en salsa verde con nopales y champiñones + frijolitos
   Lote: 1.15 kg lomo · 600 g nopal · 300 g champiñón · 500 g tomate verde · 8 tortillas · 2 tz frijol cocido
   Tu porción: 200 g lomo cocido + 2 tz verdura + 2 tortillas + ½ tz frijoles
2. Filete de pescado al limón + puré de papa + verduras salteadas
3. Sopa de verdura + albóndigas sin grasa + arroz
4. Garbanzos a la mexicana + atún
5. Ceviche de pescado + ¼ aguacate + salmas
Leguminosa: 3 de 7 comidas ✔ · Cobertura: 20 porciones / 7 comidas → 13 para congelar
Lista de compras → 4 pasillos, 23 artículos (incluye básicos de colación)
```

---

## 7. Roadmap

| Fase | Entregable |
|---|---|
| **0** | Schemas + seed: plan v2, tabla de intercambios, ~40 ingredientes con rendimientos, los 5 ejemplos de comida del PDF como recetas |
| **1 (MVP)** | Motor de porciones + generador de N platillos + lista de compras con básicos (API + Swagger, tests) |
| **2** | Importador Keep (Takeout) con normalización vía Claude y pantalla de revisión |
| **3** | PWA Blazor (menú, recetas con "tu porción", checklist del súper) + servidor MCP |
| **4** | Sugerencia de platillos nuevos con despensa; macros informativos (SMAE) |

Riesgo principal: la precisión de las **conversiones** (taza ↔ g por verdura, rendimientos). Para reducirlo: empezar con los ~40 ingredientes que aparecen en Keep y afinar cada rendimiento pesando una vez.

---

## 7.1 Implementación de la Fase 0 + 1

Alcance: backend + datos iniciales + pruebas. Blazor, MCP, el import de Keep y las sugerencias con IA quedan para las fases 2–4.

1. **Solución** `MenuApp.sln` con `src/MenuPlanner.{Domain,Portioning,Application,Infrastructure,Api}` y `tests/MenuPlanner.{UnitTests,ApiTests,IntegrationTests}`, más `.gitignore`, `.editorconfig` y `Directory.Build.props` (net8.0, nullable, warnings as errors).
2. **Domain**: `Quantity`/`Unit` (g, ml, pieza, taza, cda, cdita, porción), `ComponentGroup`, `NutritionPlan` (`MealTemplate`, `ComponentSlot` con rango, `ExchangeList`, `WeeklyBasic`, `PlanRules`), `Ingredient` (conversiones, rendimiento, unidad de compra, pasillo), `Recipe` (componentes + fijos), `WeeklyMenu` (Draft/Confirmed + snapshot), `ShoppingList`.
3. **Schemas** `schemas/*.schema.json` (plan, receta, ingrediente), validados con JsonSchema.Net en la API y en las pruebas del seed.
4. **Seed** `seed/`: `plan-2.json` (comida, intercambios de cereal y grasa, colación como básico, reglas), 47 ingredientes con rendimientos y conversiones, las 5 comidas de ejemplo del PDF y 8 recetas semilla de cocina casera.
5. **Portioning**: `UnitConverter`, `PortionCalculator` (escala por componente, cocido → crudo, redondeo a unidad medible y comprable, porción al servir, validaciones y acompañamiento sugerido).
6. **Application**: `MenuGenerator` (puntaje, restricciones de variedad, regla de leguminosa sobre porciones, semilla, cobertura), casos de uso de menú (generar, agregar, quitar, cambiar, confirmar), `ShoppingListBuilder` (sumar, básicos, unidad de compra, despensa, pasillos, exportar a texto).
7. **Infrastructure**: `MongoContext`, repositorios, creación de índices al arrancar y carga idempotente del seed (solo en colecciones vacías).
8. **Api**: Minimal APIs de la sección 6 que entran en la fase 1 (plan, recetas, ingredientes, despensa, básicos, menús, lista de compras), Swagger y `ProblemDetails`.
9. **Pruebas**: unitarias del motor, del generador, de la lista (golden tests) y de los casos de uso con repositorios en memoria; pruebas HTTP de la API con `WebApplicationFactory`; integración contra Mongo local (se omiten si no hay servidor).
10. **README**: requisitos (.NET 8 y tu Mongo local), configuración de la cadena de conexión, cómo correr y probar, y ejemplos de `curl`.

## 8. Verificación
- **Unit tests del motor**: lomo 200 g cocido × 4 ÷ 0.70 → 1 143 g → 1.15 kg; un fijo (salsa) no escala; cereal "tortilla" → 8 piezas; receta sin verdura → sugiere acompañamiento; método "empanizado" → rechazo.
- **Regla semanal**: 5 platillos → porciones con leguminosa ∈ [2, 3].
- **Golden test de la lista**: 5 recetas semilla + básicos → lista esperada (unidades de compra y redondeo).
- **Integración** (contra tu Mongo local, base `menuplanner_test_{guid}` que se borra al terminar; si `MENUPLANNER_TEST_MONGO` no está definida o Mongo no responde, se marcan como *skipped*): generar → swap → confirmar → `lastCooked` actualizado; al publicar el plan v3, un menú nuevo usa v3 y el confirmado conserva el snapshot de v2.
- **Local**: `dotnet run --project src/MenuPlanner.Api` (el seed se carga al arrancar si la base está vacía) → Swagger → `POST /api/menus/generate {dishCount:5}`.
- **Import Keep**: muestra de 20 notas reales → revisar % de platillos bien normalizados antes de procesar los 2 años.
- **E2E**: por MCP, "Dame un menú semanal con 5 platillos" → comparar a mano 2 porciones contra la tabla del PDF.

## Supuestos
- Se usa el valor superior del rango de proteína por defecto (configurable).
- Desayuno y cena entran solo como básicos configurables en la lista, no los genera el menú.
- Los datos personales y de contacto del PDF no se guardan ni se mandan a servicios externos; solo la estructura del plan.

---

## Fase 2: importador de Keep (vía Google Docs)

### Contexto
El administrador de la organización bloqueó Takeout para Keep. Copiaste las notas a un Google Doc llamado **"Menus"**. Lo leí completo con el conector de Drive. Lo que encontré:
- **71 notas** en orden de la más reciente a la más antigua: "Noviembre 1" (nov 2026) → "Lista mayo 2" (may 2024). Se separan con `-----`.
- **Título = mes + número de semana, sin año.** Variantes: emojis (que llegan mal codificados), prefijo "Lista", "Julio - 2", meses sin semana, un "Septiembre 3" repetido.
- **Secciones fijas:** Comidas, Vegetales/Verduras/Verdura, Procesados, Carnes/Carne, Limpieza.
- **"Comidas": 386 apariciones con 177 nombres escritos de distinta forma**, que se agrupan en unos 60 platillos. Hay ortografía juguetona ("Poyo", "Mollo con Pole", "Anvorgesas", "Bongles"), combinaciones ("pollo adobado/lentejas", "teriyaki c arroz") y algunas líneas que no son platillos ("vinagre de arroz", "desayuno jueves").
- **Las listas de compras traen cantidades** ("1 kg piernil deshuesado", "3/4 kg pechuga", "3 latas de atún").

Decisiones confirmadas:
- Importar **solo alimentos** (se ignora Limpieza).
- Redacto las recetas ajustadas de los **~20 platillos más frecuentes**. Los que no cumplen el plan llevan versión adaptada y se marcan para revisión.
- **Reemplazar las 8 recetas inventadas** por las de tu historial (se conservan las 5 del plan).

### Diseño

**Cómo entra el archivo.** Descargas el Doc (Archivo → Descargar → Markdown o Texto) y lo subes con `POST /api/imports/keep`. El archivo **nunca va al repo**: `data/` queda en `.gitignore`. La normalización corre **sin LLM y sin API key**: uso un diccionario de platillos que armo ahora a partir de tu documento, más coincidencia aproximada. Lo que no coincida pasa a tu revisión. Normalizar con Claude queda como opción futura.

1. **`KeepNotesParser`** (proyecto nuevo `src/MenuPlanner.Importers`)
   - Separa notas por líneas `---`/`___`/`***` o por un título nuevo. Limpia viñetas (`- * • ● ○ ◦`), `<!-- end list -->`, negritas y emojis o mojibake.
   - Título: regex `mes [-] [semana]`. Ignora "Lista" y los emojis.
   - Secciones (sin importar mayúsculas ni acentos): comidas → platillos; carnes, vegetales/verduras/frutas y procesados → historial de compras; limpieza o sección desconocida → se ignora.
   - Cantidades: `3/4 kg pechuga`, `1kg`, `600g`, `2L de leche`, `3 latas de atún` → (cantidad, unidad, nombre).
   - **Año deducido:** se toma el año de la nota más reciente (por defecto el actual; si su mes es posterior al mes actual, el anterior; también se puede indicar con `latestYear`). Al bajar por la lista, si el mes "sube" se resta un año. Fecha = lunes de la semana N del mes; sin semana = primer lunes. Los títulos duplicados se reportan como aviso.
   - **Idempotencia:** cada nota tiene un hash (título + contenido normalizado) con índice único. Reimportar no duplica nada.
2. **`DishMatcher`**
   - Normaliza: minúsculas, sin acentos, correcciones (poyo→pollo, tortiya→tortilla, triyaki→teriyaki, koreano→coreano, anvorgesa(s)→hamburguesa, bongles→boneless, "mollo con pole"→pollo con mole, aloz→arroz, `c/`→con).
   - En combinaciones con "/" toma el primer platillo y guarda la otra parte como acompañamiento o alternativa.
   - Busca en `seed/dish-aliases.json` (unos 60 platillos canónicos con alias y una lista `ignore`). Si no lo encuentra, usa similitud aproximada (≥ 0.88) con un nivel de confianza; si tampoco, el nombre queda como *sin asignar*.
3. **Modelo nuevo en Mongo**
   - `dishes`: `{id, name, aliases[], recipeId?, compatibility: compatible|adaptada|noCompatible, stats {timesCooked, firstCooked, lastCooked, months[]}}`
   - `dishHistory`: `{noteHash, date, rawName, dishId?, side?, importId}`
   - `purchaseHistory`: `{noteHash, date, section, raw, qty?, unit?, name, ingredientId?}`
   - `importJobs`: resumen (notas nuevas y duplicadas, coincidencias, sin asignar con frecuencia y sugerencias, avisos)
   - `Recipe` gana `dishId?`. `RecipeStats` gana `historyCount` y `historyLastCooked`. `MenuGenerator.Weight` pasa a usar `timesCooked + historyCount`, la última vez cocinado más reciente y un **bonus de temporada** (×1.25 si lo cocinaste en ese mes en años anteriores).
4. **API**
   - `POST /api/imports/keep`: archivo `.md`/`.txt` o texto plano; opciones `latestYear` y `dryRun`.
   - `GET /api/imports`, `GET /api/imports/{id}`.
   - `POST /api/imports/{id}/review` con `{mappings:[{raw, dishId | newDish | ignore}]}`: guarda alias, vuelve a asignar y recalcula estadísticas.
   - `GET /api/dishes` (ordenado por frecuencia), `PUT /api/dishes/{id}` (alias, receta ligada, compatibilidad).
   - `GET /api/pantry/suggestions`: ingredientes por frecuencia de compra, ligados al catálogo por alias. Los aceptas con los `PUT` de despensa y básicos que ya existen.
   - `POST /api/seed/recipes`: inserta o actualiza las recetas del seed y borra las de origen `semilla`, para tu base que ya tiene el seed anterior.
5. **Recetas (seed)**
   - ~20 recetas nuevas que cubren cerca del 75% de lo que cocinas: alambre, enchiladas suizas, pollo teriyaki, pollo con mole, hamburguesas, enchiladas rojas, tacos de carne al vapor, sopa de tortilla, pollo ajonjolí, boneless, pollo a las hierbas, picadillo, pipián, quesatacos, pollo mostaza/honey mustard, puerco en salsa verde, pollo adobado, pollo a la naranja, pollo coreano, tinga/tostadas de pollo y shawarma.
   - Las que no cumplen el plan van **adaptadas**, con la etiqueta `revisar-nutriologa`: boneless al horno sin empanizar, hamburguesa de res magra con pan integral o lechuga, enchiladas suizas con yogurt griego y panela, quesatacos con panela.
   - Se agregan los ingredientes que faltan: pierna de cerdo, paleta de res, tortilla de harina integral, yogurt griego, queso panela, salsa de soya, ajonjolí, col napa, cebollín, pasta de mole y pipián, chile guajillo, jengibre, mostaza, miel…
   - Se quitan las 8 recetas inventadas (`source: semilla`).
6. **Privacidad:** el Doc no se versiona. Las pruebas usan un archivo de muestra corto, sintético, con el mismo formato.

### Archivos
- Nuevos:
  - `src/MenuPlanner.Importers/{KeepNotesParser,DishMatcher,TextNormalizer,QuantityParser}.cs`
  - `src/MenuPlanner.Domain/Dish.cs`
  - `src/MenuPlanner.Application/Imports/ImportService.cs`
  - `src/MenuPlanner.Api/Endpoints/ImportEndpoints.cs`
  - `seed/dish-aliases.json`
  - `schemas/dish.schema.json`
- Cambios:
  - `seed/recipes.json`, `seed/ingredients.json`
  - `MenuGenerator.Weight`
  - `Recipe` / `RecipeStats`
  - `MongoContext` (colecciones e índices)
  - Repositorios, `DependencyInjection`, `.gitignore` (`data/`), README

### Verificación
- Unitarias:
  - Parser: títulos con emojis, "Lista Julio - 2", sin semana, secciones variantes, viñetas de `.md` y `.txt`, cantidades.
  - Inferencia de año: cruce dic→nov y el orden completo de 71 títulos con la secuencia real.
  - Matcher: correcciones, alias, aproximados, combinaciones, ignorados.
  - Idempotencia del import y flujo de revisión.
  - Pesos y temporada en el generador.
  - Las pruebas del seed que ya existen validan las nuevas recetas: schema, catálogo y porcionado sin avisos.
- API (`WebApplicationFactory`): import, revisión, platillos y sugerencias de despensa.
- Integración con Mongo: ida y vuelta del import.
- **Validación con tu documento real**, aquí en la sesión y sin versionarlo: correr el parser y el matcher sobre el contenido leído de Drive. Se espera 71 notas, ~386 apariciones, ≥ 90% asignadas y reimportar = 0 nuevas. Te reporto la tasa y la lista de nombres sin asignar.
- Local: descargar "Menus" como `.md`, luego `curl -F file=@Menus.md http://localhost:5033/api/imports/keep`, y generar un menú de 5 donde tus favoritos aparezcan más.

### Resultado con tu documento real
- 71 notas (13 may 2024 → 2 nov 2026), 386 líneas de "Comidas": **376 asignadas (100%)**, 10 ignoradas, 0 sin asignar.
- 1 216 renglones de compras; 69% quedan ligados a un ingrediente del catálogo.
- Las recetas del seed cubren el **73%** de lo que cocinas (274 de 376).
- Un aviso: hay dos notas tituladas "Septiembre 3" (2026); se importan ambas.
- Cambios de criterio durante la implementación:
  - `MaxSameProtein` pasó de 2 a 3: el pollo domina tu historial.
  - El generador ahora asegura que haya platillos con leguminosa suficiente para cumplir 2–3 veces por semana.

---

## Fase 3: app web (Blazor + MudBlazor) y servidor MCP para Claude Desktop

### Contexto
El backend ya hace todo: plan, recetas, porciones, menú, lista de compras e import de Keep. Pero hoy solo se usa con `curl` o Swagger. La fase 3 agrega:
- **Una app web local** para usarlo cómodo.
- **Un servidor MCP** para pedir "Dame un menú semanal con 5 platillos" desde Claude Desktop.

Decisiones confirmadas:
- **Solo en la computadora** (localhost): sin PWA ni acceso por la red, y sin PIN.
- **UI con MudBlazor.**
- **MCP para Claude Desktop** (stdio).

Versiones verificadas en NuGet (todas compatibles con .NET 8): MudBlazor 9.13.0, ModelContextProtocol 2.4.0, bUnit 2.15.2, Microsoft.AspNetCore.Components.WebAssembly 8.0.x.

### Diseño

### 1. Contratos compartidos (`src/MenuPlanner.Contracts`, nuevo)
- Mover aquí los records de entrada y salida que hoy viven en Application, para que la app web no dependa de Application, Mongo ni JsonSchema:
  - `MenuView`, `MenuDishView`, `MenuSummary`, `MenuListItem`
  - `GenerateMenuRequest`, `SwapCriteria`, `AddDishRequest`
  - `DishView`, `PantrySuggestion`, `ReviewRequest`, `ReviewMapping`
  - `SeedSyncResult`
- Referencia solo a Domain. Application, Api y Web lo referencian. El cambio es mecánico (namespaces).

### 2. App web (`src/MenuPlanner.Web`, Blazor WebAssembly + MudBlazor)
- **Servida por la misma API** (`UseBlazorFrameworkFiles` + `MapFallbackToFile("index.html")`): un solo `dotnet run`, mismo origen y sin CORS. La raíz `/` abre la app; Swagger sigue en `/swagger`.
- **`ApiClient`** tipado sobre `HttpClient` con `AppJson.Options` (se mueve a Contracts). Convierte `ProblemDetails` en avisos (`ISnackbar`).
- **Páginas** (menú lateral, adaptable):
  1. **Menú** (`/`): elegir N (1–14), semana e incluir/excluir platillos. Cada platillo se muestra en una tarjeta con "tu porción", el lote y sugerencias, y botones para cambiar (filtro por proteína o etiqueta), quitar o agregar. Hay resumen de leguminosa y cobertura, avisos, y botones para regenerar y confirmar. Incluye el historial de menús.
  2. **Lista de compras** (`/menus/{id}/compras`): checklist por pasillo, con las palomitas guardadas en `localStorage` por menú. Sección "ya tienes" y botón para **copiar como texto** (WhatsApp/Keep).
  3. **Recetas**: lista con filtros (proteína, etiqueta, `revisar-nutriologa`, veces cocinada). El detalle muestra lo porcionado, con selector de proteína mín/medio/máx y leguminosa. Se editan con el formulario dinámico.
  4. **Platillos** (historial): frecuencia, última vez, mapa de meses, receta ligada y compatibilidad. Se editan con el formulario dinámico.
  5. **Importar Keep**: subir `.md`/`.txt`, revisar en seco (`dryRun`) y luego importar. Tabla de "sin asignar" y "aproximados" con acción por renglón (asignar a platillo, crear nuevo o ignorar) que llama a `review`.
  6. **Despensa**: sugerencias con botones "a despensa" y "a básicos", y edición de despensa y básicos.
  7. **Plan**: versión activa, editar o crear versión con el formulario dinámico, activar.
  8. **Ingredientes**: tabla y edición con el formulario dinámico.
- **Formulario dinámico desde JSON Schema** (`Components/SchemaForm.razor` + `SchemaFormModel`):
  - Descarga el schema de `/api/schemas/{name}` y genera el formulario sobre un `JsonObject`.
  - Soporta:
    - tipos: `object`, `array` (agregar, quitar y reordenar), `string`, `number`/`integer`, `boolean`
    - `enum` (select), `type` con `null`, `$ref` a `#/$defs`, `anyOf` con null
    - `title`/`description`, `default`, `minimum`/`maximum`, `readOnly` (se muestra sin editar), `required`
  - Valida **en el cliente con JsonSchema.Net** (mismo schema que el servidor) y muestra los errores junto al campo por `instanceLocation`. El servidor vuelve a validar (incluye las referencias del catálogo).
  - Las listas de ids (`ingredientId`, `recipeId`) usan autocompletado con el catálogo (`x-ref`: el editor de recetas y platillos pasa un proveedor de opciones por nombre de campo).
- **Sin PWA ni service worker** (decisión: solo localhost).

### 3. Servidor MCP (`src/MenuPlanner.Mcp`, consola stdio)
- `ModelContextProtocol` 2.4: `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`. **Logs a stderr** (stdout es el protocolo).
- **Usa los mismos servicios en proceso** (Application + Infrastructure contra tu Mongo, con `appsettings.json` propio y `Mongo__ConnectionString`). No requiere que la API esté corriendo.
- **Tools** (descripciones en español, salida en texto legible con los ids para encadenar):
  - `generate_weekly_menu(dishCount, weekOf?, include?, exclude?, seed?)`: usa `MenuTextFormatter` y devuelve el id.
  - `get_menu(menuId?)`: sin id, el borrador más reciente.
  - `swap_dish(menuId, dish, proteinFamily?, tag?)`, `add_dish(menuId, dish)`, `remove_dish(menuId, dish)`, `confirm_menu(menuId)`.
  - `get_shopping_list(menuId)`: texto por pasillo (`ShoppingListBuilder.ToText`).
  - `get_recipe(dish, proteinTarget?)`: receta porcionada (lote + tu porción + pasos).
  - `list_recipes(filter?)` y `list_favorites(top?)`: historial de Keep.
  - `pantry_suggestions(months?)`.
- **`DishResolver`** (Application): el usuario escribe "las enchiladas", "tinga", "Poyo ajonjolí". Se resuelve con `DishMatcher` (alias de Keep → `recipeId`), luego id o nombre exacto de receta, y si no, similitud. Cuando hay ambigüedad devuelve las opciones en el texto.
- `docs/claude-desktop.md`: bloque para `claude_desktop_config.json` (`command: dotnet`, `args: [<ruta>/MenuPlanner.Mcp.dll]`, `env: Mongo__ConnectionString`) y cómo publicar con `dotnet publish -c Release`.

### 4. Modo memoria (para probar sin Mongo)
- Mover `InMemoryStore` de las pruebas a `MenuPlanner.Infrastructure/Memory/InMemoryRepositories.cs`. Se carga desde el seed al iniciar.
- Se activa con `Storage:Provider = Memory` (por defecto `Mongo`). Sirve para demos, para las pruebas E2E y para que yo pueda verificar aquí la UI y el MCP, porque en este contenedor no hay Mongo. Las pruebas unitarias pasan a usarlo.

### Archivos
- Nuevos:
  - `src/MenuPlanner.Contracts/*`
  - `src/MenuPlanner.Web/` (`Program.cs`, `ApiClient.cs`, `Layout/`, `Pages/*.razor`, `Components/SchemaForm*.razor(.cs)`, `wwwroot/`)
  - `src/MenuPlanner.Mcp/` (`Program.cs`, `MenuTools.cs`, `RecipeTools.cs`, `appsettings.json`)
  - `src/MenuPlanner.Application/Menus/DishResolver.cs`
  - `src/MenuPlanner.Infrastructure/Memory/InMemoryRepositories.cs`
  - `tests/MenuPlanner.WebTests` (bUnit), `tests/MenuPlanner.McpTests`
  - `docs/claude-desktop.md`
- Cambios:
  - `Program.cs` de la API (hospedar Web, proveedor de almacenamiento)
  - `MenuService` / endpoints (usings de Contracts)
  - `InMemoryStore` (se mueve)
  - README

### Verificación
- **bUnit:**
  - `SchemaForm` genera campos para los 4 schemas reales (enum → select, array → agregar/quitar, `readOnly` deshabilitado), edita y produce un JSON que valida contra el schema. Muestra los errores en el campo correcto.
  - Checklist de compras (persistencia en `localStorage` simulada).
  - Página de menú con `ApiClient` simulado.
- **MCP:**
  - Pruebas de cada tool con el modo memoria (texto con id, cambiar, confirmar, lista, receta).
  - `DishResolver` ("enchiladas" → enchiladas-suizas, "poyo ajonjolí", ambiguo).
  - **E2E por stdio con el cliente del SDK** (`McpClient` + `StdioClientTransport`) lanzando el servidor compilado en modo memoria: `ListTools` y `generate_weekly_menu(5)`.
- **UI de punta a punta en este contenedor:** API en modo memoria + Playwright (Chromium ya instalado). Generar menú de 5 → cambiar un platillo → confirmar → lista de compras → importar el fixture de muestra → revisar. Capturas de pantalla para ti.
- Las pruebas existentes (unitarias, API e integración) siguen pasando; las de Mongo las corres tú con `MENUPLANNER_TEST_MONGO`.

### Resultado
- La API sirve la app web: `dotnet run` → http://localhost:5033 (Swagger en `/swagger`).
- Recorrido E2E con Playwright en modo memoria, sin errores en consola: generar 5 → cambiar a pescado → lista (palomitas persisten al recargar) → confirmar → receta → editar receta con el formulario dinámico (validación en el cliente y guardado) → importar el fixture → revisar → platillos → plan → despensa.
- Servidor MCP probado por stdio con el cliente oficial: 12 herramientas.
- Cambios durante la implementación:
  - `DishResolver` usa la coincidencia aproximada del historial solo como último recurso, para que "lomo en salsa verde" no termine en "pollo en salsa verde".
  - El formulario lee números tanto enteros como decimales.
  - Los schemas ganaron títulos en español, que también son las etiquetas del formulario.
