using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.World
{
    /// <summary>
    /// Motivo de rechazo al intentar adquirir un anexo de residencia (TASK-905).
    /// </summary>
    public enum AnnexPurchaseRejection
    {
        None,

        /// <summary>La residencia especificada no existe en el registro del mundo.</summary>
        ResidenceNotFound,

        /// <summary>El jugador no es el propietario de la residencia.</summary>
        NotResidenceOwner,

        /// <summary>El lado cardinal seleccionado no es un lado libre de anexo para esta residencia.</summary>
        SideNotFree,

        /// <summary>La residencia ya cuenta con un anexo adquirido en ese lado.</summary>
        AnnexAlreadyExists,

        /// <summary>La plantilla de anexo solicitada no existe en el catálogo.</summary>
        UnknownTemplate,

        /// <summary>El jugador no tiene fondos suficientes para adquirir el anexo.</summary>
        InsufficientFunds,

        /// <summary>Se ha alcanzado el límite máximo de anexos permitidos para esta residencia.</summary>
        MaxAnnexesReached
    }

    /// <summary>
    /// Resultado de una validación y compra de anexo temático.
    /// </summary>
    public sealed record AnnexPurchaseResult(
        bool Success,
        AnnexPurchaseRejection Rejection,
        AnnexAssignment? Assignment,
        int RemainingPlayerMoney,
        int PricePaid = 0
    );

    /// <summary>
    /// Lógica de dominio pura para la validación y compra de anexos temáticos de residencia (TASK-905).
    /// </summary>
    public static class AnnexPurchaseLogic
    {
        /// <summary>Multiplicador de precio por defecto para el primer anexo adquirido por una residencia (40% del precio base).</summary>
        public const float DefaultFirstAnnexMultiplier = 0.40f;

        /// <summary>Multiplicador de precio por defecto para el segundo anexo adquirido por una residencia (100% del precio base).</summary>
        public const float DefaultSecondAnnexMultiplier = 1.00f;

        /// <summary>
        /// Calcula el precio en oro de un anexo según la plantilla y el número de anexos previos que ya posee la residencia.
        /// El primer mapa cuesta bastante menos que el segundo (tarifa reducida para el primer anexo).
        /// </summary>
        public static int CalculatePrice(
            AnnexTemplate template,
            int existingAnnexCount,
            float firstMultiplier = DefaultFirstAnnexMultiplier,
            float secondMultiplier = DefaultSecondAnnexMultiplier)
        {
            if (template == null) return 0;
            float multiplier = existingAnnexCount <= 0 ? firstMultiplier : secondMultiplier;
            return Math.Max(0, (int)Math.Round(template.BasePriceGold * multiplier));
        }

        /// <summary>
        /// Calcula el precio en oro de un anexo según el ID de plantilla y el número de anexos previos que ya posee la residencia.
        /// </summary>
        public static int CalculatePrice(
            string templateId,
            int existingAnnexCount,
            float firstMultiplier = DefaultFirstAnnexMultiplier,
            float secondMultiplier = DefaultSecondAnnexMultiplier)
        {
            AnnexTemplate template = AnnexTemplateCatalog.GetTemplate(templateId);
            return CalculatePrice(template, existingAnnexCount, firstMultiplier, secondMultiplier);
        }

        /// <summary>
        /// Valida la adquisición de un anexo temático y genera la asignación si procede.
        /// </summary>
        public static AnnexPurchaseResult ValidateAndPurchase(
            long playerId,
            string residenceSlotId,
            AnnexSide side,
            string templateId,
            int playerMoney,
            DoorAssignment? residenceDoor,
            IReadOnlyCollection<AnnexSide> freeSides,
            ServerWorldState worldState,
            int maxAnnexesPerResidence = 2,
            float firstAnnexMultiplier = DefaultFirstAnnexMultiplier,
            float secondAnnexMultiplier = DefaultSecondAnnexMultiplier)
        {
            if (residenceDoor == null)
            {
                return new AnnexPurchaseResult(false, AnnexPurchaseRejection.ResidenceNotFound, null, playerMoney);
            }

            if (residenceDoor.OwnerId != playerId)
            {
                return new AnnexPurchaseResult(false, AnnexPurchaseRejection.NotResidenceOwner, null, playerMoney);
            }

            if (freeSides == null || !freeSides.Contains(side))
            {
                return new AnnexPurchaseResult(false, AnnexPurchaseRejection.SideNotFree, null, playerMoney);
            }

            if (worldState.FindAnnex(residenceSlotId, side) != null)
            {
                return new AnnexPurchaseResult(false, AnnexPurchaseRejection.AnnexAlreadyExists, null, playerMoney);
            }

            int existingAnnexesCount = worldState.GetAnnexesForResidence(residenceSlotId).Count;
            if (existingAnnexesCount >= maxAnnexesPerResidence)
            {
                return new AnnexPurchaseResult(false, AnnexPurchaseRejection.MaxAnnexesReached, null, playerMoney);
            }

            if (string.IsNullOrWhiteSpace(templateId) || !AnnexTemplateCatalog.Templates.ContainsKey(templateId))
            {
                return new AnnexPurchaseResult(false, AnnexPurchaseRejection.UnknownTemplate, null, playerMoney);
            }

            AnnexTemplate template = AnnexTemplateCatalog.GetTemplate(templateId);
            int price = CalculatePrice(template, existingAnnexesCount, firstAnnexMultiplier, secondAnnexMultiplier);
            if (playerMoney < price)
            {
                return new AnnexPurchaseResult(false, AnnexPurchaseRejection.InsufficientFunds, null, playerMoney, price);
            }

            string annexId = ServerWorldState.BuildAnnexId(residenceSlotId, side);
            var assignment = new AnnexAssignment
            {
                AnnexId = annexId,
                ResidenceSlotId = residenceSlotId,
                Side = side,
                TemplateId = template.Id,
                OwnerId = playerId,
                AcquiredAtUtc = DateTimeOffset.UtcNow
            };

            return new AnnexPurchaseResult(
                Success: true,
                Rejection: AnnexPurchaseRejection.None,
                Assignment: assignment,
                RemainingPlayerMoney: playerMoney - price,
                PricePaid: price
            );
        }
    }
}
