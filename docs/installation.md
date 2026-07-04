# Instalación de JellyNotify

## Requisitos previos

- Jellyfin 10.11.x o superior
- Acceso de administrador al servidor Jellyfin

---

## Opción A — Desde el repositorio de plugins (recomendado)

Esta es la forma más sencilla. El plugin se instala directamente desde la interfaz de Jellyfin.

### 1. Añadir el repositorio JellyNotify

1. Abre Jellyfin en tu navegador
2. Ve a **Panel de administración** → **Plugins** → **Repositorios**
3. Haz clic en el botón **＋** (Añadir repositorio)
4. Introduce la URL del repositorio:

   ```
   https://raw.githubusercontent.com/Rovaal-code/JellyNotify/main/repository/manifest.json
   ```

5. Haz clic en **Guardar**

### 2. Instalar el plugin

1. Ve a **Panel de administración** → **Plugins** → **Catálogo**
2. Busca **JellyNotify** en la lista
3. Haz clic en **Instalar**
4. **Reinicia Jellyfin** cuando se te indique

### 3. Configurar el plugin

1. Ve a **Panel de administración** → **Plugins** → **JellyNotify**
2. Configura las integraciones:
   - **Overseerr/Jellyseerr**: URL y API key
   - **Sonarr**: URL y API key (puedes añadir múltiples instancias)
   - **Radarr**: URL y API key (puedes añadir múltiples instancias)
   - **Discord**: URL del webhook (opcional)
   - **Telegram**: Token del bot (opcional)
3. Haz clic en **Guardar configuración**

---

## Opción B — Instalación manual (sin repositorio)

Si prefieres instalar manualmente sin añadir el repositorio:

### 1. Descargar el ZIP

Descarga `jellynotify_1.0.3.0.zip` desde:

```
https://github.com/Rovaal-code/JellyNotify/releases/tag/v1.0.3
```

### 2. Copiar al directorio de plugins

**Linux (systemd):**
```bash
sudo mkdir -p /var/lib/jellyfin/plugins/JellyNotify_1.0.3.0
sudo unzip jellynotify_1.0.3.0.zip -d /var/lib/jellyfin/plugins/JellyNotify_1.0.3.0/
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/JellyNotify_1.0.3.0/
```

**Docker:**
```bash
# Asumiendo que tienes mapeado /config → /path/to/jellyfin/config
mkdir -p /path/to/jellyfin/config/plugins/JellyNotify_1.0.3.0
unzip jellynotify_1.0.3.0.zip -d /path/to/jellyfin/config/plugins/JellyNotify_1.0.3.0/
```

**Windows:**
```powershell
Expand-Archive jellynotify_1.0.3.0.zip `
  -DestinationPath "$env:APPDATA\Jellyfin\Server\plugins\JellyNotify_1.0.3.0"
```

### 3. Reiniciar Jellyfin

```bash
sudo systemctl restart jellyfin
```

### 4. Verificar la instalación

1. Ve a **Panel de administración** → **Plugins**
2. Comprueba que **JellyNotify** aparece en la lista con estado **Activo**
3. Abre cualquier página normal de Jellyfin Web (la biblioteca, inicio, etc.) — debe aparecer la campana de JellyNotify en la cabecera, no solo en la página de configuración
4. Abre la consola del navegador (F12) y comprueba que aparece una única línea `[JellyNotify] loaded`
5. Ve a **Panel de administración** → **Plugins** → **JellyNotify** → pestaña **Diagnostics** y comprueba que "Web injection" indica **Active**

---

## Verificar integridad del ZIP

```bash
md5sum jellynotify_1.0.3.0.zip
# Debe coincidir con el checksum publicado en repository/manifest.json para la versión 1.0.3.0
```

---

## Solución de problemas

### El plugin no aparece en el catálogo
- Verifica que añadiste el URL del repositorio correctamente (debe terminar en `manifest.json`)
- Espera unos segundos y recarga la página

### El plugin aparece pero no se instala
- Comprueba que tienes Jellyfin 10.11.x o superior
- Revisa los logs de Jellyfin en `/var/log/jellyfin/`

### Las notificaciones no llegan
- Verifica que el servidor Jellyfin tiene acceso de red a Overseerr/Sonarr/Radarr
- Comprueba la configuración en **Panel de administración** → **Plugins** → **JellyNotify**
- Usa el botón **"Probar conexión"** en la página de configuración
- Usa el botón **"Enviar notificación de prueba"** para comprobar los canales
