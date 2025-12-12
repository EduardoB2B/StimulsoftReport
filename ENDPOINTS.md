# Documentación de Endpoints API

Este documento contiene la lista de endpoints disponibles en la API, con su ruta, método HTTP y descripción.

---

## 1. Cambiar nivel de logging dinámicamente

- **Ruta:** `/api/logging/level/{level}`
- **Método:** `POST`
- **Descripción:** Permite cambiar el nivel de logging en tiempo real.  
  Niveles posibles: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal`, `Off`.

---

## 2. Consultar logs

- **Ruta:** `/api/logging/logs` (ruta aproximada, puede variar según implementación)
- **Método:** `GET`
- **Descripción:** Obtiene los registros de logs generados por la aplicación.

---

## 3. Estado de salud detallado

- **Ruta:** `/health/detailed`
- **Método:** `GET`
- **Descripción:** Devuelve el estado de salud del servicio, indicando si está `Healthy` o con problemas.

---

## 4. Información del entorno / API instalada

- **Ruta:** `/api/env-info`
- **Método:** `GET`
- **Descripción:** Proporciona información general sobre el entorno, versión y configuración del API instalada.

---