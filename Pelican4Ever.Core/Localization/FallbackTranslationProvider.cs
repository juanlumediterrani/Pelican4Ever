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
            ["security.pairing_success"] = "¡Dispositivo emparejado correctamente como '{{alias}}'!"
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
