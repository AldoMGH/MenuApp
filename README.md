# MenuApp: planificador de menú semanal

Genera tu menú semanal de **comidas** a partir de tu plan de alimentación, con platillos de **4 porciones**, la **lista de compras** agrupada por pasillo y las **recetas con cantidades ajustadas al plan** (lote para cocinar + tu porción al servir).

- Stack: .NET 8 (C#) · MongoDB local · Minimal APIs + Swagger
- Arquitectura y decisiones de diseño: [`docs/arquitectura.md`](docs/arquitectura.md)

> Estado: **Fase 0 + 1 (MVP de backend)**. Pendientes: importador de Google Keep, PWA en Blazor, servidor MCP y sugerencia de platillos nuevos con IA (ver roadmap en la documentación).

## Cómo funciona

El plan no se basa en macros: es una **plantilla por tiempo de comida**. Para la comida pide 2 tz de verdura, 150–200 g de proteína magra **cocida**, 1 porción de cereal (2 tortillas, ½ tz de arroz cocido…) y ½ tz de leguminosa 2–3 veces por semana.

1. **Motor de porciones** (`MenuPlanner.Portioning`): para cada componente de la receta calcula
   `lote = porción del plan × 4 ÷ rendimiento crudo→cocido` y redondea a cantidades medibles.
   Ejemplo: 200 g de lomo cocido × 4 ÷ 0.70 = **1.15 kg de lomo crudo**. Los ingredientes fijos (salsa, condimentos) no se escalan.
2. **Generador** (`MenuGenerator`): elige N platillos por muestreo ponderado (frecuencia × penalización si se cocinó hace menos de 3 semanas) con reglas de variedad. Usa una semilla, así que el mismo pedido da el mismo menú.
3. **Leguminosa por porciones** (`LegumeBalancer`): reparte "2–3 veces por semana" entre las porciones del lote. Usa guisados que ya la incluyen o le agrega frijolitos como acompañamiento a algunas porciones.
4. **Lista de compras** (`ShoppingListBuilder`): suma lo que piden las recetas y los básicos diarios (colación × 7), convierte a unidad de compra redondeando hacia arriba (tortillas de 250 en 250 g, carne de 50 en 50 g…), separa lo que tienes en la despensa y agrupa por pasillo.

## Requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Tu servidor **MongoDB local** (por defecto `mongodb://localhost:27017`, base `menuplanner`)

## Configuración

`src/MenuPlanner.Api/appsettings.json`:

```json
"Mongo": { "ConnectionString": "mongodb://localhost:27017", "Database": "menuplanner" },
"Seed":  { "Enabled": true }
```

Para cambiar la conexión sin tocar el archivo:

```bash
dotnet user-secrets set "Mongo:ConnectionString" "mongodb://usuario:clave@localhost:27017" --project src/MenuPlanner.Api
```

o con la variable de entorno `Mongo__ConnectionString`.

Al arrancar, la API crea los índices y **carga el seed solo en colecciones vacías**. Nunca sobrescribe tus datos.

## Ejecutar

```bash
dotnet run --project src/MenuPlanner.Api
```

Swagger: http://localhost:5033/swagger

### Ejemplos

Dame un menú semanal con 5 platillos (resumen legible):

```bash
curl -s -X POST "http://localhost:5033/api/menus/generate?format=text" -H "Content-Type: application/json" -d '{"dishCount": 5}'
```

La misma petición sin `format=text` devuelve JSON, con el `id` del menú para los siguientes pasos.

Lista de compras para copiar a WhatsApp o Keep:

```bash
curl -s "http://localhost:5033/api/menus/{id}/shopping-list?format=text"
```

Cambiar un platillo por otro de pescado:

```bash
curl -s -X POST "http://localhost:5033/api/menus/{id}/dishes/{recipeId}/swap?format=text" -H "Content-Type: application/json" -d '{"proteinFamily": "pescado"}'
```

Agregar o quitar un platillo:

```bash
curl -s -X POST "http://localhost:5033/api/menus/{id}/dishes" -H "Content-Type: application/json" -d '{"recipeId": "tinga-pollo"}'
```

```bash
curl -s -X DELETE "http://localhost:5033/api/menus/{id}/dishes/tinga-pollo"
```

Confirmar (congela recetas y lista, y actualiza el historial de cada platillo):

```bash
curl -s -X POST "http://localhost:5033/api/menus/{id}/confirm"
```

Receta porcionada según el plan (opcional: `proteinTarget=min|mid|max`):

```bash
curl -s "http://localhost:5033/api/recipes/lomo-salsa-verde-nopales/portioned?proteinTarget=max"
```

Marcar lo que siempre tienes en casa:

```bash
curl -s -X PUT "http://localhost:5033/api/pantry" -H "Content-Type: application/json" -d '{"ingredientIds": ["aceite-oliva", "ajo", "chia"]}'
```

### Endpoints

| Recurso | Endpoints |
|---|---|
| Plan | `GET /api/plans`, `GET /api/plans/active`, `GET/PUT /api/plans/{id}`, `POST /api/plans/{id}/activate` |
| Ingredientes | `GET /api/ingredients`, `GET/PUT/DELETE /api/ingredients/{id}` |
| Recetas | `GET /api/recipes?meal=comida`, `GET/PUT/DELETE /api/recipes/{id}`, `GET /api/recipes/{id}/portioned` |
| Menús | `POST /api/menus/generate`, `GET /api/menus`, `GET /api/menus/{id}`, `POST /api/menus/{id}/dishes`, `DELETE /api/menus/{id}/dishes/{recipeId}`, `POST /api/menus/{id}/dishes/{recipeId}/swap`, `POST /api/menus/{id}/confirm`, `GET /api/menus/{id}/shopping-list` |
| Despensa y básicos | `GET/PUT /api/pantry`, `GET/PUT /api/basics` |
| Schemas | `GET /api/schemas/{nutrition-plan|recipe|ingredient}` (para formularios dinámicos) |

Los `PUT` de plan, receta e ingrediente se validan contra `schemas/*.schema.json`. Además se revisa que los ingredientes existan y que el cereal y la grasa estén en la tabla de intercambios del plan.

## Datos

| Carpeta | Contenido |
|---|---|
| `schemas/` | JSON Schema (draft 2020-12) de plan, receta e ingrediente. La misma fuente sirve para la API, el seed y los futuros formularios. |
| `seed/plan-2.json` | Estructura del plan v2 (sin datos personales): plantilla de la comida, tabla de intercambios, colación y reglas. |
| `seed/ingredients.json` | 47 ingredientes con conversiones (taza ↔ g), rendimiento crudo→cocido, unidad de compra y pasillo. **Los rendimientos son aproximados: afínalos pesando una vez.** |
| `seed/recipes.json` | Las 5 comidas de ejemplo del plan y 8 recetas caseras de arranque. Se reemplazarán o completarán con tu historial de Keep. |

Cuando la nutrióloga cambie el plan, crea una versión nueva (`PUT /api/plans/plan-3` con `"active": true`). Los menús confirmados conservan el cálculo de la versión con la que se hicieron.

## Pruebas

Todas las pruebas:

```bash
dotnet test
```

- `MenuPlanner.UnitTests`: motor de porciones, conversiones, generador, regla de leguminosa, lista de compras (golden test), validez del seed contra los schemas y casos de uso con repositorios en memoria.
- `MenuPlanner.ApiTests`: la API HTTP real con `WebApplicationFactory` y repositorios en memoria.
- `MenuPlanner.IntegrationTests`: contra tu Mongo local, en una base temporal `menuplanner_test_*` que se borra al terminar. **Se omiten si no defines la variable.**

Para correr también las de integración (Linux/macOS):

```bash
MENUPLANNER_TEST_MONGO="mongodb://localhost:27017" dotnet test
```

En PowerShell:

```powershell
$env:MENUPLANNER_TEST_MONGO = "mongodb://localhost:27017"; dotnet test
```

## Estructura

```
src/
  MenuPlanner.Domain/          Entidades: plan, ingrediente, receta, menú, lista
  MenuPlanner.Portioning/      Conversión de unidades, redondeo y motor de porciones
  MenuPlanner.Application/     Generador, leguminosa, lista de compras, casos de uso, schemas, seed
  MenuPlanner.Infrastructure/  MongoDB (repositorios, índices, carga del seed)
  MenuPlanner.Api/             Minimal APIs + Swagger
tests/
  MenuPlanner.UnitTests/  MenuPlanner.ApiTests/  MenuPlanner.IntegrationTests/
schemas/  seed/  docs/
```
