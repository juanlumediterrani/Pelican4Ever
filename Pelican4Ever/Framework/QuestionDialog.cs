using StardewValley;
using StardewValley.Menus;

namespace Pelican4Ever.Framework
{
    public static class QuestionDialog
    {
        /// <summary>
        /// Muestra una pregunta con respuesta por callback. Si ya hay un cuadro de diálogo abierto (estamos dentro
        /// del callback de otra pregunta), la difiere a su cierre: <c>GameLocation.answerDialogue</c> anula
        /// <c>afterQuestion</c> justo después de invocar el callback, así que una pregunta creada ahí perdería su respuesta.
        /// </summary>
        public static void Ask(string question, Response[] choices, GameLocation.afterQuestionBehavior onAnswer)
        {
            if (Game1.activeClickableMenu is not DialogueBox)
            {
                Game1.currentLocation?.createQuestionDialogue(question, choices, onAnswer);
                return;
            }

            Game1.afterFadeFunction? previous = Game1.afterDialogues;
            Game1.afterDialogues = () =>
            {
                previous?.Invoke();
                Game1.currentLocation?.createQuestionDialogue(question, choices, onAnswer);
            };
        }
    }
}
