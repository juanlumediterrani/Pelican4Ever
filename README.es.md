# Pelican 4Ever (`SV-RTMP`)

🌐 **Idiomas:** [English](README.md) | Español

Mod para **Stardew Valley 1.6.15** (SMAPI 4.1+) que transforma el modo multijugador cooperativo en un **mundo persistente en tiempo real (escala 1:1)** diseñado para servidores dedicados 24/7 de **hasta 16 jugadores**.

Incluye residencias privadas personalizables accesibles desde la Plaza Central y una red determinista de puentes, 8 tipos auténticos de granja con anexos temáticos en miniatura (40×32), economía comunitaria persistente, descanso asíncrono sin desmayos forzados, supervisión DevOps remota mediante API REST y panel web, y soporte nativo para agentes autónomos.

---

## 🚀 Servidor Preview y Acceso para Testers

¡El **Servidor Preview de Pelican 4Ever** ya está disponible para testers y colaboradores!

El acceso al servidor está protegido mediante autenticación criptográfica y lista blanca por emparejamiento previo (`P4E\x02`). Para participar en las pruebas:

1. **Únete al servidor de Discord:** [https://discord.gg/2x5RuvvQB](https://discord.gg/2x5RuvvQB)
2. **Envía un mensaje directo (DM) a Dextrure** para solicitar tu **código de pairing** e indicar tu IP pública inicial.
3. Tras recibir la confirmación y conectar por primera vez con el mod actualizado, tu dispositivo quedará vinculado de forma permanente para acceder desde cualquier conexión.

Para más detalles sobre el proceso de conexión y requisitos del cliente, consulta la [Guía para Testers](docs/plan-despliegue-y-guia-testers-identidad.md).

---

## 🌟 Características Principales

### 1. Reloj 1:1 y Ciclo Diario Persistente (24/7)
- **Avance en Tiempo Real:** El reloj del juego avanza al mismo ritmo que la hora real (`600.000 ms` por cada 10 minutos de juego; 1 día real = 1 día en el juego).
- **Madrugada Continua (*NightHold*):** Entre las 01:50 y las 06:00 reales, el reloj se congela en `2550` (nunca alcanza las `2600`), permitiendo jugar de madrugada sin desmayos forzados por horario. El entorno nocturno se simula de forma suave y las máquinas procesan materiales continuamente.
- **Cambio de Fecha Desacoplado:** La fecha de calendario avanza a las `02:00 AM`. Antes del cierre se guarda y valida un backup con firma SHA-256, se sincronizan los peers y se avisa a los jugadores con 10 minutos de antelación.
- **Amanecer y Horarios Naturales:** La luz del día y los horarios de los habitantes de Pelican comienzan a las `06:00 AM`; el cambio de fecha nocturno no adelanta las rutinas diurnas.
- **Calendario Perpetuo por Época:** Las estaciones duran 28 días reales (4 semanas exactas), haciendo que el Día 1 sea siempre lunes real y el Día 7 sea domingo real.

### 2. Hasta 16 Residencias Privadas y Anexos Temáticos
- **Red de Portales:** 16 residencias individuales distribuidas entre la Plaza Central (`Farm`) y una cadena determinista de puentes (`Pelican4Ever_Bridge_1..3`).
- **8 Tipos de Granja Oficiales:** Elección entre Estándar, Fluvial, Bosque, Colinas, Salvaje, Cuatro Esquinas, Playa y Pradera (Meadowlands), con forraje activo, estanques de pesca, minerales y criaturas nocturnas adaptadas a cada bioma.
- **Cueva Privada Individual:** Cada granjero elige de forma independiente si desea murciélagos de fruta o setas para su cueva personal.
- **Anexos Temáticos en Miniatura (40×32):** Expansiones de terreno adquiribles compuestas al 100% con piezas y tilesets vanilla (ADR 0007 / Épica 9.1), con variantes de entrada única, cierres perimetrales naturales de agua/acantilado y saneamiento automático de despojos al recargar la partida (MapVersion 3).
- **Políticas de Acceso y Anti-Griefing:** Seguridad configurable por residencia (`Locked`, `Friends`, `Public`) con gestión dinámica de listas de amigos y permisos de construcción para visitantes mediante el comando `/residencia`.
- **Culling Dinámico de Red:** Las residencias y anexos sin jugadores presentes no consumen ancho de banda ni saturan la red de los clientes.

### 3. Sueño Asíncrono y Balance de Energía
- **Descanso y Desconexión Individual:** Cada jugador puede acostarse en su cama y salir al menú principal o desconectarse de forma individual sin pausar el mundo ni expulsar a los demás granjeros.
- **Sistema de Saciedad y Estado «Bien Comido»:** La saciedad se vacía a lo largo de 4 horas reales. Al alcanzar 1/4 de saciedad con cualquier comida o plato cocinado, se activa el buff *Bien Comido*, permitiendo recuperar energía pasivamente al sentarse en el suelo o en sillas (`GroundSitModule`).
- **Regeneración Diaria en el Spa:** Baño relajante en el Spa del ferrocarril que otorga hasta 120 de energía y salud en 2 horas.
- **Turnos Mineros Equitativos:** Los niveles y recursos de las minas se regeneran cada 12 horas (06:00 y 18:00 reales) para repartir minerales y gemas de forma justa entre turnos.

### 4. Vida Comunitaria e Inmersión Social
- **Buzones Vecinales:** Envío y recepción de correo vecinal junto al portal de cada residencia con indicadores visuales sincronizados.
- **Chat Multicanal y de Proximidad Espacial:** Canales global (`/g`), local por proximidad con atenuación sonora y visual por distancia (`/l`), y susurros privados (`/w`).
- **Mercado Vecinal Asíncrono:** Tablón comunitario para compraventa de excedentes y cultivos entre granjeros conectados o desconectados, con depósito en garantía y confirmación de entrega segura.
- **Festivales Desacoplados:** Entrada y salida libre a festivales sin salto forzado de cámara ni reseteos de reloj; minijuegos competitivos por tandas con premios oficiales otorgados a las 23:50 y ceremonias comunitarias en vivo (Luau, Medusas Lunares).

### 5. Panel DevOps, API de Anfitrión v1 y Modo Headless
- **Dashboard Web Integrado (SPA):** Interfaz ligera servida en el puerto `8085` para monitorizar el estado del servidor, mapa del mundo interactivo, jugadores conectados y residencias.
- **API REST Versionada (`/api/v1/host/`):** Contrato HTTP local seguro para controlar el reloj, gestionar anuncios, expulsar infractores, organizar tandas festivas y consumir el flujo incremental de eventos (`events?since=N`).
- **Comandos de Consola Remotos:** Ejecución remota y segura de comandos administrativos whitelisted a través de la API REST.
- **Reinicio Seguro Programado (`p4e_safe_restart`):** Calcula el progreso en riesgo de los jugadores antes de programar reinicios limpios con persistencia garantizada.
- **Modo Servidor Headless:** Permite al servidor dedicado ejecutarse sin dibujado gráfico en Linux, minimizando el consumo de CPU y RAM.

### 6. Integración con Agentes Autónomos y Cosméticos
- **Modo `AgentHost`:** Diseñado para anfitriones autónomos (como Stardew Voyager o Putre), permitiendo registrar descansos diarios sin cerrar la sesión de red.
- **Pack Cosmético Dr4co (Fashion Sense 7.5.0+):** Cuerpo personalizado con ciclo de movimiento en 4 direcciones, alineación 16×32 con sombra vanilla y desplazamiento de herramientas en garras (`AgentCosmeticsModule` / `AgentToolOffset`).
- **Comando `p4e_dr4co_sprite`:** Permite alternar la apariencia del dragón desde consola o chat sin necesidad de utilizar el espejo cosmético en el juego.

### 7. Verificación de Integridad del Modpack
- **Control Estricto de Clientes (`RequiredClientMods`):** El servidor valida durante el handshake inicial de SMAPI que los clientes conectados tengan instalada la versión exacta del modpack para prevenir desincronizaciones de ítems y desajustes visuales.

---

## 📚 Documentación Técnica

* [Guía para Testers e Identidad de Dispositivos (`docs/plan-despliegue-y-guia-testers-identidad.md`)](docs/plan-despliegue-y-guia-testers-identidad.md)
* [Protocolo de Identidad y Modelo de Amenazas (`docs/protocolo-identidad-y-modelo-amenazas.md`)](docs/protocolo-identidad-y-modelo-amenazas.md)
* [Diseño del Juego (`design.md`)](design.md)
* [Arquitectura Técnica (`architecture.md`)](architecture.md)
* [Backlog de Iteraciones y Épicas (`backlog.md`)](backlog.md)
* [Especificación de la API de Anfitrión (`docs/host-api.md`)](docs/host-api.md)
* [Guía del Modpack persistente (`docs/modpack.md`)](docs/modpack.md)
* [Servidor persistente desatendido (`docs/servidor-persistente.md`)](docs/servidor-persistente.md)
* [Arquitectura de Producción con Hermes (`docs/arquitectura-produccion-hermes.md`)](docs/arquitectura-produccion-hermes.md)
* [Decisiones de Arquitectura (`docs/adr/`)](docs/adr/)
* [Guía de Sprites y Cosméticos para Agentes (`agents/README.md`)](agents/README.md)
* [Pipeline de Generación Pixel Art (`docs/pixel_art_pipeline.md`)](docs/pixel_art_pipeline.md)

---

## 🏗️ Estructura de la Solución

```text
Pelican4Ever.sln
├── Pelican4Ever.Core/      # Lógica de dominio pura (.NET 6, cero dependencias de SMAPI ni MonoGame)
│   ├── Admin/              # API de anfitrión, ejecución en hilo principal, eventos y reinicio seguro
│   ├── Chat/               # Enrutamiento de canales, parsing de comandos y proximidad espacial
│   ├── Config/             # ServerConfig con validación determinista estricta
│   ├── Economy/            # Saciedad, descanso, spa, multiplicadores y mercado vecinal
│   ├── Festivals/          # Tandas de inscripción, ceremonias, puntuaciones y premios
│   ├── Networking/         # Requisitos del modpack de clientes y culling de red
│   ├── Players/            # PlayerRecord, derivación de SleepState, SleepPolicy y offsets de agentes
│   ├── Server/             # ServerWorldState y ciclo de vida del servidor
│   ├── Time/               # GameClock, ServerCalendar, ServerTime y CatchUpPlan
│   └── World/              # Residencias, portales, anexos temáticos, tipos de granja y cuevas
├── Pelican4Ever/           # Mod SMAPI (.NET 6, ensamblado con parches Harmony)
│   ├── Framework/          # ModContext, IModule, routing espacial y clonación de mapas
│   └── Modules/            # 31 módulos SMAPI desacoplados con registro defensivo
└── Pelican4Ever.Tests/     # Suite de 1.313 pruebas unitarias y de integración en xUnit (.NET 8)
```

---

## ⚙️ Compilación y Pruebas

### Requisitos
* **.NET SDK 8.0**
* **Stardew Valley 1.6.15** y **SMAPI 4.1+**

### Pasos
1. Si tu instalación de Steam o Stardew Valley está en una ruta personalizada, copia `local.props.example` como `local.props` y ajusta `<GamePath>`:
   ```bash
   cp local.props.example local.props
   ```
2. Compila la solución completa. El paquete se compilará con `TreatWarningsAsErrors = true`, desplegando el mod directamente en tu directorio `Mods/Pelican4Ever` y generando el archivo ZIP de release:
   ```bash
   dotnet build Pelican4Ever.sln
   ```
3. Ejecuta la suite completa de pruebas unitarias y de integración:
   ```bash
   dotnet test Pelican4Ever.Tests
   ```

---

## 🐳 Despliegue con Docker Compose (Servidor Dedicado 24/7)

El proyecto incluye soporte para ejecución desatendida mediante [docker-compose.yml](docker-compose.yml) apoyado sobre la imagen de `JunimoServer`:

1. Crea tu archivo de variables de entorno a partir de la plantilla:
   ```bash
   cp .env.example .env
   # Edita .env con tus credenciales de Steam para el Host dedicado
   ```
2. Configura tu token de administración seguro en `config.json` a partir de [config.example.json](config.example.json):
   ```bash
   cp config.example.json config.json
   # Asegúrate de cambiar AdminApiToken por una clave secreta propia
   ```
3. Levanta el contenedor en segundo plano:
   ```bash
   docker compose up -d
   ```
4. El servidor expondrá los puertos configurados:
   - `24642/udp`: Puerto multijugador nativo (Lidgren/Galaxy).
   - `8085`: API REST y Dashboard web de administración (`http://localhost:8085/`).
   - `5900`: VNC opcional para inspección visual remota.
