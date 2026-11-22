using System;
using System.Globalization;

namespace Pelican4Ever.Core.Time
{
    /// <summary>
    /// Parser puro de los argumentos de <c>p4e_time</c> / <c>/time</c>. Reproduce exactamente la gramática
    /// que ya aplica <c>RealTimeClockModule.AdjustTime</c>: no añade formatos ni límites nuevos.
    /// </summary>
    public static class TimeCommandParser
    {
        public enum Kind
        {
            Invalid,
            SetInvalid,
            Reset,
            Set,
            Hours,
            Minutes
        }

        /// <param name="Kind">Tipo de ajuste.</param>
        /// <param name="Value">Delta con signo (horas/minutos) u hora objetivo (<see cref="Kind.Set"/>).</param>
        /// <param name="Amount">Cantidad positiva tal como se escribió (horas/minutos).</param>
        /// <param name="IsMinus">El primer carácter era <c>-</c>.</param>
        public readonly record struct Parsed(Kind Kind, TimeSpan Value, double Amount, bool IsMinus);

        public static Parsed Parse(string input)
            => ParseWithConfirmation(input, out _);

        /// <summary>
        /// Parsea argumentos de <c>p4e_time</c> / <c>/time</c> aceptando un sufijo final explícito de
        /// confirmación (<c>confirm</c>, <c>confirmar</c>, <c>--confirm</c>, <c>--confirmar</c>).
        /// </summary>
        public static Parsed ParseWithConfirmation(string input, out bool isExplicitlyConfirmed)
        {
            isExplicitlyConfirmed = false;
            if (string.IsNullOrWhiteSpace(input))
                return new Parsed(Kind.Invalid, TimeSpan.Zero, 0, false);

            string trimmed = input.Trim();
            string[] tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2 && IsConfirmToken(tokens[^1]))
            {
                isExplicitlyConfirmed = true;
                trimmed = string.Join(" ", tokens, 0, tokens.Length - 1);
            }

            input = trimmed.ToLowerInvariant();

            if (input == "reset")
                return new Parsed(Kind.Reset, TimeSpan.Zero, 0, false);

            if (input.StartsWith("set ") || input.StartsWith("set="))
            {
                string val = input.Substring(4).Trim();
                if (TimeSpan.TryParse(val, out TimeSpan targetSpan)
                    || (int.TryParse(val, out int rawTime) && TryConvertRawTimeToSpan(rawTime, out targetSpan)))
                    return new Parsed(Kind.Set, targetSpan, 0, false);
                return new Parsed(Kind.SetInvalid, TimeSpan.Zero, 0, false);
            }

            bool isMinus = input.StartsWith("-");
            string numStr = input.TrimStart('+', '-').TrimEnd('h');

            if (numStr.EndsWith("m"))
            {
                if (double.TryParse(numStr.TrimEnd('m'), NumberStyles.Any, CultureInfo.InvariantCulture, out double minutes))
                    return new Parsed(Kind.Minutes, TimeSpan.FromMinutes(isMinus ? -minutes : minutes), minutes, isMinus);
            }
            else if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double hours))
            {
                return new Parsed(Kind.Hours, TimeSpan.FromHours(isMinus ? -hours : hours), hours, isMinus);
            }

            return new Parsed(Kind.Invalid, TimeSpan.Zero, 0, false);
        }

        private static bool TryConvertRawTimeToSpan(int raw, out TimeSpan span)
        {
            int hours = raw / 100;
            int mins = raw % 100;
            if (hours >= 0 && hours < 24 && mins >= 0 && mins < 60)
            {
                span = new TimeSpan(hours, mins, 0);
                return true;
            }
            span = TimeSpan.Zero;
            return false;
        }

        private static bool IsConfirmToken(string token)
            => token.Equals("confirm", StringComparison.OrdinalIgnoreCase)
               || token.Equals("confirmar", StringComparison.OrdinalIgnoreCase)
               || token.Equals("--confirm", StringComparison.OrdinalIgnoreCase)
               || token.Equals("--confirmar", StringComparison.OrdinalIgnoreCase);
    }
}
