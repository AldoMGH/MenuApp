# MenuApp: planificador de menú semanal

Genera tu menú semanal de **comidas** a partir de tu plan de alimentación, con platillos de **4 porciones**, la **lista de compras** agrupada por pasillo y las **recetas con cantidades ajustadas al plan** (lote para cocinar + tu porción al servir).

- Stack: .NET 8 (C#) · MongoDB local · Minimal APIs + Swagger
- **¿Primera vez? Empieza aquí: [`docs/guia-de-inicio.md`](docs/guia-de-inicio.md)** (instalación, primer arranque, importar Keep, primer menú y Claude Desktop)
- Arquitectura y decisiones de diseño: [`docs/arquitectura.md`](docs/arquitectura.md)

> Estado: **Fases 0 a 4 completas**: backend, motor de porciones, menú, lista de compras, importador de Google Keep, app web (Blazor + MudBlazor), servidor MCP para Claude Desktop, **platillos nuevos con lo que sueles tener** y **macros aproximados**.

![Menú de la semana](docs/img/01-menu.png)

## Cómo funciona

El plan no se basa en macros: es una **plantilla por tiempo de comida**. Para la comida pide 2 tz de verdura, 150–200 g de proteína magra **cocida**, 1 porción de cereal (2 tortillas, ½ tz de arroz cocido…) y ½ tz de leguminosa 2–3 veces por semana.

1. **Motor de porciones** (`MenuPlanner.Portioning`): para cada componente de la receta calcula
   `lote = porción del plan × 4 ÷ rendimiento crudo→cocido` y redondea a cantidades medibles.
   Ejemplo: 200 g de lomo cocido × 4 ÷ 0.70 = **1.15 kg de lomo crudo**. Los ingredientes fijos (salsa, condimentos) no se escalan.
2. **Generador** (`MenuGenerator`): elige N platillos por muestreo ponderado (frecuencia × penalización si se cocinó hace menos de 3 semanas) con reglas de variedad. Usa una semilla, así que el mismo pedido da el mismo menú.
3. **Leguminosa por porciones** (`LegumeBalancer`): reparte "2–3 veces por semana" entre las porciones del lote. Usa guisados que ya la incluyen o le agrega frijolitos como acompañamiento a algunas porciones.
4. **Lista de compras** (`ShoppingListBuilder`): suma lo que piden las recetas y los básicos diarios (colación × 7), convierte a unidad de compra redondeando hacia arriba (tortillas de 250 en 250 g, carne de 50 en 50 g…), separa lo que tienes en la despensa y agrupa por pasillo.

## Importar tu historial de Keep (fase 2)

Takeout está bloqueado para tu cuenta, así que el historial entra por **Google Docs**:

1. En Keep, selecciona tus notas de menú → ⋮ → **Copiar en Documentos de Google** (ya lo hiciste: documento "Menus").
2. En el documento: **Archivo → Descargar → Markdown (.md)** o **Texto sin formato (.txt)**.
3. Guárdalo en `data/` (esa carpeta está en `.gitignore`: tus notas nunca se suben al repo).
4. Primero revisa sin guardar nada (`dryRun=true`):

```bash
curl -s -F "file=@data/Menus.md" "http://localhost:5033/api/imports/keep?dryRun=true"
```

5. Importa en serio:

```bash
curl -s -F "file=@data/Menus.md" "http://localhost:5033/api/imports/keep"
```

**Qué hace:**
- **Notas:** lee cada nota (título "Mes N" = semana del mes) y **deduce el año** por el orden: la primera es la más reciente. Si hace falta, indícalo con `latestYear=2026`.
- **Platillos:** las líneas de "Comidas" se asignan con el diccionario `seed/dish-aliases.json`, armado a partir de tus 177 formas de escribir unos 66 platillos ("Poyo ajonjolí", "Mollo con Pole", "Anvorgesas", "Bongles/Alitas"…). Con tu documento real: **376 de 376 asignados**; las 10 líneas que no son platillos se ignoran ("vinagre de arroz", "desayuno jueves"…).
- **Compras:** guarda Carnes, Verduras y Procesados como historial (con cantidades: "3/4 kg pechuga" → 750 g). Limpieza se ignora.
- **Idempotente:** reimportar el mismo documento (en `.md` o `.txt`) no duplica nada; solo agrega notas nuevas.
- **Estadísticas:** cada platillo ligado a una receta suma su frecuencia, última vez y meses del año. El generador las usa: tus favoritos salen más, lo que cocinaste hace menos de 3 semanas sale menos, y hay un bonus de temporada.

**Revisión** (si aparecen nombres sin asignar):

```bash
curl -s "http://localhost:5033/api/imports/{importId}"
```

```bash
curl -s -X POST "http://localhost:5033/api/imports/{importId}/review" -H "Content-Type: application/json" -d '{"mappings":[{"name":"pozole verde","newDishName":"Pozole verde"},{"name":"alambreh","dishId":"alambre"},{"name":"desayuno viernes","ignore":true}]}'
```

**Tus platillos y la despensa:**

```bash
curl -s "http://localhost:5033/api/dishes"
```

```bash
curl -s "http://localhost:5033/api/pantry/suggestions?months=6&minShare=0.4"
```

Para ligar un platillo a una receta o marcar su compatibilidad: `PUT /api/dishes/{id}` (valida con `schemas/dish.schema.json`).

**Si ya tenías la base de la fase 1**, trae las recetas e ingredientes nuevos (no pisa tus datos; quita las 8 recetas de arranque que inventé):

```bash
curl -s -X POST "http://localhost:5033/api/seed/sync"
```

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

- **App web:** http://localhost:5033, o https://localhost:7259 con el perfil `https`. La API la sirve; un solo proceso. **Arranca siempre `MenuPlanner.Api`**, no `MenuPlanner.Web` por separado (en Visual Studio: proyecto de inicio = `MenuPlanner.Api`).
- **Swagger:** http://localhost:5033/swagger

Para probar **sin MongoDB** (datos de ejemplo en memoria que se pierden al cerrar):

```bash
dotnet run --project src/MenuPlanner.Api -- --Storage:Provider=Memory
```

## App web

| Página | Qué haces ahí |
|---|---|
| **Menú de la semana** | Generar N platillos (incluir/excluir), ver tu porción y el lote, cambiar por otra proteína, quitar, agregar, otra propuesta y confirmar |
| **Lista de compras** | Checklist por pasillo (las palomitas se guardan en el navegador), "ya tienes", copiar como texto |
| **Recetas** | Buscar y filtrar; detalle porcionado con proteína mín/medio/máx y frijolitos |
| **Mis platillos** | Tu historial de Keep: veces, última vez, meses y receta ligada |
| **Importar Keep** | Subir el `.md`/`.txt`, revisar sin guardar, importar y resolver los nombres pendientes |
| **Despensa y básicos** | Lo que siempre tienes, lo que compras seguido y tus básicos semanales |
| **Plan** | Tu plato por componentes, tablas de intercambio, reglas, versiones y sincronizar el seed |
| **Ingredientes** | Rendimientos, equivalencias y cómo se compra cada uno |

**Formularios dinámicos:** recetas, platillos, ingredientes y planes se editan con un formulario que se **genera a partir del JSON Schema** (`schemas/*.schema.json`):
- Soporta objetos anidados, arreglos (agregar, quitar y reordenar), enums, `$ref`, tipos con `null`, `readOnly` y autocompletado de ids del catálogo.
- Valida en el navegador con el mismo schema que la API (JsonSchema.Net) y muestra cada error junto a su campo.
- El servidor vuelve a validar, incluidas las referencias entre documentos.

| Lista de compras | Editor generado desde JSON Schema |
|---|---|
| ![Lista de compras](docs/img/02-compras.png) | ![Editor de receta](docs/img/04-editor-receta.png) |

## Platillos nuevos y macros (fase 4)

**¿Qué puedo cocinar con lo que tengo?** (Despensa en la app, o `cookable_recipes` en Claude): ordena tus recetas por qué tanto de sus ingredientes ya sueles tener. Cuenta tu despensa, tus básicos y lo que compras en al menos 40% de las semanas según Keep. Además te dice qué falta comprar.

**Platillos nuevos con Claude Desktop** (sin API key ni costo extra): usa el prompt **`sugerir_platillo`** o pídelo con tus palabras ("propónme algo nuevo con pescado"). El proceso es:
1. Claude lee el contexto: tu plato del plan, los intercambios, las reglas, lo que sueles tener con sus ids y tus gustos.
2. Propone una receta y la revisa con `preview_recipe`. La app la valida con el mismo JSON Schema y las reglas del plan, calcula tu porción, el lote, los macros y lo que falta comprar. **No guarda nada.**
3. Solo si la aceptas, `save_recipe` la guarda con las etiquetas `ia-sugerida` y `revisar-nutriologa`, la agrega a tus platillos y, si quieres, al menú en borrador.

El LLM propone; el código calcula y valida. Si Claude usa un ingrediente que no existe, la app le sugiere los más parecidos del catálogo.

**Macros aproximados:** kcal, proteína, carbohidratos, grasa y fibra por porción y por lote. Se ven en la receta, en cada tarjeta del menú, como promedio del menú y en las respuestas del MCP. Salen de valores de referencia SMAE/USDA por 100 g en crudo, que se editan en cada ingrediente. **Son informativos:** tu plan se sigue por porciones, no por calorías.

| Con lo que tienes | Macros por receta |
|---|---|
| ![Cocinables](docs/img/12-cookable.png) | ![Macros](docs/img/11-macros.png) |

## Claude Desktop (MCP)

Pide "Dame un menú semanal con 5 platillos" desde Claude Desktop. El servidor `src/MenuPlanner.Mcp` expone 16 herramientas (generar, cambiar, agregar, quitar, confirmar, lista de compras, recetas, favoritos, despensa, cocinables y el ciclo para crear platillos nuevos) y el prompt `sugerir_platillo`. Corre por stdio contra tu misma base de Mongo. Configuración paso a paso: [`docs/claude-desktop.md`](docs/claude-desktop.md).

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
| Despensa y básicos | `GET/PUT /api/pantry`, `GET /api/pantry/suggestions`, `GET/PUT /api/basics` |
| Importar Keep | `POST /api/imports/keep`, `GET /api/imports`, `GET /api/imports/{id}`, `POST /api/imports/{id}/review` |
| Platillos (historial) | `GET /api/dishes?sort=frequency|name`, `GET/PUT /api/dishes/{id}` |
| Seed | `POST /api/seed/sync` |
| Schemas | `GET /api/schemas/{nutrition-plan|recipe|ingredient|dish}` (para formularios dinámicos) |

Los `PUT` de plan, receta e ingrediente se validan contra `schemas/*.schema.json`. Además se revisa que los ingredientes existan y que el cereal y la grasa estén en la tabla de intercambios del plan.

## Datos

| Carpeta | Contenido |
|---|---|
| `schemas/` | JSON Schema (draft 2020-12) de plan, receta e ingrediente. La misma fuente sirve para la API, el seed y los futuros formularios. |
| `seed/plan-2.json` | Estructura del plan v2 (sin datos personales): plantilla de la comida, tabla de intercambios, colación y reglas. |
| `seed/ingredients.json` | 69 ingredientes con conversiones (taza ↔ g), rendimiento crudo→cocido, unidad de compra, pasillo y alias para ligar tus listas de compras. **Los rendimientos son aproximados: afínalos pesando una vez.** |
| `seed/recipes.json` | 25 recetas: las 5 comidas de ejemplo del plan y 20 de tus platillos más frecuentes, ajustadas al plan. Cubren el 73% de lo que cocinas. Las adaptadas (boneless al horno, hamburguesa en lechuga, enchiladas suizas con yogurt griego…) llevan la etiqueta `revisar-nutriologa`. |
| `seed/dish-aliases.json` | Diccionario de platillos: unos 66 platillos con sus alias, correcciones de ortografía y líneas a ignorar. |

Cuando la nutrióloga cambie el plan, crea una versión nueva (`PUT /api/plans/plan-3` con `"active": true`). Los menús confirmados conservan el cálculo de la versión con la que se hicieron.

## Ajustes del generador

En `appsettings.json` → `MenuGeneration`:

| Ajuste | Valor por defecto | Qué controla |
|---|---|---|
| `MaxSameProtein` | 3 | Máximo de platillos con la misma proteína por menú |
| `MaxLaborious` | 1 | Máximo de platillos laboriosos por menú |
| `RequireFishFrom` | 4 | Desde cuántos platillos se pide al menos uno de pescado |
| `RecentDays` | 21 | Días en que un platillo cuenta como "cocinado hace poco" |
| `RecentPenalty` | 0.2 | Multiplicador del peso si se cocinó hace poco |
| `SeasonBonus` | 1.25 | Multiplicador si lo cocinaste en ese mes en años anteriores |

## Pruebas

Todas las pruebas:

```bash
dotnet test
```

- `MenuPlanner.UnitTests`: motor de porciones, conversiones, generador, regla de leguminosa, lista de compras (golden test), validez del seed contra los schemas y casos de uso con repositorios en memoria. También cubre el importador de Keep: títulos, años, dos formatos de exportación, cantidades, asignación, revisión, idempotencia y despensa. Si guardas tu documento en `data/Menus-drive.md`, una prueba extra lo valida completo.
- `MenuPlanner.ApiTests`: la API HTTP real con `WebApplicationFactory` y repositorios en memoria.
- `MenuPlanner.WebTests` (bUnit):
  - el formulario dinámico con los 4 schemas reales: dibujo, validación por campo, arreglos y campos bloqueados;
  - la lista de compras con palomitas en `localStorage`.
- `MenuPlanner.McpTests`:
  - cada herramienta con el modo memoria;
  - el resolutor de nombres ("las enchiladas", "poyo ajonjolí");
  - una sesión de punta a punta por stdio con el cliente oficial del SDK de MCP.
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
  MenuPlanner.Importers/       Parser de notas de Keep, cantidades y asignación de platillos e ingredientes
  MenuPlanner.Application/     Generador, leguminosa, lista de compras, importación, casos de uso, schemas, seed
  MenuPlanner.Infrastructure/  MongoDB (repositorios, índices, carga del seed) y modo memoria
  MenuPlanner.Contracts/       Tipos compartidos entre API, app web y MCP (vistas, peticiones, JSON)
  MenuPlanner.Api/             Minimal APIs + Swagger; también sirve la app web
  MenuPlanner.Web/             Blazor WebAssembly + MudBlazor; formulario dinámico desde JSON Schema
  MenuPlanner.Mcp/             Servidor MCP (stdio) para Claude Desktop
tests/
  MenuPlanner.UnitTests/  MenuPlanner.ApiTests/  MenuPlanner.WebTests/  MenuPlanner.McpTests/  MenuPlanner.IntegrationTests/
schemas/  seed/  docs/  data/ (tus notas; no se versiona)
```
