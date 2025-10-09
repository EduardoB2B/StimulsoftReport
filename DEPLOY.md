# 📦 DEPLOY.md – Procedimiento de Despliegue Manual

Este documento describe el proceso para generar, versionar y desplegar la aplicación de **Reportes con Stimulsoft** en entornos manuales.

---

## ✅ Checklist de despliegue

### 1. Verificar el commit actual en Git
Antes de compilar, identifica el hash del commit que quedará en producción:

```bash
git log -1 --oneline
# Ejemplo: 1a9c904 Commit de corrección CFDI
```

Para obtener el hash completo:

```bash
git rev-parse HEAD
# Ejemplo: 1a9c90430fe5242dbb7a8ad85e24da30ced9be51
```

📌 Este hash aparecerá también en el endpoint `/health/detailed` dentro del campo `"commit"`.  
Así puedes validar que el servidor está ejecutando exactamente el código de este commit.

---

### 2. Actualizar la versión en el `.csproj`

Edita el archivo **`StimulsoftReport.csproj`** y actualiza las propiedades:

```xml
<PropertyGroup>
  <Version>1.3.1</Version>
  <AssemblyInformationalVersion>1.3.1-Release</AssemblyInformationalVersion>
</PropertyGroup>
```

📌 Convención para versionado (SemVer):
- **Major (1.x.x)** → cambios incompatibles.  
- **Minor (x.1.x)** → nuevas funcionalidades compatibles.  
- **Patch (x.x.1)** → corrección de bugs.  

---

### 3. Crear paquete de despliegue

Ejecuta el comando de **publish**:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o C:\ReportePDFDev\
```

Explicación de parámetros:
- `-c Release` → compila en configuración Release.  
- `-r win-x64` → genera para Windows x64.  
- `--self-contained true` → incluye runtime de .NET (no depende de instalación en servidor).  
- `-o C:\ReportePDFDev\` → carpeta donde se genera el paquete.  

---

### 4. Copiar archivos al servidor

1. Detén la aplicación/servicio en el servidor (si corresponde).  
2. Copia el contenido de `C:\ReportePDFDev\` al directorio de la aplicación en el servidor.  
3. Reinicia la aplicación/servicio para usar la nueva versión.

---

### 5. Validar en `/health/detailed`

Accede al endpoint:

```
http://<tu-servidor>:5000/health/detailed
```

Verifica:
- `"version"` → corresponde al valor actualizado en `.csproj`.  
- `"commit"` → coincide con el hash de `git rev-parse HEAD`.  
- `"reportes"` → muestra las plantillas `.mrt` con su última fecha de modificación.  
- `"marca_tiempo"` → corresponde a la hora local del servidor.  

Ejemplo esperado:

```json
{
  "estado": "Healthy",
  "marca_tiempo": "04-10-2025 10:22:15",
  "version": "1.3.1-Release",
  "commit": "1a9c90430fe5242dbb7a8ad85e24da30ced9be51",
  "reportes": [
    { "reporte": "ReporteCfdi.mrt", "ultima_modificacion": "01-10-2025 11:06" }
  ]
}
```

---

## 📌 Notas finales

- Si el commit en `/health/detailed` no coincide con el de tu repo → probablemente copiaste una build distinta al servidor.  
- Si la versión no cambió → asegúrate de actualizar `<Version>` y `<AssemblyInformationalVersion>` en el `.csproj` antes de publicar.  
- Este procedimiento aplica a **despliegues manuales**. Para despliegues automáticos (CI/CD), se usarían pipelines que inyectan versión y commit automáticamente.

---

✅ Con este documento, cualquier miembro del equipo puede desplegar de forma consistente y comprobar fácilmente qué versión/commit está en producción.
