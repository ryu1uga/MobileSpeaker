# MobileSpeaker

Usa un celular (iPhone o Android) como parlante de la PC por Wi-Fi. Captura lo que suena en Windows (YouTube, juegos, lo que sea) y lo envía al navegador del celular con una latencia de entre 0.1 y 0.3 s aproximadamente.

- No necesita Bluetooth, Stereo Mix ni drivers como VB-CABLE: usa WASAPI loopback.
- En el celular no se instala nada: solo se abre una URL en el navegador y se toca **Reproducir**.
- Pensado para funcionar incluso en equipos antiguos (por ejemplo, un iPhone 7 con iOS 15 o un Android de hace varios años). Ver [Dispositivos compatibles](#dispositivos-compatibles).

## Cómo funciona

1. **Captura**: WASAPI loopback del dispositivo de salida por defecto (NAudio), con un buffer de 20 ms.
2. **Conversión**: del mix format de Windows (normalmente float32, a veces Extensible y con más de 2 canales) a PCM 16 bits estéreo intercalado little-endian, con el mismo sample rate. Si hay más de 2 canales se hace un downmix a estéreo (el LFE se descarta).
3. **Servidor**: Kestrel escuchando en `0.0.0.0:8765`.
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

- Windows 10 u 11 (x64).
- Para compilar: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- PC y celular en la misma red Wi-Fi.

## Compilación

```powershell
dotnet publish -c Release
```

El ejecutable queda en:

```
bin\Release\net8.0\win-x64\publish\MobileSpeaker.exe
```

Es un solo `.exe` autocontenido: no hace falta instalar .NET en la PC donde se ejecute.

Para probar sin publicar:

```powershell
dotnet run
```

## Uso

1. Ejecuta `MobileSpeaker.exe` (o `MobileSpeaker.exe 9000` para usar otro puerto).
2. La primera vez Windows muestra un aviso del firewall: marca **Redes privadas** y acepta.
3. La consola muestra el dispositivo capturado, el formato y las direcciones disponibles, por ejemplo:
   ```
   http://192.168.1.34:8765
   ```
4. En el celular abre esa dirección en el navegador (Safari, Chrome, etc.) y toca **Reproducir**.
5. Reproduce algo en la PC.

En la página puedes ajustar:

- **Retardo objetivo**: menos retardo = menos latencia, pero más riesgo de cortes. Empieza con 80 ms; si escuchas chasquidos o cortes, súbelo a 120-150 ms. Si tu Wi-Fi es muy estable puedes bajarlo.
- **Volumen**: de 0 a 150 %.

El indicador de estado muestra si está conectado, reproduciendo, si no hay sonido en la PC o si está reconectando. Si la conexión se cae, la página reintenta cada segundo.

Se pueden conectar varios celulares (u otros navegadores) al mismo tiempo.

## Solución de problemas

- **El celular no carga la página**: revisa que la red Wi-Fi de la PC esté configurada como **privada** y que MobileSpeaker esté permitido en el Firewall de Windows para redes privadas (Panel de control > Firewall de Windows Defender > Permitir una aplicación). Algunas redes (universidades, hoteles) aíslan a los clientes entre sí; en ese caso no funcionará.
- **iPhone: no suena con el interruptor de silencio activado**: toca **Detener** y luego **Reproducir** otra vez.
- **Dice "Sin sonido en la PC"**: Windows no envía datos al loopback cuando nada está sonando. Reproduce algo.
- **Cortes frecuentes**: sube el retardo objetivo, acércate al router o usa la banda de 5 GHz.

## Limitaciones

- **El audio también sale por la PC**: la captura loopback copia lo que suena en el dispositivo de salida, no lo redirige. Si no quieres escucharlo en la PC, baja el volumen del parlante o monitor físico, o desconéctalo. Según la tarjeta de sonido, el volumen maestro de Windows puede o no afectar a lo que se captura, así que lo más seguro es dejarlo alto y bajar el del parlante.
- **Se corta si se bloquea la pantalla del celular**: en iPhone, Safari suspende el audio de la página; en Android depende del fabricante y del ahorro de batería. Mantén la pantalla encendida (en iPhone: Ajustes > Pantalla y brillo > Bloqueo automático; en Android: Ajustes > Pantalla > Tiempo de espera de la pantalla).
- **Si cambia el dispositivo de salida** (por ejemplo, conectas audífonos o cambias de parlante en Windows), hay que cerrar y volver a abrir el exe, porque la captura queda ligada al dispositivo que estaba por defecto al iniciar.
- La latencia real depende del Wi-Fi; en redes congestionadas puede superar los 0.3 s.
- Solo HTTP en la red local, sin cifrado ni autenticación: no lo expongas a internet.
