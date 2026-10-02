# MobileSpeaker

Usa un celular (iPhone o Android) como parlante de la PC por Wi-Fi. Captura lo que suena en Windows (YouTube, juegos, lo que sea) y lo envía al navegador del celular con una latencia de entre 0.1 y 0.3 s aproximadamente.

- No necesita Bluetooth, Stereo Mix ni drivers como VB-CABLE: usa WASAPI loopback.
- En el celular no se instala nada: solo se abre una URL en el navegador y se toca **Reproducir**.
- En la PC es una aplicación con ventana: un botón activa o desactiva el puente, y puede quedarse en la bandeja del sistema.
- Pensado para funcionar incluso en equipos antiguos (por ejemplo, un iPhone 7 con iOS 15 o un Android de hace varios años). Ver [Dispositivos compatibles](#dispositivos-compatibles).

## Cómo funciona

1. **Captura**: WASAPI loopback del dispositivo de salida por defecto (NAudio), con un buffer de 20 ms. Si cambias de parlante o audífonos en Windows, la captura se reinicia sola con el nuevo dispositivo.
2. **Conversión**: del mix format de Windows (normalmente float32, a veces Extensible y con más de 2 canales) a PCM 16 bits estéreo intercalado little-endian, con el mismo sample rate. Si hay más de 2 canales se hace un downmix a estéreo (el LFE se descarta).
3. **Servidor**: Kestrel escuchando en `0.0.0.0:8765` (el puerto se cambia en la ventana).
   - `GET /` devuelve la página del celular (embebida en el exe).
   - `/ws` es un WebSocket: envía primero `{"sampleRate":N,"channels":2}` y después bloques binarios de PCM.
   - Cada cliente tiene una cola de 8 bloques que descarta los más antiguos, así un cliente lento no acumula retraso.
4. **Celular**: la página usa la Web Audio API para programar cada bloque con un pequeño colchón (retardo objetivo configurable entre 30 y 300 ms).

## Dispositivos compatibles

La página solo necesita un navegador con Web Audio API y WebSocket, así que funciona en casi cualquier celular, tablet o computadora de los últimos años:

| Dispositivo | Navegador | Notas |
| --- | --- | --- |
| iPhone / iPad con iOS 15 o superior | Safari (y Chrome o Firefox, que en iOS usan el mismo motor) | Suena aunque el interruptor de silencio esté activado. Versiones anteriores de iOS probablemente funcionen, pero no están probadas. |
| Android 5 o superior | Chrome, Firefox, Samsung Internet, Edge | La latencia puede ser algo mayor según el modelo (en gama baja se suman entre 50 y 150 ms); si hay cortes, sube el retardo objetivo. |
| Otra PC, Mac o tablet | Cualquier navegador moderno | Útil para probar o para usar otra computadora como parlante. |

No funciona en navegadores muy antiguos sin Web Audio API (por ejemplo, Internet Explorer o Android 4 con el navegador de stock).

## Requisitos

- Windows 10 (versión 1607 o posterior) u 11, de 64 bits.
- PC y celular en la misma red Wi-Fi.
- Para compilar: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Para generar el instalador: además, [Inno Setup 6.3 o superior](https://jrsoftware.org/isdl.php) (`winget install JRSoftware.InnoSetup`).

## Compilación

### Instalador (recomendado)

```powershell
.\build-installer.ps1
```

Genera `publish\MobileSpeaker-Setup-1.0.0.exe`. Para otra versión: `.\build-installer.ps1 -Version 1.1.0`.

Si PowerShell no deja ejecutar el script, usa: `powershell -ExecutionPolicy Bypass -File .\build-installer.ps1`.

El instalador:

- Instala el programa en `C:\Program Files\MobileSpeaker` y crea el acceso en el menú Inicio (y en el escritorio, si lo eliges).
- Crea una regla en el Firewall de Windows para que el celular pueda conectarse sin avisos. Por defecto solo para **redes privadas**; hay una casilla para permitir también **redes públicas** (útil si tu Wi-Fi está marcada como pública).
- Pide permisos de administrador (por Program Files y el firewall).
- Al desinstalar quita el programa, la regla del firewall, el inicio con Windows y las preferencias.

### Exe portable (sin instalar)

```powershell
dotnet publish -c Release
```

Genera un solo `.exe` autocontenido en `bin\Release\net8.0-windows\win-x64\publish\MobileSpeaker.exe`, que se puede copiar a otra PC y ejecutar directamente. En ese caso Windows muestra el aviso del firewall la primera vez: marca **Redes privadas**.

Para probar durante el desarrollo: `dotnet run`.

## Uso

1. Abre **MobileSpeaker** desde el menú Inicio.
2. Pulsa **Activar puente**.
3. En el celular abre la dirección que muestra la ventana (por ejemplo `http://192.168.1.34:8765`) y toca **Reproducir**. El botón **Copiar** la copia al portapapeles.
4. Reproduce algo en la PC.

Para dejar de transmitir, pulsa **Desactivar puente**.

### La ventana

- **Estado**: desactivado, activo esperando celulares, o cuántos celulares hay conectados.
- **Dirección**: la recomendada aparece grande (la de la red Wi-Fi o cable con salida a internet). Debajo están las demás; las de adaptadores virtuales (Hyper-V, WSL, VirtualBox, VPN) normalmente no sirven.
- **Dispositivo**: qué salida de audio de Windows se está capturando y en qué formato.
- **Opciones**:
  - **Puerto** (8765 por defecto; se cambia con el puente desactivado).
  - **Activar el puente al abrir el programa**.
  - **Iniciar con Windows**: arranca minimizado en la bandeja del sistema.
- **Registro**: conexiones, desconexiones, cambios de dispositivo y errores.

Al **minimizar**, el programa sigue funcionando en la bandeja del sistema (junto al reloj). Desde su icono puedes activar o desactivar el puente, copiar la dirección o salir. Al **cerrar** la ventana, el puente se desactiva y el programa se cierra.

### En el celular

- **Retardo objetivo**: menos retardo = menos latencia, pero más riesgo de cortes. Empieza con 80 ms; si escuchas chasquidos o cortes, súbelo a 120-150 ms. Si tu Wi-Fi es muy estable puedes bajarlo.
- **Volumen**: de 0 a 150 %.

El indicador muestra si está conectado, reproduciendo, si no hay sonido en la PC o si está reconectando. Si la conexión se cae, la página reintenta cada segundo. Se pueden conectar varios celulares al mismo tiempo.

## Solución de problemas

- **El celular dice "timed out" o no carga la página**:
  1. En la PC abre `http://localhost:8765`. Si no carga, el puente no está activo.
  2. Usa la dirección **recomendada**, no la de un adaptador virtual.
  3. Revisa que el celular esté en la misma red Wi-Fi (no en datos móviles ni en una red de invitados).
  4. Si tu red Wi-Fi está marcada como **pública**, reinstala marcando la casilla de redes públicas, o cámbiala a privada en Configuración > Red e Internet > Wi-Fi.
  5. Algunas redes (universidades, oficinas, hoteles) aíslan a los dispositivos entre sí; ahí no funcionará. Para comprobarlo, conecta la PC al hotspot del celular y prueba de nuevo.
- **iPhone: no suena con el interruptor de silencio activado**: toca **Detener** y luego **Reproducir** otra vez.
- **Dice "Sin sonido en la PC"**: Windows no envía datos al loopback cuando nada está sonando. Reproduce algo.
- **Cortes frecuentes**: sube el retardo objetivo, acércate al router o usa la banda de 5 GHz.
- **"No se pudo abrir el puerto"**: otro programa usa ese puerto. Cambia el puerto en la ventana y vuelve a activar el puente.

## Limitaciones

- **El audio también sale por la PC**: la captura loopback copia lo que suena en el dispositivo de salida, no lo redirige. Si no quieres escucharlo en la PC, baja el volumen del parlante o monitor físico, o desconéctalo. Según la tarjeta de sonido, el volumen maestro de Windows puede o no afectar a lo que se captura, así que lo más seguro es dejarlo alto y bajar el del parlante.
- **Se corta si se bloquea la pantalla del celular**: en iPhone, Safari suspende el audio de la página; en Android depende del fabricante y del ahorro de batería. Mantén la pantalla encendida (en iPhone: Ajustes > Pantalla y brillo > Bloqueo automático; en Android: Ajustes > Pantalla > Tiempo de espera de la pantalla).
- **Al cambiar de dispositivo de salida** en Windows, los celulares se desconectan un instante y se reconectan solos.
- La latencia real depende del Wi-Fi; en redes congestionadas puede superar los 0.3 s.
- Solo HTTP en la red local, sin cifrado ni autenticación: no lo expongas a internet.
- El instalador y el exe no están firmados, así que Windows SmartScreen puede mostrar un aviso ("Más información" > "Ejecutar de todas formas").
