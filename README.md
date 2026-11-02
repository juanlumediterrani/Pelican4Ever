# Pelican 4Ever (`SV-RTMP`)

Mod para **Stardew Valley 1.6.15** (SMAPI 4.1+) que transforma el modo multijugador en un **mundo persistente en tiempo real (1:1)** diseñado para servidores dedicados 24/7 de **hasta 16 jugadores**, con residencias privadas enlazadas a puertas de Pelican Town, anexos temáticos ampliables, economía comunitaria y supervisión DevOps remota.

---

## 🌟 Características Principales

1. **Reloj 1:1 y Ciclo Diario Persistente (24/7):**
   - El reloj del juego avanza al mismo ritmo que la hora real (`600.000 ms` por cada 10 minutos de juego).
   - **Madrugada continua (*NightHold*):** Entre las 01:50 y las 06:00 reales, el reloj se congela en `2550`, permitiendo jugar de madrugada sin desmayos forzados por horario.
   - **Fecha y ciclo desacoplados:** La fecha de calendario cambia a las `02:00 AM`; antes del cierre se guarda y verifica un backup SHA-256, se solicitan cierres a los peers y solo entonces se inicia el nuevo día nativo. Aviso a 10 minutos.
   - **Amanecer y horarios:** El reloj, la luz y los horarios de Pelican siguen comenzando a las `06:00 AM`; el cambio de fecha no los adelanta.

2. **Hasta 16 Residencias Privadas y Anexos Temáticos:**
   - **Catálogo de Puertas:** 16 residencias individuales mapeadas en fachadas urbanas y rurales de Pelican Town, con políticas de acceso configurables (`Locked`, `Friends`, `Public`) y protección anti-griefing.
   - **Variedad de Granjas:** Elección de 7 tipos de granja (Estándar, Fluvial, Bosque, Colinas, Salvaje, Cuatro Esquinas y Playa) con forraje, estanques, minerales y criaturas activas por residencia.
   - **Anexos Adquiribles (40×32):** Minimapas temáticos ampliables acoplados a las salidas perimetrales libres, adquiribles con oro desde un diálogo de compra con soporte de carteras separadas.
   - **Culling Dinámico de Red:** Las residencias y anexos vacíos no consumen ancho de banda ni saturan la red de los clientes.

3. **Sueño Asíncrono y Balance de Energía:**
   - **Descansar y Desconectar:** Cada jugador puede acostarse en su cama y salir al menú principal de forma individual sin pausar ni desconectar a los demás.
   - **Presupuesto de Saciedad:** Tope diario de recuperación instantánea por comida, bufo de *Bien Comido* para platos elaborados y descanso termal limitado en el Spa.
   - **Minas Persistentes:** Turnos mineros regenerados cada 12 horas (06:00 y 18:00) para repartir recursos equitativamente.

4. **Inmersión y Vida Comunitaria:**
   - **Buzones Urbanos:** Envío y recepción de cartas vecinales desde las fachadas de cada residencia.
   - **Chat Multicanal y de Proximidad:** Canales global (`/g`), local con atenuación por distancia espacial (`/l`) y susurros privados (`/w`).
   - **Mercado Vecinal Asíncrono:** Tablón comunitario para compraventa de objetos y excedentes entre granjeros conectados o desconectados.
   - **Festivales Desacoplados:** Entrada y salida libre sin salto de cámara ni reseteos de hora; minijuegos competitivos por tandas con premios oficiales a las 23:50 y ceremonias comunitarias en vivo (Luau y Medusas Lunares).

5. **Panel DevOps y API de Anfitrión v1:**
   - **Dashboard Web Integrado:** SPA ligera en puerto `8085` para monitorizar estado, jugadores conectados y residencias.
   - **API REST Versionada (`/api/v1/host/`):** Contrato HTTP local seguro para control del reloj, jugadores, anuncios, tandas festivas y registro de sucesos incremental (`events?since=N`).
   - **Modo `AgentHost` y Reinicio Seguro:** Permite al anfitrión (humano o agente autónomo como Stardew Voyager) registrar descansos diarios sin desconectarse, y programar reinicios limpios (`p4e_safe_restart`) con cálculo de progreso no guardado en riesgo.

### Apariencia opcional para agentes

El pack cosmético de Dr4co está en [`agents/Dr4co.FashionSense`](agents/Dr4co.FashionSense/). Requiere **Fashion Sense 7.5.0+** (`PeacefulEnd.FashionSense`) para mostrar el cuerpo del jugador; es una dependencia opcional de ese recurso visual, no de Pelican 4Ever. El servidor y el mod funcionan sin Fashion Sense ni el pack. Consulta [`agents/README.md`](agents/README.md) para instalarlo.

---

## 📚 Documentación Técnica

* [Diseño del Juego (`design.md`)](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/design.md)
* [Arquitectura Técnica (`architecture.md`)](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/architecture.md)
* [Backlog de Iteraciones y Épicas (`backlog.md`)](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/backlog.md)
* [Especificación de la API de Anfitrión (`docs/host-api.md`)](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/docs/host-api.md)
* [Guía del Modpack persistente (`docs/modpack.md`)](docs/modpack.md)
* [Servidor persistente desatendido (`docs/servidor-persistente.md`)](docs/servidor-persistente.md)
* [Decisiones de Arquitectura (`docs/adr/`)](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/docs/adr)

---

## 🏗️ Estructura de la Solución

```text
Pelican4Ever.sln
├── Pelican4Ever.Core/      # Lógica de dominio pura (.NET 6, cero dependencias de SMAPI ni MonoGame)
│   ├── Admin/              # API de anfitrión, cola en hilo principal, eventos y reinicio seguro
│   ├── Config/             # ServerConfig con validación determinista estricta
│   ├── Economy/            # Presupuesto de saciedad, spa, multiplicadores y mercado vecinal
│   ├── Festivals/          # Tandas de inscripción, ceremonias, puntuaciones y premios
│   ├── Players/            # PlayerRecord, derivación de SleepState y SleepPolicy
│   ├── Time/               # GameClock, ServerCalendar, ServerTime y CatchUpPlan
│   └── World/              # ServerWorldState, puertas, anexos, interiores y culling
├── Pelican4Ever/           # Mod SMAPI (.NET 6, ensamblado con parches Harmony)
│   ├── Framework/          # ModContext, IModule, routing espacial y clonación de mapas
│   └── Modules/            # 16 módulos SMAPI desacoplados con registro defensivo
└── Pelican4Ever.Tests/     # Suite de 379 pruebas unitarias y de contrato en xUnit (.NET 8)
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
3. Ejecuta la suite de pruebas unitarias:
   ```bash
   dotnet test Pelican4Ever.Tests
   ```

---

## 🐳 Despliegue con Docker Compose (Servidor Dedicado 24/7)

El proyecto incluye soporte para ejecución desatendida mediante [docker-compose.yml](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/docker-compose.yml) apoyado sobre la imagen de `JunimoServer`:

1. Crea tu archivo de variables de entorno a partir de la plantilla:
   ```bash
   cp .env.example .env
   # Edita .env con tus credenciales de Steam para el Host dedicado
   ```
2. Configura tu token de administración seguro en `config.json` a partir de [config.example.json](file:///home/mtx/Documentos/Projects/Stardew%20Valley%20MORPG/config.example.json):
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
   - `5900`: VNC opcional para inspección visual.
