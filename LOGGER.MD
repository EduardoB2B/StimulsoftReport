# Guía de Uso para Logging en StimulsoftReport

Este documento resume los comandos y endpoints disponibles para gestionar el logging en la aplicación, incluyendo consulta de archivos de log, visualización y control dinámico del nivel de logging.

---

## 1. Consultar lista de archivos de log existentes

**Endpoint:**  
`GET /api/logs/`

**Descripción:**  
Devuelve la lista de archivos de log disponibles para consulta o descarga.

**Cómo usar:**  
Haz una petición GET a `/api/logs/` para obtener la lista de archivos.

---

## 2. Descargar o ver contenido de un archivo de log

**Endpoint:**  
`GET /api/logs/{nombreArchivo}`

**Parámetros:**  
- `nombreArchivo`: Nombre del archivo de log a consultar o descargar (ejemplo: `log-2025-10-21.txt`)

**Descripción:**  
Devuelve el contenido del archivo de log especificado.

**Cómo usar:**  
Haz una petición GET a `/api/logs/{nombreArchivo}` reemplazando `{nombreArchivo}` por el nombre del archivo que quieres ver o descargar.

---

## 3. Consultar nivel actual de logging

**Endpoint:**  
`GET /api/logging/level`

**Descripción:**  
Devuelve el nivel de logging actual configurado en la aplicación.

**Cómo usar:**  
Haz una petición GET a `/api/logging/level`.

**Respuesta ejemplo:**  
```json
{
  "CurrentLevel": "Information"
}

Endpoint:
POST /api/logging/level?level={nivel}

Parámetros:

level: Nivel de logging deseado. Valores válidos:
Verbose
Debug
Information
Warning
Error
Fatal
Off (apaga el logging, internamente se usa Fatal)


**Descripción:**  
Este endpoint permite controlar dinámicamente el nivel de logging sin reiniciar la aplicación.

Para prender el logging, envía un nivel distinto de Off, por ejemplo Information o Debug.
Para apagar el logging, envía el nivel Off, que internamente configura el nivel a Fatal para minimizar los logs.
Cómo actualizar el nivel de logging:

Para prender el logging con un nivel específico, realiza una petición POST con el nivel deseado.
Ejemplo para prender con nivel Warning:
POST /api/logging/level?level=Warning
Para apagar el logging, realiza una petición POST con el nivel Off.
Ejemplo para apagar:
POST /api/logging/level?level=Off