using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.Localization
{
    /// <summary>
    /// Proveedor por defecto con cadenas base de respaldo en castellano para pruebas unitarias sin inicializar SMAPI.
    /// </summary>
    public sealed class FallbackTranslationProvider : ITranslationProvider
    {
        private static readonly Dictionary<string, string> Defaults = new(StringComparer.OrdinalIgnoreCase)
        {
            ["common.yes"] = "Sí",
            ["common.no"] = "No",
            ["common.neighbor"] = "un vecino",
            ["dialog.cancel"] = "Cancelar",
            ["dialog.exit"] = "Salir",
            ["dialog.back"] = "Volver",
            ["dialog.understood"] = "Entendido",

            ["residence.permission.closed"] = "cerrada",
            ["residence.permission.friends_only"] = "solo amigos",
            ["residence.permission.open"] = "abierta",
            ["residence.permission.visitors_can_edit"] = "{{access}}, visitas pueden editar",
            ["residence.permission.visitors_view_only"] = "{{access}}, visitas solo miran",

            ["catalog.header"] = "=== RESIDENCIAS DE LA PLAZA ===",
            ["catalog.none_free"] = "{{occupied}} ocupadas, ninguna libre.",
            ["catalog.prompt_walk"] = "{{occupied}} ocupadas, {{free}} libres. Camina hacia un portal libre de la plaza para fundar la tuya.",
            ["catalog.free"] = "[Libre] {{door}}: {{location}} ({{x}},{{y}}) - {{desc}}",
            ["catalog.occupied"] = "[Ocupada] {{door}} ({{owner}}): {{location}} ({{x}},{{y}})",
            ["catalog.summary_line"] = "{{door}}: {{owner}}{{type}} - {{location}}",
            ["catalog.summary_count"] = "{{occupied}} ocupadas, {{free}} libres.",
            ["catalog.none_occupied"] = "Ninguna ocupada todavía.",
            ["catalog.registered_doors"] = "Residencias registradas ({{count}} disponibles):",

            ["festival.score_board_empty"] = "=== Tablón Oficial: {{title}} ===^^Todavía no hay marcas registradas en el evento de hoy.^^¡Habla con el alcalde Lewis para participar en la próxima tanda!^^El premio oficial se entregará a la mejor marca al cierre del festival (23:50).",
            ["festival.score_board_header"] = "=== Clasificación Oficial: {{title}} ===^^",
            ["festival.score_board_footer"] = "^^El ganador con la mayor puntuación de la jornada recibirá el premio oficial a las 23:50.",
            ["festival.rank_1"] = "1. [1er Puesto]",
            ["festival.rank_2"] = "2. [2º Puesto]",
            ["festival.rank_3"] = "3. [3er Puesto]",

            ["mailbox.empty"] = "Tu buzón está vacío.",
            ["mailbox.unassigned"] = "Este buzón pertenece a una residencia desocupada.",
            ["mailbox.letters_count"] = "Tienes {{count}} carta(s) por leer.",
            ["mailbox.restricted"] = "Buzón de {{neighbor}}. Solo su propietario puede abrir el correo.",

            ["market.invalid_item"] = "El objeto no es válido.",
            ["market.cannot_buy_own"] = "No puedes comprar tu propia oferta.",
            ["market.not_enough_gold"] = "No tienes suficiente oro. Requiere {{price}}g (tienes {{current}}g).",
            ["market.already_sold"] = "No se puede retirar una oferta que ya ha sido vendida.",
            ["market.stack_greater_than_zero"] = "La cantidad debe ser mayor a 0.",
            ["market.price_range"] = "El precio debe situarse entre {{min}}g y {{max}}g.",
            ["market.listing_not_found"] = "Oferta no encontrada.",
            ["market.already_bought"] = "Esta oferta ya ha sido adquirida.",
            ["market.purchase_success"] = "Has adquirido {{item}} (x{{stack}}) por {{price}}g.",
            ["market.not_the_seller"] = "Solo el vendedor original puede retirar esta oferta.",
            ["market.withdrawn_success"] = "Has retirado {{item}} (x{{stack}}) del mercado.",

            ["bed_logout.host_agent_active"] = "Descanso registrado. Modo anfitrión agente activo: el servidor permanece en línea.",
            ["bed_logout.disconnecting"] = "Descanso registrado. Desconectando de forma segura...",

            ["security.pairing_invalid_device"] = "Tu cliente no proporcionó un identificador de dispositivo válido.",
            ["security.pairing_code_empty"] = "El código de emparejamiento no puede estar vacío.",
            ["security.pairing_no_active_invite"] = "No hay ninguna invitación de emparejamiento activa para tu conexión o ha caducado.",
            ["security.pairing_incorrect_code"] = "Código de emparejamiento incorrecto.",
            ["security.pairing_ip_mismatch"] = "Este código de emparejamiento está vinculado a otra dirección IP.",
            ["security.pairing_success"] = "¡Dispositivo emparejado correctamente como '{{alias}}'!",

            ["side.north"] = "Norte",
            ["side.east"] = "Este",
            ["side.south"] = "Sur",
            ["side.west"] = "Oeste",

            ["badge.claim_slot"] = "[E / Clic o Caminar] Fundar Parcela",
            ["badge.my_slot"] = "🏠 Mi Parcela (Caminar para entrar)",
            ["badge.neighbor_slot"] = "🏠 Parcela de {{owner}}",
            ["badge.annex_enter"] = "🌲 {{name}} (Caminar para entrar)",
            ["badge.annex_purchase"] = "[E / Clic o Caminar] Adquirir Anexo",
            ["badge.annex_unavailable"] = "🚫 Ampliación no disponible",

            ["annex.rejection.insufficient_funds"] = "No dispones de suficiente dinero para adquirir este anexo.",
            ["annex.rejection.not_residence_owner"] = "Solo el propietario de la residencia puede adquirir anexos.",
            ["annex.rejection.already_exists"] = "Ya existe un anexo adquirido en este acceso.",
            ["annex.rejection.side_not_free"] = "Este acceso no admite un anexo.",
            ["annex.rejection.max_reached"] = "Has alcanzado el límite máximo de anexos para esta residencia.",
            ["annex.rejection.default"] = "No se pudo adquirir el anexo.",

            ["cave.dialog_prompt"] = "¿Cómo te gustaría preparar tu cueva privada?",
            ["cave.choice_mushrooms"] = "Champiñones",
            ["cave.choice_bats"] = "Murciélagos de la fruta",
            ["cave.selected_mushrooms"] = "Tu cueva ha sido preparada con cajas de champiñones.",
            ["cave.selected_bats"] = "Tu cueva ha sido preparada para atraer murciélagos de la fruta.",

            ["location.host_farm"] = "Granja Host",
            ["location.bridge"] = "Puente {{number}}",
            ["location.farm_slot"] = "Granja {{number}} ({{type}})",
            ["location.cave_slot"] = "Cueva Parcela {{slot}}",
            ["location.cellar_slot"] = "Bodega Parcela {{slot}}",
            ["location.greenhouse_slot"] = "Invernadero Parcela {{slot}}",
            ["location.annex_slot"] = "Anexo {{side}} ({{type}})",

            ["farm_template.Standard.name"] = "Granja Estándar",
            ["farm_template.Foraging.name"] = "Granja Forestal",
            ["farm_template.Fishing.name"] = "Granja Fluvial",
            ["farm_template.Mining.name"] = "Granja Minera",
            ["farm_template.Combat.name"] = "Granja Salvaje",
            ["farm_template.FourCorners.name"] = "Granja Cuatro Esquinas",
            ["farm_template.Ranching.name"] = "Granja Pradera",
            ["farm_template.Beach.name"] = "Granja de la Playa",

            ["door.Farm_Portal_01.description"] = "Parcela 01 (Granja Host - Norte Oeste)",
            ["door.Farm_Portal_02.description"] = "Parcela 02 (Granja Host - Norte Este)",
            ["door.Farm_Portal_03.description"] = "Parcela 03 (Granja Host - Sur Oeste)",
            ["door.Farm_Portal_04.description"] = "Parcela 04 (Granja Host - Sur Este)",
            ["door.Farm_Portal_05.description"] = "Parcela 05 (Puente 1 - Norte Izquierda)",
            ["door.Farm_Portal_06.description"] = "Parcela 06 (Puente 1 - Norte Derecha)",
            ["door.Farm_Portal_07.description"] = "Parcela 07 (Puente 1 - Sur Izquierda)",
            ["door.Farm_Portal_08.description"] = "Parcela 08 (Puente 1 - Sur Derecha)",
            ["door.Farm_Portal_09.description"] = "Parcela 09 (Puente 2 - Norte Izquierda)",
            ["door.Farm_Portal_10.description"] = "Parcela 10 (Puente 2 - Norte Derecha)",
            ["door.Farm_Portal_11.description"] = "Parcela 11 (Puente 2 - Sur Izquierda)",
            ["door.Farm_Portal_12.description"] = "Parcela 12 (Puente 2 - Sur Derecha)",
            ["door.Farm_Portal_13.description"] = "Parcela 13 (Puente 3 - Norte Izquierda)",
            ["door.Farm_Portal_14.description"] = "Parcela 14 (Puente 3 - Norte Derecha)",
            ["door.Farm_Portal_15.description"] = "Parcela 15 (Puente 3 - Sur Izquierda)",
            ["door.Farm_Portal_16.description"] = "Parcela 16 (Puente 3 - Sur Derecha)"
        };

        public string Get(string key)
        {
            if (Defaults.TryGetValue(key, out string? text))
                return text;
            return key;
        }

        public string Get(string key, object? tokens)
        {
            string template = Get(key);
            return TokenInterpolator.Interpolate(template, tokens);
        }
    }
}
