# App de Reportes con Stimulsoft

Bienvenido/a al repositorio de la **App de Reportes con Stimulsoft**.  
Este proyecto es una aplicación para la generación y visualización de reportes usando Stimulsoft Reports.

## 🚀 Empezando

Sigue estos pasos para tener el proyecto funcionando en tu máquina local.

### 📋 Prerrequisitos

- **.NET 6 SDK** o superior  
- **SQL Server** (puedes usar una instancia local o en la nube)  
- **Stimulsoft Designer** (opcional, para editar los reportes `.mrt`)

### ⚙️ Instalación

1. **Clona el repositorio:**
    ```bash
    git clone https://github.com/EduardoB2B/StimulsoftReport.git
    cd StimulsoftReport
    ```

2. **Restaura las dependencias:**
    ```bash
    dotnet restore
    ```

3. **Configura la base de datos:**
    - Asegúrate de tener SQL Server corriendo.
    - Crea una base de datos llamada `ReportesDB` (o la que uses).
    - Actualiza la cadena de conexión en `appsettings.json`:
      ```json
      "ConnectionStrings": {
        "DefaultConnection": "Server=localhost;Database=ReportesDB;User Id=usuario;Password=tu_password;"
      }
      ```

### ▶️ Ejecutar la Aplicación

Para iniciar la app en modo desarrollo:

```bash
dotnet run