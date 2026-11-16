# Usar el planificador desde Claude Desktop (MCP)

El servidor MCP (`src/MenuPlanner.Mcp`) expone el planificador como herramientas para Claude. Corre en tu computadora por **stdio**: Claude Desktop lo lanza y se comunica con él. No abre puertos ni expone nada a internet. Usa la misma base de MongoDB que la app web y **no necesita que la API esté corriendo**.

## 1. Publicar el servidor

```bash
dotnet publish src/MenuPlanner.Mcp -c Release -o publish/mcp
```

Esto deja `publish/mcp/MenuPlanner.Mcp.dll` junto con `appsettings.json` y la carpeta `seed/`.

## 2. Configurar Claude Desktop

Abre **Claude Desktop → Settings → Developer → Edit Config**. Se abre `claude_desktop_config.json`:
- **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`

Agrega el servidor (usa la ruta absoluta de tu carpeta `publish/mcp`):

```json
{
  "mcpServers": {
    "menu-planner": {
      "command": "dotnet",
      "args": ["/RUTA/A/MenuApp/publish/mcp/MenuPlanner.Mcp.dll"],
      "env": {
        "Mongo__ConnectionString": "mongodb://localhost:27017",
        "Mongo__Database": "menuplanner"
      }
    }
  }
}
```

En Windows, la ruta va con dobles diagonales: `"C:\\Users\\tu-usuario\\MenuApp\\publish\\mcp\\MenuPlanner.Mcp.dll"`.

Reinicia Claude Desktop. En el chat verás las herramientas de **menu-planner**.

## 3. Pídele cosas como

- "Dame un menú semanal con 5 platillos"
- "Que incluya enchiladas suizas y no tinga"
- "Cambia el picadillo por algo con pescado"
- "Quita las hamburguesas y agrega pollo teriyaki"
- "¿Cuánto me sirvo de lomo en salsa verde?"
- "Pásame la lista de compras", y luego "Confirma el menú"
- "¿Qué platillos cocino más?" / "¿Qué debería tener siempre en la despensa?"
- "¿Qué puedo cocinar con lo que tengo?"
- "Propónme un platillo nuevo con pescado" (o usa el prompt **sugerir_platillo** desde el botón "+" del chat → menu-planner)

Puedes nombrar los platillos como los escribes en Keep ("las enchiladas", "poyo ajonjolí", "tinga"). El servidor los traduce a la receta correspondiente; si hay varias opciones, te las devuelve para que elijas.

## Herramientas

| Herramienta | Qué hace |
|---|---|
| `generate_weekly_menu` | Genera N platillos de 4 porciones (con incluir, excluir, semana y semilla) |
| `get_menu`, `list_menus` | Ver un menú (sin id: el borrador más reciente) o los recientes |
| `swap_dish`, `add_dish`, `remove_dish` | Editar el borrador (cambiar por proteína o etiqueta) |
| `confirm_menu` | Congela recetas y lista de compras y registra el historial |
| `get_shopping_list` | Lista por pasillo con cantidades de compra |
| `get_recipe` | Receta porcionada: lote, tu porción y pasos (`proteinTarget` min/mid/max) |
| `list_recipes`, `list_favorites` | Catálogo y tus platillos más frecuentes de Keep |
| `pantry_suggestions` | Lo que compras seguido según tus listas |
| `cookable_recipes` | Recetas ordenadas por lo que ya sueles tener, con lo que falta |
| `get_suggestion_context` | Contexto para inventar un platillo: plato del plan, intercambios, reglas, ingredientes disponibles (ids), gustos y formato |
| `preview_recipe` | Valida y porciona una receta propuesta (lote, tu porción, macros, lo que falta). No guarda |
| `save_recipe` | Guarda la receta aceptada (`ia-sugerida` + `revisar-nutriologa`) y opcionalmente la agrega al menú |

**Prompt** `sugerir_platillo(idea?)`: guía a Claude por el ciclo contexto → 2–3 ideas → `preview_recipe` → corregir → mostrarte → `save_recipe` solo si aceptas.

## Probar sin MongoDB

Con `"Storage__Provider": "Memory"` en `env`, el servidor arranca con el seed en memoria. Sirve para probar, pero los datos se pierden al cerrar Claude Desktop.

## Problemas comunes

- **Las herramientas no aparecen:** revisa el log en Claude Desktop (Settings → Developer → menu-planner → Open logs). Los mensajes del servidor salen por stderr.
- **"No se pudo conectar a MongoDB":** verifica que tu servidor esté corriendo y que `Mongo__ConnectionString` sea correcta.
- **`dotnet` no se encuentra:** usa la ruta completa del ejecutable (`which dotnet` en macOS/Linux, `where dotnet` en Windows) en `command`.
