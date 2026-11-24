# Stardew Valley & Fashion Sense Sprite Conventions

Guía exhaustiva para la creación de sprites, retratos (portraits) y hojas de animación de personajes para Stardew Valley utilizando Fashion Sense y RetroDiffusion.

---

## 1. Especificaciones de Hojas de Personaje (Character Spritesheets)

### 1.1 Estructura Fashion Sense (`body.png`)
Fashion Sense organiza las animaciones de cuerpo en una cuadrícula estricta de **4 columnas × 4 filas** (16 fotogramas):

| Fila | Coordenada Y (para 32px) | Dirección | Propiedad Fashion Sense | Fotogramas |
|:---:|:---:|:---:|:---:|:---:|
| **0** | `Y = 0` | **Frontal (Abajo / Down)** | `FrontBody` | Frame 0 (Reposo), 1 (Paso A), 2 (Reposo), 3 (Paso B) |
| **1** | `Y = 32` | **Lateral Derecho ($\rightarrow$)** | `RightBody` | Frame 0 (Reposo), 1 (Paso A), 2 (Reposo), 3 (Paso B) |
| **2** | `Y = 64` | **Trasera (Arriba / Up)** | `BackBody` | Frame 0 (Reposo), 1 (Paso A), 2 (Reposo), 3 (Paso B) |
| **3** | `Y = 96` | **Lateral Izquierdo ($\leftarrow$)** | `LeftBody` | Espejado horizontal de `RightBody` o dibujo explícito |

### 1.2 Dimensiones de Celda y Hoja
* **Granjero Vanilla:** 16 ancho × 32 alto $\rightarrow$ Hoja total: 64 × 128 px.
* **Personajes Voluminosos (Orcos, Armaduras, Razas Fantasía):**
  * **20 × 32 px:** Hoja total: **80 × 128 px**. (Muy recomendado para mantener proporción esbelta pero hombros fuertes).
  * **24 × 32 px:** Hoja total: **96 × 128 px**. (Estándar de Putre y Dr4co para cuernos, hombreras y capas anchas).
* **Pies / Centro de Masa:** Los pies deben apoyarse de manera consistente en `Y = 30` ó `31` dentro de la celda de 32px, con el centro de gravedad alineado en `X = Ancho / 2` (ej. `X = 9.5` en 20px, `X = 11.5` en 24px).

---

## 2. Principios de Animación y "Head-Lock"

En pixel art de baja resolución (16×32 a 24×32), cualquier variación no intencionada de 1 solo píxel en la cabeza produce un temblor ("jitter" o "cabeza flotante") que destruye la estética retro.

### 2.1 La Regla de Head-Lock
* La silueta de la cabeza (zona superior, típicamente `Y < 14` ó `15`) **se extrae del fotograma de reposo (Frame 0)** y se implanta rígidamente en los fotogramas de paso (Frame 1 y 3).
* **Rebote de paso (+1px vertical):** En los pasos de marcha (Frames 1 y 3), toda la cabeza se desplaza exactamente $+1\text{px}$ hacia abajo junto con el torso para simular la compresión natural del peso al caminar.
* **Espacio negativo (Separación de piernas):** En los fotogramas de paso (Frames 1 y 3) de la vista frontal y trasera, debe existir un espacio vacío (alfa=0) de al menos 1-2 píxeles entre los pantalones/botas para que la zancada sea claramente legible.

### 2.2 Temporización y Configuración JSON (`body.json`)
```json
{
  "Name": "NombrePersonaje",
  "Format": "7.5.0",
  "HideEyes": true,
  "HideHair": true,
  "HideSleeves": true,
  "FrontBody": {
    "StartingPosition": { "X": 0, "Y": 0 },
    "BodySize": { "Width": 24, "Length": 32 },
    "Scale": 4.0,
    "DisableGrayscale": true,
    "IdleAnimation": [{ "Frame": 0, "Duration": 1000 }],
    "MovementAnimation": [
      { "Frame": 0, "Duration": 140 },
      { "Frame": 1, "Duration": 140 },
      { "Frame": 2, "Duration": 140 },
      { "Frame": 3, "Duration": 140 }
    ]
  }
}
```

---

## 3. Retratos de Diálogo (Portraits)

* **Dimensiones exactas:** **64 × 64 px** (sin escala nativa).
* **Encuadre:** Busto (desde el pecho/hombros hasta la parte superior de la cabeza/cuernos).
* **Iluminación:** Fuente de luz superior-derecha (sol cálido de Pelican Town), sombras en la parte inferior-izquierda.
* **Paleta:** Debe compartir la misma paleta que el sprite de cuerpo para que ambos elementos sean visualmente idénticos.
* **Integración en Fashion Sense:** Se guarda como `portrait.png` (64×64) dentro de la carpeta del paquete junto a `body.png`.

---

## 4. Pipeline con RetroDiffusion API v2

RetroDiffusion genera pixel art nativo sin subpíxeles ni degradados falsos, eliminando la necesidad de desescalados destructivos.

### 4.1 Herramientas de RetroDiffusion para Stardew Valley

#### ✅ Herramientas Válidas (Basadas en Tu Sprite Existente)
Para personajes de Stardew Valley que ya tienen un diseño base (como Putre o Dr4co), **solo tienen sentido las herramientas Image-to-Image a resolución nativa**:

| Estilo / ID | Propósito en Stardew Valley | Tamaño | Coste | Parámetros Clave |
|---|---|:---:|:---:|---|
| `rd_advanced_animation__rotate` | **La herramienta clave**: Toma el sprite frontal nativo y genera la rotación completa de 8 ángulos sin inventar otro personaje. | **32×32** | $0.10 | `input_image: <b64>`, `return_spritesheet: true` |
| `rd_advanced_animation__walking` | Genera los fotogramas de marcha directamente a partir del sprite rotado. | **32×32** | $0.14 | `input_image: <b64>`, `prompt: "walking"` |
| `rd_advanced_animation__attack` | Genera animaciones de golpe/tajo a partir del sprite para acciones de herramientas/combate. | **32×32** | $0.14 | `input_image: <b64>`, `prompt: "attack"` |

#### ❌ Herramientas que NO Aplican a este Flujo ("No tocan")
* **`rd_animation__four_angle_walking` (48×48, $0.07):** Genera personajes desde texto a 48px. No conoce el sprite de tu personaje y la escala 48px es incompatible con los 32px de Stardew.
* **`rd_plus__character_turnaround` / `character_turnaround:rd_flux` (128×128, $0.06):** Concept art a alta resolución desde texto. No sirve para sprites de juego listos para SMAPI.
* **`rd_plus__low_res` ($0.06):** Generación de retratos desde texto cuando el personaje ya tiene su retrato oficial.

### 4.2 Protocolo de Inferencia Económica
1. **Verificación de coste previa:** Siempre enviar primero con `"check_cost": true` (dry-run gratuito).
2. **Eliminación de fondo nativa:** Pasar `"remove_bg": true` en estilos que lo soporten para obtener canal alfa binario directo.
3. **Control de Idempotencia:** Utilizar la cabecera `Idempotency-Key: <uuid>` para evitar cobros dobles en caso de timeout de red.
4. **Sondeo asíncrono:** Leer el `task_id` devuelto y consultar `GET /v2/inferences/tasks/{task_id}` cada 2 segundos hasta que el estado sea `succeeded`.
