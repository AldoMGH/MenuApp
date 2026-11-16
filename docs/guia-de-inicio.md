# Guía de inicio: instalar, inicializar y usar la app

Recorrido completo para dejar la app funcionando en tu computadora con tu MongoDB local, tu historial de Keep y Claude Desktop. Los pasos 1 a 9 se hacen una sola vez (unos 30–45 minutos). El paso 11 es la rutina de cada semana.

> Los comandos van primero para **macOS/Linux** y, cuando cambian, después para **Windows (PowerShell)**.

---

## 0. Requisitos

| Necesitas | Cómo comprobarlo |
|---|---|
| **Git** | `git --version` |
| **SDK de .NET 8** ([descarga](https://dotnet.microsoft.com/download/dotnet/8.0)) | `dotnet --list-sdks` muestra una línea `8.0.x` |
| **MongoDB** corriendo en tu computadora (Community Server; 6.0 o superior recomendado) | `mongosh --eval "db.runCommand({ping:1})"` responde `{ ok: 1 }` |
| **Claude Desktop** (opcional, para pedir menús por chat) | Abre la app y verifica que tengas sesión |
| Tu documento **"Menus"** de Google Docs (tus notas de Keep) | Ábrelo en Google Docs |

Si MongoDB no responde, inicia el servicio:
- **macOS (Homebrew):** `brew services start mongodb-community`
- **Linux:** `sudo systemctl start mongod`
- **Windows:** en *Servicios*, inicia **MongoDB Server**, o en PowerShell como administrador: `net start MongoDB`

---

## 1. Obtener el código

```bash
git clone https://github.com/AldoMGH/MenuApp.git
```

```bash
cd MenuApp
```

Mientras el PR [#1](https://github.com/AldoMGH/MenuApp/pull/1) no se fusione, usa la rama con todas las fases:

```bash
git checkout feat/fase-4-sugerencias
```

Cuando ya esté fusionado, quédate en `main`.

---

## 2. Compilar y correr las pruebas

```bash
dotnet build
```

```bash
dotnet test
```

Debe terminar sin fallas. Las pruebas de integración con MongoDB aparecen como *omitidas* hasta que les indiques tu servidor. Usan una base temporal `menuplanner_test_*` que se borra al terminar.

macOS/Linux:

```bash
MENUPLANNER_TEST_MONGO="mongodb://localhost:27017" dotnet test
```

Windows (PowerShell):

```powershell
$env:MENUPLANNER_TEST_MONGO = "mongodb://localhost:27017"; dotnet test
```

---

## 3. Configurar la conexión a MongoDB

Por defecto la app usa `mongodb://localhost:27017` y la base **`menuplanner`** (en `src/MenuPlanner.Api/appsettings.json`). Si tu Mongo no tiene usuario ni contraseña, **no tienes que hacer nada**.

Si tu Mongo usa usuario y contraseña, guárdalos como *user-secret*. No quedan en el repositorio:

```bash
dotnet user-secrets set "Mongo:ConnectionString" "mongodb://usuario:clave@localhost:27017" --project src/MenuPlanner.Api
```

Otra opción es la variable de entorno `Mongo__ConnectionString` (con dos guiones bajos).

---

## 4. Primer arranque

```bash
dotnet run --project src/MenuPlanner.Api
```

Se abre el navegador en **http://localhost:5033**. Swagger, la documentación de la API, está en http://localhost:5033/swagger.

En el primer arranque la app crea los índices y **carga los datos iniciales**. En la terminal verás:

```
Seed: 69 ingredientes cargados.
Seed: 1 planes cargados.
Seed: 25 recetas cargadas.
Seed: 66 platillos del diccionario de Keep cargados.
```

Para comprobarlo en Mongo:

```bash
mongosh menuplanner --eval "db.recipes.countDocuments()"
```

Debe dar `25`.

> **Los datos iniciales solo se cargan en colecciones vacías.** Reiniciar la app nunca pisa tus cambios.

**¿Solo quieres echar un vistazo sin MongoDB?** El modo memoria trae los mismos datos de ejemplo, pero se pierde todo al cerrar:

```bash
dotnet run --project src/MenuPlanner.Api -- --Storage:Provider=Memory
```

Para detener la app: `Ctrl + C` en la terminal.

---

## 5. Revisar tu plan de alimentación

Entra a **Plan de alimentación** y compáralo con el PDF de tu nutrióloga:
- **Comida, tu plato:**
  - 2 tz de verdura
  - 150–200 g de proteína magra **en cocido**
  - ½ tz de leguminosa 2–3 veces por semana
  - 1 porción de cereal
  - grasa opcional
- **1 porción de cereal =** 2 tortillas de maíz, 4 de nopal, ½ tz de arroz o pasta cocidos, ½ papa, 1 paquete de salmas…
- **Reglas:** sin fritos, capeados ni empanizados.
- **Punto de proteína:** el plan usa el **máximo** del rango (200 g), por tu entrenamiento de fuerza. Si prefieres otro: **Editar esta versión** → *Punto del rango de proteína* → `min`, `mid` o `max`.

**Cuando la nutrióloga cambie tu plan:** **Nueva versión (cita con la nutrióloga)** copia el plan actual. Editas lo que cambió, guardas y pulsas **Activar** en esa versión. Los menús ya confirmados conservan el cálculo de la versión anterior.

---

## 6. Importar tu historial de Keep

1. En Google Docs abre **"Menus"** → **Archivo → Descargar → Markdown (.md)**.
2. Guarda el archivo en la carpeta `data/` del proyecto, por ejemplo `data/Menus.md`. Esa carpeta está en `.gitignore`: **tus notas nunca se suben a GitHub**.
3. En la app ve a **Importar Keep** → **Elegir archivo…** → **Revisar sin guardar**. Deberías ver aproximadamente:
   - **71 notas** (mayo 2024 → noviembre 2026)
   - **~386 platillos**, con ~100% asignados
   - unos 10 ignorados (líneas que no son platillos, como "vinagre de arroz")
   - un aviso por las dos notas "Septiembre 3"
4. Si las fechas salen en el año equivocado, escribe el **año de la nota más reciente** y repite la revisión.
5. Pulsa **Importar**.
6. Si aparece la tabla **Revisión**, decide renglón por renglón: *Asignar a…* un platillo, *Platillo nuevo* o *No es platillo*. Luego **Aplicar revisión**. La app aprende esos alias para la próxima vez.
7. En **Mis platillos** verás tu historial: veces, última vez y meses del año. Desde ahí puedes ligar un platillo a una receta con el lápiz.

> Reimportar el mismo documento no duplica nada: solo agrega notas nuevas.

---

## 7. Despensa y básicos

En **Despensa y básicos**:
1. **Lo que siempre tienes:** agrega lo que nunca compras cada semana (aceite, sal de ajo, chía…) y pulsa **Guardar despensa**. Saldrán en "Ya tienes" en la lista de compras.
2. **Lo que compras seguido:** sale de tus listas de Keep. Con **A despensa** lo agregas en un clic.
3. **Básicos semanales:** tus desayunos y cenas habituales, como cantidad por día. Por ejemplo: huevo 2 pzas/día, 7 días; tortilla de maíz 2 pzas/día. Pulsa **Guardar básicos**. Se suman a la lista cada semana. La colación del plan (fruta, yogurt y chía) ya viene incluida.
4. **¿Qué puedo cocinar con lo que tengo?:** tus recetas ordenadas por qué tanto de sus ingredientes ya sueles tener, con lo que falta comprar.

---

## 8. (Opcional, muy recomendable) Afinar los ingredientes

Las cantidades de compra dependen del **rendimiento crudo → cocido** (por ejemplo, la pechuga rinde ×0.72). Los valores son aproximados. Para afinarlos:
1. Pesa una vez tus 3–4 proteínas principales en crudo y ya cocidas (pechuga, piernil, molida, pescado).
2. Rinde = gramos cocidos ÷ gramos crudos.
3. En **Ingredientes** → lápiz → **Rendimiento crudo → cocido**, guarda el valor.

Ahí mismo puedes corregir la **nutrición por 100 g** de productos preparados que varían por marca (pasta de mole, pipián, salmas, tostadas). Los macros son solo informativos.

---

## 9. Tu primer menú

1. **Menú de la semana** → elige cuántos platillos quieres. Cada uno rinde **4 porciones**: 5 platillos son 20 porciones para 7 comidas, y lo que sobra se congela. Pulsa **Generar menú**.
2. Ajusta:
   - **Cambiar** (por otra proteína o cualquier otro)
   - **Quitar**
   - **Agregar un platillo**
   - **Otra propuesta**
   - Antes de generar puedes marcar **Incluir sí o sí** y **No incluir**.
3. Cada tarjeta muestra **tu porción al servir** (lo que te pones en el plato), el **lote para cocinar** y los **macros aproximados**.
4. **Lista de compras:** agrupada por pasillo, con cantidades como se compran (1.15 kg, ½ kg de tortilla, 1 manojo). Palomea en el súper; las marcas se guardan en el navegador. **Copiar como texto** la deja lista para WhatsApp o Keep.
5. Cuando ya compraste o cocinaste, pulsa **Confirmar**. Se congela la lista y los platillos se suman a tu historial; la próxima vez el generador evita repetir lo de las últimas 3 semanas.

---

## 10. Claude Desktop (opcional)

1. Publica el servidor MCP:

   ```bash
   dotnet publish src/MenuPlanner.Mcp -c Release -o publish/mcp
   ```

2. Claude Desktop → **Settings → Developer → Edit Config** y agrega (con la **ruta absoluta** a tu carpeta):

   ```json
   {
     "mcpServers": {
       "menu-planner": {
         "command": "dotnet",
         "args": ["/Users/tu-usuario/MenuApp/publish/mcp/MenuPlanner.Mcp.dll"],
         "env": {
           "Mongo__ConnectionString": "mongodb://localhost:27017",
           "Mongo__Database": "menuplanner"
         }
       }
     }
   }
   ```

   En Windows la ruta va con dobles diagonales: `"C:\\Users\\tu-usuario\\MenuApp\\publish\\mcp\\MenuPlanner.Mcp.dll"`.

3. Reinicia Claude Desktop y prueba:
   - "Dame un menú semanal con 5 platillos"
   - "Cambia el picadillo por algo con pescado" · "Pásame la lista de compras" · "Confirma el menú"
   - "¿Qué puedo cocinar con lo que tengo?"
   - "Propónme un platillo nuevo con pescado", o el prompt **sugerir_platillo** desde el botón "+" del chat. Claude propone, la app valida y porciona, y **solo se guarda si dices que sí**.

El servidor MCP usa la misma base que la app web: lo que hagas en uno se ve en el otro. No hace falta que la app web esté abierta. Más detalle en [`claude-desktop.md`](claude-desktop.md).

---

## 11. Rutina semanal sugerida

1. **Sábado o domingo:** genera el menú (en la app o con Claude) y ajústalo.
2. **Lista de compras** → copia el texto o llévala en el navegador → compra.
3. **Confirma** el menú.
4. Durante la semana, consulta **tu porción** en la receta al servir.
5. Cuando quieras variar: pide un **platillo nuevo** a Claude o revisa **¿Qué puedo cocinar con lo que tengo?**.

Ya no necesitas llevar el menú en Keep: confirmar en la app registra tu historial. Si sigues anotando en Keep, puedes reimportar el documento cuando quieras; solo se agregan las notas nuevas.

---

## 12. Actualizar la app

```bash
git pull
```

Después reinicia la app (`Ctrl + C` y `dotnet run --project src/MenuPlanner.Api`). Si la actualización trae recetas, ingredientes o platillos nuevos, ve a **Plan de alimentación → Sincronizar seed**. Agrega lo nuevo sin pisar tus datos.

> Limitación conocida: la sincronización **no rellena campos nuevos en ingredientes que ya tienes** (por ejemplo, la nutrición). Edítalos en **Ingredientes**.

Si usas Claude Desktop, vuelve a publicar el MCP (paso 10.1) y reinicia Claude Desktop.

---

## 13. Respaldo y reinicio

**Respaldar** (recomendado antes de actualizar):

```bash
mongodump --db menuplanner --out respaldo
```

**Restaurar:**

```bash
mongorestore --drop --db menuplanner respaldo/menuplanner
```

**Empezar de cero.** Borra todo: plan, recetas, historial y menús. Después reinicia la app y se vuelven a cargar los datos iniciales:

```bash
mongosh menuplanner --eval "db.dropDatabase()"
```

---

## 14. Solución de problemas

| Síntoma | Qué hacer |
|---|---|
| Al arrancar: **"No se pudo conectar a MongoDB"** | Inicia el servicio de MongoDB (paso 0) o revisa la cadena de conexión (paso 3) |
| **"No hay un plan de alimentación activo"** | En **Plan**, activa una versión; si no hay ninguna, reinicia con la base vacía (paso 13) |
| El puerto **5033 está ocupado** | `dotnet run --project src/MenuPlanner.Api -- --urls http://localhost:5050` |
| La página queda **en blanco** o con estilos viejos | Detén la app, ejecuta `dotnet build`, vuelve a arrancar y recarga sin caché (`Ctrl/Cmd + Shift + R`) |
| La importación encuentra **0 notas** | Cada nota debe empezar con un título "Mes N" (p. ej. "Noviembre 1") y tener secciones como "Comidas" |
| Las fechas importadas salen en **otro año** | En Importar Keep indica el año de la nota más reciente |
| **Claude Desktop no muestra las herramientas** | Revisa la ruta absoluta del `.dll`; en Settings → Developer → menu-planner abre los logs. Si `dotnet` no se encuentra, pon su ruta completa en `command` (`which dotnet` o `where dotnet`) |
| Una receta muestra **"Sin datos nutrimentales"** | Agrega la nutrición por 100 g del ingrediente en **Ingredientes** |

¿Dudas sobre cómo funciona por dentro? Ve [`arquitectura.md`](arquitectura.md).
