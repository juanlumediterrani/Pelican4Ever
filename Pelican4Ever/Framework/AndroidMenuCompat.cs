using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pelican4Ever.Core.Economy;
using StardewValley.Menus;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Acceso reflexivo defensivo a miembros de <see cref="CarpenterMenu"/> y
    /// <see cref="PurchaseAnimalsMenu"/> cuya firma o existencia difiere en Stardew Valley Android,
    /// evitando referencias IL rotas al cargar el ensamblado bajo SMAPI Android.
    /// </summary>
    internal static class AndroidMenuCompat
    {
        private static readonly BindingFlags MemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly MethodInfo? PurchaseAnimalsGetOffScreenRowsMethod =
            AccessTools.Method(typeof(PurchaseAnimalsMenu), "GetOffScreenRows", new[] { typeof(int) });

        public static ClickableComponent? GetCarpenterDemolishButton(CarpenterMenu? menu) =>
            ReadMember<ClickableComponent>(menu, "demolishButton");

        public static bool IsCarpenterReadOnly(CarpenterMenu? menu) =>
            ReadMember(menu, "readOnly");

        public static int GetCarpenterBlueprintCount(CarpenterMenu? menu)
        {
            object? blueprints = ReadMemberObject(menu, "Blueprints");
            return blueprints is ICollection col ? col.Count : 0;
        }

        public static bool IsCarpenterUpgradeAction(CarpenterMenu? menu)
        {
            object? action = ReadMemberObject(menu, "Action");
            return action != null && string.Equals(action.ToString(), "Upgrade", StringComparison.OrdinalIgnoreCase);
        }

        public static void ReadCarpenterBlueprintInfo(
            CarpenterMenu? menu,
            out string? displayName,
            out int? buildDays,
            out int? buildCost,
            out List<MailOrderItemLine> materials)
        {
            displayName = null;
            buildDays = null;
            buildCost = null;
            materials = new List<MailOrderItemLine>();

            object? blueprint = ReadMemberObject(menu, "Blueprint");
            if (blueprint == null)
                return;

            displayName = ReadMember<string>(blueprint, "DisplayName");
            buildDays = ReadMemberNullableInt(blueprint, "BuildDays");
            buildCost = ReadMemberNullableInt(blueprint, "BuildCost");

            if (ReadMemberObject(blueprint, "BuildMaterials") is IEnumerable rawMaterials)
            {
                foreach (object? mat in rawMaterials)
                {
                    if (mat == null)
                        continue;
                    string? itemId = ReadMember<string>(mat, "ItemId");
                    int? amount = ReadMemberNullableInt(mat, "Amount");
                    if (string.IsNullOrWhiteSpace(itemId) || amount is not > 0)
                        continue;

                    materials.Add(new MailOrderItemLine
                    {
                        QualifiedItemId = itemId,
                        DisplayName = itemId,
                        Stack = amount.Value
                    });
                }
            }
        }

        public static void UpdatePurchaseAnimalsScrollArrows(PurchaseAnimalsMenu? menu, int animalCount)
        {
            if (menu == null || PurchaseAnimalsGetOffScreenRowsMethod == null)
                return;

            ClickableComponent? upArrow = ReadMember<ClickableComponent>(menu, "upArrow");
            ClickableComponent? downArrow = ReadMember<ClickableComponent>(menu, "downArrow");
            if (upArrow == null || downArrow == null)
                return;

            if (PurchaseAnimalsGetOffScreenRowsMethod.Invoke(null, new object[] { animalCount }) is int offScreenRows)
            {
                bool showScroll = offScreenRows > 0;
                upArrow.visible = showScroll;
                downArrow.visible = showScroll;
            }
        }

        private static T? ReadMember<T>(object? instance, string name) where T : class =>
            ReadMemberObject(instance, name) as T;

        private static bool ReadMember<T>(object? instance, string name, out T value) where T : struct
        {
            if (ReadMemberObject(instance, name) is T typed)
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }

        private static bool ReadMember(object? instance, string name, bool fallback) =>
            ReadMember<bool>(instance, name, out bool value) ? value : fallback;

        private static bool ReadMember(CarpenterMenu? instance, string name) =>
            ReadMember((object?)instance, name, fallback: false);

        private static int? ReadMemberNullableInt(object? instance, string name) =>
            ReadMemberObject(instance, name) is int value ? value : null;

        private static object? ReadMemberObject(object? instance, string name)
        {
            if (instance == null)
                return null;

            Type type = instance.GetType();
            PropertyInfo? prop = type.GetProperty(name, MemberFlags);
            if (prop != null && prop.GetIndexParameters().Length == 0)
                return prop.GetValue(instance);

            FieldInfo? field = type.GetField(name, MemberFlags);
            return field?.GetValue(instance);
        }
    }
}
