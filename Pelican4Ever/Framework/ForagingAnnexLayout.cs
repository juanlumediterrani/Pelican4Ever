using Pelican4Ever.Core.World;
using xTile.Dimensions;
using xTile.Layers;
using xTile.Tiles;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Contiene los tiles retocados artesanalmente del Anexo Forestal con corrección exhaustiva
    /// de todos los patrones de copas (AlwaysFront), vallas rústicas, sombras y transiciones de bosque,
    /// garantizando adaptaciones direccionales orgánicas (North, South, East) y cierre total con bosque nativo.
    /// </summary>
    internal static class ForagingAnnexLayout
    {
        // Tuplas (layer: 0=Back, 1=Buildings, 2=Front, 3=AlwaysFront, x, y, tileIndex [-1 = null])
        private static readonly (byte Layer, byte X, byte Y, short Index)[] RetouchedOverrides = new (byte, byte, byte, short)[]
        {
            (0, 18, 0, 351), (0, 19, 0, 351), (0, 20, 0, 351), (0, 21, 0, 351), (0, 22, 0, 351), (0, 23, 0, 351), (0, 24, 0, 351), (0, 25, 0, 351),
            (0, 26, 0, 351), (0, 27, 0, 351), (0, 28, 0, 351), (0, 29, 0, 351), (0, 30, 0, 351), (0, 31, 0, 351), (0, 32, 0, 351), (0, 33, 0, 351),
            (0, 34, 0, 351), (0, 35, 0, 351), (0, 36, 0, 351), (0, 37, 0, 351), (0, 38, 0, 351), (0, 39, 0, 351), (0, 18, 1, 351), (0, 19, 1, 351),
            (0, 20, 1, 351), (0, 21, 1, 175), (0, 22, 1, 175), (0, 23, 1, 351), (0, 24, 1, 351), (0, 25, 1, 351), (0, 26, 1, 351), (0, 27, 1, 351),
            (0, 28, 1, 351), (0, 29, 1, 175), (0, 30, 1, 175), (0, 31, 1, 351), (0, 32, 1, 351), (0, 33, 1, 351), (0, 34, 1, 351), (0, 35, 1, 351),
            (0, 36, 1, 175), (0, 37, 1, 351), (0, 38, 1, 351), (0, 39, 1, 351), (0, 18, 2, 351), (0, 19, 2, 351), (0, 20, 2, 379), (0, 21, 2, 175),
            (0, 22, 2, 175), (0, 23, 2, 175), (0, 24, 2, 351), (0, 25, 2, 351), (0, 26, 2, 351), (0, 27, 2, 380), (0, 28, 2, 357), (0, 29, 2, 405),
            (0, 30, 2, 405), (0, 31, 2, 405), (0, 32, 2, 405), (0, 33, 2, 405), (0, 34, 2, 405), (0, 35, 2, 405), (0, 36, 2, 175), (0, 37, 2, 175),
            (0, 38, 2, 351), (0, 39, 2, 351), (0, 17, 3, 354), (0, 18, 3, 405), (0, 19, 3, 405), (0, 20, 3, 405), (0, 21, 3, 405), (0, 22, 3, 405),
            (0, 23, 3, 405), (0, 24, 3, 405), (0, 25, 3, 405), (0, 26, 3, 405), (0, 27, 3, 405), (0, 28, 3, 406), (0, 29, 3, 353), (0, 30, 3, 276),
            (0, 31, 3, 277), (0, 32, 3, 277), (0, 33, 3, 278), (0, 34, 3, 376), (0, 35, 3, 376), (0, 36, 3, 175), (0, 37, 3, 175), (0, 38, 3, 351),
            (0, 39, 3, 351), (0, 17, 4, 353), (0, 18, 4, 376), (0, 19, 4, 376), (0, 20, 4, 376), (0, 21, 4, 376), (0, 22, 4, 376), (0, 23, 4, 376),
            (0, 24, 4, 376), (0, 25, 4, 376), (0, 26, 4, 376), (0, 27, 4, 376), (0, 28, 4, 376), (0, 29, 4, 377), (0, 30, 4, 175), (0, 31, 4, 175),
            (0, 32, 4, 175), (0, 33, 4, 175), (0, 34, 4, 175), (0, 35, 4, 175), (0, 36, 4, 175), (0, 37, 4, 175), (0, 38, 4, 351), (0, 39, 4, 351),
            (0, 18, 5, 275), (0, 19, 5, 275), (0, 20, 5, 275), (0, 21, 5, 275), (0, 22, 5, 275), (0, 23, 5, 275), (0, 24, 5, 275), (0, 25, 5, 275),
            (0, 26, 5, 175), (0, 27, 5, 175), (0, 28, 5, 175), (0, 29, 5, 175), (0, 30, 5, 175), (0, 31, 5, 175), (0, 32, 5, 175), (0, 33, 5, 175),
            (0, 34, 5, 175), (0, 35, 5, 175), (0, 36, 5, 175), (0, 37, 5, 175), (0, 38, 5, 351), (0, 39, 5, 351), (0, 19, 6, 200), (0, 20, 6, 179),
            (0, 21, 6, 201), (0, 22, 6, 179), (0, 23, 6, 180), (0, 24, 6, 180), (0, 25, 6, 179), (0, 26, 6, 180), (0, 27, 6, 180), (0, 28, 6, 180),
            (0, 29, 6, 180), (0, 30, 6, 179), (0, 31, 6, 180), (0, 32, 6, 180), (0, 33, 6, 205), (0, 34, 6, 203), (0, 35, 6, 175), (0, 36, 6, 175),
            (0, 37, 6, 175), (0, 38, 6, 351), (0, 39, 6, 351), (0, 34, 7, 228), (0, 35, 7, 175), (0, 36, 7, 175), (0, 37, 7, 175), (0, 38, 7, 351),
            (0, 39, 7, 351), (0, 34, 8, 228), (0, 35, 8, 175), (0, 36, 8, 175), (0, 37, 8, 175), (0, 38, 8, 351), (0, 39, 8, 351), (0, 34, 9, 228),
            (0, 35, 9, 175), (0, 36, 9, 175), (0, 37, 9, 175), (0, 38, 9, 351), (0, 39, 9, 351), (0, 34, 10, 228), (0, 35, 10, 175), (0, 36, 10, 175),
            (0, 37, 10, 175), (0, 38, 10, 351), (0, 39, 10, 351), (0, 34, 11, 228), (0, 35, 11, 175), (0, 36, 11, 175), (0, 37, 11, 175), (0, 38, 11, 351),
            (0, 39, 11, 351), (0, 34, 12, 228), (0, 35, 12, 175), (0, 36, 12, 175), (0, 37, 12, 175), (0, 38, 12, 351), (0, 39, 12, 351), (0, 34, 13, 228),
            (0, 35, 13, 175), (0, 36, 13, 175), (0, 37, 13, 175), (0, 38, 13, 351), (0, 39, 13, 351), (0, 34, 14, 228), (0, 35, 14, 175), (0, 36, 14, 175),
            (0, 37, 14, 175), (0, 38, 14, 351), (0, 39, 14, 351), (0, 34, 15, 228), (0, 35, 15, 175), (0, 36, 15, 175), (0, 37, 15, 175), (0, 38, 15, 351),
            (0, 39, 15, 351), (0, 34, 16, 228), (0, 35, 16, 175), (0, 36, 16, 175), (0, 37, 16, 175), (0, 38, 16, 351), (0, 39, 16, 351), (0, 34, 17, 228),
            (0, 35, 17, 175), (0, 36, 17, 175), (0, 37, 17, 175), (0, 38, 17, 351), (0, 39, 17, 351), (0, 34, 18, 228), (0, 35, 18, 175), (0, 36, 18, 175),
            (0, 37, 18, 175), (0, 38, 18, 351), (0, 39, 18, 351), (0, 34, 19, 228), (0, 35, 19, 175), (0, 36, 19, 175), (0, 37, 19, 175), (0, 38, 19, 351),
            (0, 39, 19, 351), (0, 34, 20, 228), (0, 35, 20, 175), (0, 36, 20, 175), (0, 37, 20, 175), (0, 38, 20, 351), (0, 39, 20, 351), (0, 34, 21, 228),
            (0, 35, 21, 175), (0, 36, 21, 175), (0, 37, 21, 175), (0, 38, 21, 351), (0, 39, 21, 351), (0, 18, 22, 175), (0, 19, 22, 175), (0, 20, 22, 175),
            (0, 21, 22, 175), (0, 34, 22, 228), (0, 35, 22, 175), (0, 36, 22, 175), (0, 37, 22, 175), (0, 38, 22, 351), (0, 39, 22, 351), (0, 18, 23, 175),
            (0, 19, 23, 175), (0, 20, 23, 175), (0, 21, 23, 175), (0, 24, 23, 225), (0, 34, 23, 228), (0, 35, 23, 175), (0, 36, 23, 175), (0, 37, 23, 175),
            (0, 38, 23, 351), (0, 39, 23, 351), (0, 18, 24, 175), (0, 19, 24, 175), (0, 20, 24, 175), (0, 21, 24, 175), (0, 24, 24, 250), (0, 25, 24, 252),
            (0, 34, 24, 228), (0, 35, 24, 175), (0, 36, 24, 175), (0, 37, 24, 175), (0, 38, 24, 351), (0, 39, 24, 351), (0, 18, 25, 175), (0, 19, 25, 175),
            (0, 20, 25, 175), (0, 21, 25, 175), (0, 22, 25, 251), (0, 24, 25, 175), (0, 25, 25, 225), (0, 28, 25, 178), (0, 29, 25, 251), (0, 30, 25, 251),
            (0, 31, 25, 251), (0, 32, 25, 251), (0, 33, 25, 251), (0, 34, 25, 253), (0, 35, 25, 175), (0, 36, 25, 175), (0, 37, 25, 175), (0, 38, 25, 351),
            (0, 39, 25, 351), (0, 24, 26, 175), (0, 25, 26, 225), (0, 28, 26, 228), (0, 29, 26, 175), (0, 30, 26, 175), (0, 31, 26, 175), (0, 32, 26, 175),
            (0, 33, 26, 175), (0, 34, 26, 175), (0, 35, 26, 175), (0, 36, 26, 175), (0, 37, 26, 175), (0, 38, 26, 351), (0, 39, 26, 351), (0, 24, 27, 175),
            (0, 25, 27, 225), (0, 28, 27, 228), (0, 29, 27, 175), (0, 30, 27, 175), (0, 31, 27, 175), (0, 32, 27, 175), (0, 33, 27, 175), (0, 34, 27, 175),
            (0, 35, 27, 175), (0, 36, 27, 175), (0, 37, 27, 175), (0, 38, 27, 351), (0, 39, 27, 175), (0, 24, 28, 175), (0, 25, 28, 225), (0, 26, 28, 587),
            (0, 27, 28, 587), (0, 28, 28, 228), (0, 29, 28, 175), (0, 30, 28, 175), (0, 31, 28, 175), (0, 32, 28, 175), (0, 33, 28, 175), (0, 34, 28, 175),
            (0, 35, 28, 175), (0, 36, 28, 175), (0, 37, 28, 175), (0, 38, 28, 351), (0, 39, 28, 326), (0, 24, 29, 175), (0, 25, 29, 225), (0, 26, 29, 587),
            (0, 28, 29, 228), (0, 29, 29, 326), (0, 30, 29, 326), (0, 31, 29, 326), (0, 32, 29, 326), (0, 33, 29, 326), (0, 34, 29, 326), (0, 35, 29, 326),
            (0, 36, 29, 326), (0, 37, 29, 175), (0, 38, 29, 351), (0, 39, 29, 351), (0, 19, 30, 1094), (0, 20, 30, 1094), (0, 21, 30, 1094), (0, 22, 30, 1094),
            (0, 23, 30, 1094), (0, 24, 30, 1094), (0, 25, 30, 225), (0, 28, 30, 228), (0, 29, 30, 351), (0, 30, 30, 351), (0, 31, 30, 351), (0, 32, 30, 351),
            (0, 33, 30, 351), (0, 34, 30, 351), (0, 35, 30, 351), (0, 36, 30, 351), (0, 37, 30, 351), (0, 38, 30, 351), (0, 39, 30, 351), (0, 18, 31, 1094),
            (0, 19, 31, 1094), (0, 20, 31, 1094), (0, 21, 31, 1094), (0, 22, 31, 1094), (0, 23, 31, 1094), (0, 24, 31, 1094), (0, 25, 31, 225), (0, 27, 31, 587),
            (0, 28, 31, 228), (0, 29, 31, 351), (0, 30, 31, 351), (0, 31, 31, 351), (0, 32, 31, 351), (0, 33, 31, 351), (0, 34, 31, 351), (0, 35, 31, 351),
            (0, 36, 31, 351), (0, 37, 31, 351), (0, 38, 31, 351), (0, 39, 31, 351), (1, 18, 0, 16), (1, 19, 0, 16), (1, 20, 0, 16), (1, 21, 0, 16),
            (1, 22, 0, 16), (1, 23, 0, 16), (1, 24, 0, 16), (1, 25, 0, 16), (1, 26, 0, 16), (1, 27, 0, 16), (1, 28, 0, 16), (1, 29, 0, 16),
            (1, 30, 0, 16), (1, 31, 0, 16), (1, 32, 0, 16), (1, 33, 0, 16), (1, 34, 0, 16), (1, 35, 0, 16), (1, 36, 0, 16), (1, 37, 0, 386),
            (1, 38, 0, 16), (1, 39, 0, 16), (1, 18, 1, 16), (1, 19, 1, 16), (1, 20, 1, 16), (1, 23, 1, 16), (1, 24, 1, 16), (1, 25, 1, 16),
            (1, 26, 1, 16), (1, 27, 1, 16), (1, 28, 1, 1142), (1, 29, 1, 1143), (1, 30, 1, 1142), (1, 31, 1, 1143), (1, 32, 1, 16), (1, 33, 1, 1142),
            (1, 34, 1, 1143), (1, 35, 1, 1142), (1, 36, 1, 1143), (1, 37, 1, 386), (1, 38, 1, 16), (1, 39, 1, 16), (1, 18, 2, 16), (1, 19, 2, 16),
            (1, 21, 2, 1142), (1, 22, 2, 1143), (1, 23, 2, 1142), (1, 24, 2, 1143), (1, 25, 2, 1142), (1, 26, 2, 1143), (1, 27, 2, 1142), (1, 28, 2, 1143),
            (1, 37, 2, 386), (1, 38, 2, 16), (1, 39, 2, 16), (1, 37, 3, 386), (1, 38, 3, 16), (1, 39, 3, 16), (1, 37, 4, 386), (1, 38, 4, 16),
            (1, 39, 4, 16), (1, 37, 5, 386), (1, 38, 5, 16), (1, 39, 5, 16), (1, 37, 6, 386), (1, 38, 6, 16), (1, 39, 6, 16), (1, 37, 7, 386),
            (1, 38, 7, 16), (1, 39, 7, 16), (1, 37, 8, 386), (1, 38, 8, 16), (1, 39, 8, 16), (1, 37, 9, 386), (1, 38, 9, 16), (1, 39, 9, 16),
            (1, 37, 10, 386), (1, 38, 10, 16), (1, 39, 10, 16), (1, 37, 11, 386), (1, 38, 11, 16), (1, 39, 11, 16), (1, 37, 12, 386), (1, 38, 12, 16),
            (1, 39, 12, 16), (1, 37, 13, 386), (1, 38, 13, 16), (1, 39, 13, 16), (1, 37, 14, 386), (1, 38, 14, 16), (1, 39, 14, 16), (1, 37, 15, 386),
            (1, 38, 15, 16), (1, 39, 15, 16), (1, 37, 16, 386), (1, 38, 16, 16), (1, 39, 16, 16), (1, 37, 17, 386), (1, 38, 17, 16), (1, 39, 17, 16),
            (1, 37, 18, 386), (1, 38, 18, 16), (1, 39, 18, 16), (1, 37, 19, 386), (1, 38, 19, 16), (1, 39, 19, 16), (1, 37, 20, 386), (1, 38, 20, 16),
            (1, 39, 20, 16), (1, 37, 21, 386), (1, 38, 21, 16), (1, 39, 21, 16), (1, 37, 22, 386), (1, 38, 22, 16), (1, 39, 22, 16), (1, 37, 23, 386),
            (1, 38, 23, 16), (1, 39, 23, 16), (1, 37, 24, 386), (1, 38, 24, 16), (1, 39, 24, 16), (1, 37, 25, 386), (1, 38, 25, 16), (1, 39, 25, 16),
            (1, 37, 26, 386), (1, 38, 26, 16), (1, 39, 26, 16), (1, 37, 27, 386), (1, 38, 27, 16), (1, 39, 27, 16), (1, 25, 28, 386), (1, 28, 28, 386),
            (1, 37, 28, 386), (1, 38, 28, 16), (1, 39, 28, 16), (1, 25, 29, 386), (1, 28, 29, 386), (1, 29, 29, 16), (1, 30, 29, 16), (1, 31, 29, 16),
            (1, 32, 29, 16), (1, 33, 29, 16), (1, 34, 29, 16), (1, 35, 29, 16), (1, 36, 29, 16), (1, 37, 29, 16), (1, 38, 29, 16), (1, 39, 29, 16),
            (1, 25, 30, 386), (1, 28, 30, 386), (1, 29, 30, 16), (1, 30, 30, 16), (1, 31, 30, 16), (1, 32, 30, 16), (1, 33, 30, 16), (1, 34, 30, 16),
            (1, 35, 30, 16), (1, 36, 30, 16), (1, 37, 30, 16), (1, 38, 30, 16), (1, 39, 30, 16), (1, 18, 31, -1), (1, 19, 31, 1144), (1, 20, 31, 1145),
            (1, 21, 31, 1144), (1, 22, 31, 1145), (1, 25, 31, 386), (1, 28, 31, 386), (1, 29, 31, 16), (1, 30, 31, 16), (1, 31, 31, 16), (1, 32, 31, 16),
            (1, 33, 31, 16), (1, 34, 31, 16), (1, 35, 31, 16), (1, 36, 31, 16), (1, 37, 31, 16), (1, 38, 31, 16), (1, 39, 31, 16), (2, 37, 0, 361),
            (2, 37, 1, 361), (2, 37, 2, 361), (2, 37, 3, 361), (2, 37, 4, 361), (2, 37, 5, 361), (2, 37, 6, 361), (2, 37, 7, 361), (2, 37, 8, 361),
            (2, 37, 9, 361), (2, 37, 10, 361), (2, 37, 11, 361), (2, 37, 12, 361), (2, 37, 13, 361), (2, 37, 14, 361), (2, 37, 15, 361), (2, 37, 16, 361),
            (2, 37, 17, 361), (2, 37, 18, 361), (2, 37, 19, 361), (2, 37, 20, 361), (2, 37, 21, 361), (2, 37, 22, 361), (2, 37, 23, 361), (2, 37, 24, 361),
            (2, 37, 25, 361), (2, 37, 26, 361), (2, 25, 27, 361), (2, 28, 27, 361), (2, 37, 27, 361), (2, 25, 28, 386), (2, 37, 28, 361), (2, 25, 29, 386),
            (2, 28, 29, 386), (2, 25, 30, 386), (2, 28, 30, 386), (2, 25, 31, 386), (2, 28, 31, 386), (3, 8, 0, 971), (3, 9, 0, 946), (3, 10, 0, 941),
            (3, 11, 0, 941), (3, 12, 0, 941), (3, 13, 0, 945), (3, 14, 0, 946), (3, 15, 0, 946), (3, 17, 0, 971), (3, 18, 0, 946), (3, 19, 0, 946),
            (3, 20, 0, 971), (3, 21, 0, 946), (3, 22, 0, 971), (3, 23, 0, 946), (3, 24, 0, 971), (3, 25, 0, 946), (3, 26, 0, 971), (3, 27, 0, 946),
            (3, 28, 0, 971), (3, 29, 0, 946), (3, 30, 0, 971), (3, 31, 0, 946), (3, 32, 0, 971), (3, 33, 0, 946), (3, 34, 0, 971), (3, 35, 0, 946),
            (3, 36, 0, 971), (3, 37, 0, 942), (3, 38, 0, 971), (3, 39, 0, 946), (3, 9, 1, 965), (3, 10, 1, 966), (3, 11, 1, 967), (3, 12, 1, 967),
            (3, 13, 1, 969), (3, 17, 1, 996), (3, 18, 1, 945), (3, 19, 1, 940), (3, 20, 1, 942), (3, 21, 1, 943), (3, 22, 1, 942), (3, 23, 1, 943),
            (3, 24, 1, 942), (3, 25, 1, 943), (3, 26, 1, 942), (3, 27, 1, 943), (3, 28, 1, 942), (3, 29, 1, 943), (3, 30, 1, 942), (3, 31, 1, 943),
            (3, 32, 1, 942), (3, 33, 1, 943), (3, 34, 1, 942), (3, 35, 1, 943), (3, 36, 1, 942), (3, 37, 1, 969), (3, 38, 1, 1020), (3, 39, 1, 971),
            (3, 9, 2, 965), (3, 16, 2, 971), (3, 18, 2, 942), (3, 19, 2, 966), (3, 20, 2, 967), (3, 21, 2, 968), (3, 22, 2, 967), (3, 23, 2, 968),
            (3, 24, 2, 967), (3, 25, 2, 968), (3, 26, 2, 967), (3, 27, 2, 968), (3, 28, 2, 967), (3, 29, 2, 968), (3, 30, 2, 967), (3, 31, 2, 968),
            (3, 32, 2, 967), (3, 33, 2, 968), (3, 34, 2, 967), (3, 35, 2, 968), (3, 36, 2, 967), (3, 37, 2, 994), (3, 38, 2, 995), (3, 39, 2, 946),
            (3, 16, 3, 942), (3, 18, 3, 967), (3, 19, 3, 968), (3, 37, 3, 1019), (3, 38, 3, 1020), (3, 39, 3, 946), (3, 37, 4, 994), (3, 38, 4, 995),
            (3, 39, 4, 946), (3, 37, 5, 1019), (3, 38, 5, 1020), (3, 39, 5, 946), (3, 37, 6, 994), (3, 38, 6, 995), (3, 39, 6, 946), (3, 37, 7, 1019),
            (3, 38, 7, 1020), (3, 39, 7, 946), (3, 37, 8, 994), (3, 38, 8, 995), (3, 39, 8, 946), (3, 37, 9, 1019), (3, 38, 9, 1020), (3, 39, 9, 946),
            (3, 37, 10, 994), (3, 38, 10, 995), (3, 39, 10, 946), (3, 37, 11, 1019), (3, 38, 11, 1020), (3, 39, 11, 946), (3, 37, 12, 994), (3, 38, 12, 995),
            (3, 39, 12, 946), (3, 37, 13, 1019), (3, 38, 13, 1020), (3, 39, 13, 946), (3, 37, 14, 994), (3, 38, 14, 995), (3, 39, 14, 946), (3, 37, 15, 1019),
            (3, 38, 15, 1020), (3, 39, 15, 946), (3, 37, 16, 994), (3, 38, 16, 995), (3, 39, 16, 946), (3, 37, 17, 1019), (3, 38, 17, 1020), (3, 39, 17, 946),
            (3, 37, 18, 994), (3, 38, 18, 995), (3, 39, 18, 946), (3, 37, 19, 1019), (3, 38, 19, 1020), (3, 39, 19, 946), (3, 37, 20, 994), (3, 38, 20, 995),
            (3, 39, 20, 946), (3, 37, 21, 1019), (3, 38, 21, 1020), (3, 39, 21, 946), (3, 20, 22, 971), (3, 37, 22, 994), (3, 38, 22, 995), (3, 39, 22, 946),
            (3, 37, 23, 1019), (3, 38, 23, 1020), (3, 39, 23, 946), (3, 22, 24, 990), (3, 37, 24, 994), (3, 38, 24, 995), (3, 39, 24, 946), (3, 37, 25, 1019),
            (3, 38, 25, 1020), (3, 39, 25, 946), (3, 21, 26, 971), (3, 22, 26, 990), (3, 23, 26, 991), (3, 37, 26, 994), (3, 38, 26, 995), (3, 39, 26, 946),
            (3, 22, 27, 965), (3, 23, 27, 1016), (3, 37, 27, 1019), (3, 38, 27, 1020), (3, 39, 27, 946), (3, 21, 28, 946), (3, 22, 28, 1065), (3, 23, 28, 1016),
            (3, 29, 28, 1041), (3, 30, 28, 1042), (3, 31, 28, 1043), (3, 32, 28, 1042), (3, 33, 28, 1043), (3, 34, 28, 1042), (3, 35, 28, 1043), (3, 36, 28, 1042),
            (3, 37, 28, 1044), (3, 38, 28, 995), (3, 39, 28, 946), (3, 19, 29, 946), (3, 20, 29, 946), (3, 21, 29, 946), (3, 22, 29, 1040), (3, 23, 29, 1041),
            (3, 24, 29, 1043), (3, 25, 29, 1042), (3, 28, 29, 1043), (3, 29, 29, 1066), (3, 30, 29, 1067), (3, 31, 29, 1068), (3, 32, 29, 1067), (3, 33, 29, 1068),
            (3, 34, 29, 1067), (3, 35, 29, 1068), (3, 36, 29, 1067), (3, 37, 29, 1069), (3, 38, 29, 1020), (3, 39, 29, 946), (3, 18, 30, 940), (3, 19, 30, 941),
            (3, 20, 30, 944), (3, 21, 30, 941), (3, 22, 30, 1040), (3, 23, 30, 941), (3, 24, 30, 1068), (3, 25, 30, 1067), (3, 28, 30, 1068), (3, 29, 30, 946),
            (3, 30, 30, 971), (3, 31, 30, 946), (3, 32, 30, 946), (3, 33, 30, 946), (3, 34, 30, 946), (3, 35, 30, 971), (3, 36, 30, 946), (3, 37, 30, 946),
            (3, 38, 30, 946), (3, 39, 30, 946), (3, 18, 31, 965), (3, 19, 31, 966), (3, 20, 31, 967), (3, 21, 31, 968), (3, 22, 31, 969), (3, 23, 31, 970),
            (3, 24, 31, 971), (3, 25, 31, 946), (3, 28, 31, 971), (3, 29, 31, 971), (3, 30, 31, 946), (3, 31, 31, 946), (3, 32, 31, 946), (3, 33, 31, 946),
            (3, 34, 31, 971), (3, 35, 31, 946), (3, 36, 31, 946), (3, 37, 31, 946), (3, 38, 31, 946), (3, 39, 31, 946),
        };

        public static void ApplyRetouchedNorthTiles(
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet outdoorsSheet)
        {
            Layer[] layerMap = { backLayer, bldLayer, frontLayer, alwaysFrontLayer };
            foreach (var (layerId, x, y, idx) in RetouchedOverrides)
            {
                Layer targetLayer = layerMap[layerId];
                if (idx < 0)
                {
                    targetLayer.Tiles[x, y] = null;
                }
                else
                {
                    var tile = new StaticTile(targetLayer, outdoorsSheet, BlendMode.Alpha, idx);
                    if (layerId == 0)
                    {
                        // Tipo de terreno Back para azada y cultivo:
                        if (idx is 587 or 200 or 179 or 201 or 180 or 205 or 203 or 225 or 228 or 250 or 252 or 275 or 153 or 560 or 589 or 463 or 488 or 513 or 614 or 2716 or 2732 or 2748 or 227 or 251 or 202 or 226 or 253)
                            tile.Properties["Type"] = "Dirt";
                        else
                            tile.Properties["Type"] = "Grass";

                        // Setos y desniveles del norte son impasables:
                        if (idx is 350 or 352 or 353 or 375 or 376 or 377 or 400 or 401 or 402 or 405 or 406 or 275 or 276 or 277 or 278)
                            tile.Properties["Passable"] = "F";
                        else
                            tile.Properties.Remove("Passable");
                    }
                    else if (layerId == 1)
                    {
                        // Sombras de copas (1142/1143) son 100% transitables; resto de obstáculos son sólidos:
                        if (idx is 1142 or 1143)
                            tile.Properties.Remove("Passable");
                        else
                            tile.Properties["Passable"] = "F";
                    }
                    targetLayer.Tiles[x, y] = tile;
                }
            }
        }

        /// <summary>
        /// Cierra la entrada sur de forma natural y completa con bosque nativo denso (sin falsas vallas de parche).
        /// </summary>
        public static void CloseSouthEntrance(
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet outdoorsSheet)
        {
            for (int ly = 26; ly < backLayer.LayerHeight; ly++)
            {
                for (int lx = 24; lx <= 28; lx++)
                {
                    frontLayer.Tiles[lx, ly] = null;
                    bldLayer.Tiles[lx, ly] = null;
                }
            }

            // ly = 26..27: Pradera soleada limpia
            for (int lx = 24; lx <= 28; lx++)
            {
                for (int ly = 26; ly <= 27; ly++)
                {
                    var gt = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, TerrainTileClassifier.SunnyGrassTileIndex);
                    gt.Properties["Type"] = "Grass";
                    gt.Properties.Remove("Passable");
                    backLayer.Tiles[lx, ly] = gt;
                    alwaysFrontLayer.Tiles[lx, ly] = null;
                }
            }

            // ly = 28: Festón superior de copa (AlwaysFront 1042/1043 sobre césped soleado 175)
            for (int lx = 24; lx <= 28; lx++)
            {
                var gt = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, TerrainTileClassifier.SunnyGrassTileIndex);
                gt.Properties["Type"] = "Grass";
                gt.Properties.Remove("Passable");
                backLayer.Tiles[lx, 28] = gt;
                alwaysFrontLayer.Tiles[lx, 28] = new StaticTile(alwaysFrontLayer, outdoorsSheet, BlendMode.Alpha, (lx % 2 == 0) ? 1042 : 1043);
            }

            // ly = 29: Festón inferior de copa (AlwaysFront 1067/1068, Back 326 sombreado, Buildings 16 tronco sólido)
            for (int lx = 24; lx <= 28; lx++)
            {
                var b29 = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 326);
                b29.Properties["Type"] = "Grass";
                b29.Properties.Remove("Passable");
                backLayer.Tiles[lx, 29] = b29;

                var bld29 = new StaticTile(bldLayer, outdoorsSheet, BlendMode.Alpha, 16);
                bld29.Properties["Passable"] = "F";
                bldLayer.Tiles[lx, 29] = bld29;

                alwaysFrontLayer.Tiles[lx, 29] = new StaticTile(alwaysFrontLayer, outdoorsSheet, BlendMode.Alpha, (lx % 2 == 0) ? 1067 : 1068);
            }

            // ly = 30..31: Dosel profundo continuo de bosque (AlwaysFront 946/971, Back 351, Buildings 16 sólido)
            for (int lx = 24; lx <= 28; lx++)
            {
                for (int ly = 30; ly < backLayer.LayerHeight; ly++)
                {
                    var b30 = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 351);
                    b30.Properties["Type"] = "Grass";
                    b30.Properties.Remove("Passable");
                    backLayer.Tiles[lx, ly] = b30;

                    var bld30 = new StaticTile(bldLayer, outdoorsSheet, BlendMode.Alpha, 16);
                    bld30.Properties["Passable"] = "F";
                    bldLayer.Tiles[lx, ly] = bld30;

                    alwaysFrontLayer.Tiles[lx, ly] = new StaticTile(alwaysFrontLayer, outdoorsSheet, BlendMode.Alpha, ((lx + ly) % 5 == 0) ? 971 : 946);
                }
            }
        }

        /// <summary>
        /// Abre la entrada Norte de la Variante Sur (lx = 26..27, ly = 0..5) conectando limpiamente con el campo de cultivo.
        /// </summary>
        public static void OpenNorthEntrance(
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet outdoorsSheet)
        {
            // Sendero central de tierra en x = 26..27 continuo desde ly = 0 hasta conectar con el claro en ly = 6:
            for (int ly = 0; ly <= 6; ly++)
            {
                for (int lx = 26; lx <= 27; lx++)
                {
                    var dirtTile = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, TerrainTileClassifier.DirtPlainTileIndex);
                    dirtTile.Properties["Type"] = "Dirt";
                    dirtTile.Properties.Remove("Passable");
                    backLayer.Tiles[lx, ly] = dirtTile;
                    bldLayer.Tiles[lx, ly] = null;
                    frontLayer.Tiles[lx, ly] = null;
                }
                var edgeL = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 225);
                edgeL.Properties["Type"] = "Dirt";
                edgeL.Properties.Remove("Passable");
                backLayer.Tiles[25, ly] = edgeL;
                bldLayer.Tiles[25, ly] = null;
                frontLayer.Tiles[25, ly] = null;

                var edgeR = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 228);
                edgeR.Properties["Type"] = "Dirt";
                edgeR.Properties.Remove("Passable");
                backLayer.Tiles[28, ly] = edgeR;
                bldLayer.Tiles[28, ly] = null;
                frontLayer.Tiles[28, ly] = null;
            }

            // Remate limpio del seto de arbustos en ly = 3..4:
            var hedgeL_top = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 352);
            hedgeL_top.Properties["Passable"] = "F";
            backLayer.Tiles[24, 3] = hedgeL_top;
            var hedgeL_bot = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 377);
            hedgeL_bot.Properties["Passable"] = "F";
            backLayer.Tiles[24, 4] = hedgeL_bot;

            var hedgeR_top = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 350);
            hedgeR_top.Properties["Passable"] = "F";
            backLayer.Tiles[29, 3] = hedgeR_top;
            var hedgeR_bot = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 375);
            hedgeR_bot.Properties["Passable"] = "F";
            backLayer.Tiles[29, 4] = hedgeR_bot;

            // Despejar copas sobre el sendero de entrada (lx = 25..28, ly = 0..6) sin cortes verticales:
            for (int ly = 0; ly <= 6; ly++)
            {
                for (int lx = 25; lx <= 28; lx++)
                {
                    alwaysFrontLayer.Tiles[lx, ly] = null;
                }
            }

            // Postes rústicos de valla flanqueando el acceso:
            var postL = new StaticTile(bldLayer, outdoorsSheet, BlendMode.Alpha, 386);
            postL.Properties["Passable"] = "F";
            bldLayer.Tiles[25, 2] = postL;
            frontLayer.Tiles[25, 1] = new StaticTile(frontLayer, outdoorsSheet, BlendMode.Alpha, 361);

            var postR = new StaticTile(bldLayer, outdoorsSheet, BlendMode.Alpha, 386);
            postR.Properties["Passable"] = "F";
            bldLayer.Tiles[28, 2] = postR;
            frontLayer.Tiles[28, 1] = new StaticTile(frontLayer, outdoorsSheet, BlendMode.Alpha, 361);
        }

        /// <summary>
        /// Abre la entrada Oeste de la Variante Este (lx = 0..3, ly = 14..15) conectando orgánicamente con el claro.
        /// </summary>
        public static void OpenWestEntrance(
            Layer backLayer,
            Layer bldLayer,
            Layer frontLayer,
            Layer alwaysFrontLayer,
            TileSheet outdoorsSheet)
        {
            // Conservar camino vertical de tierra en lx = 0..2 y remate derecho limpio en lx = 3:
            for (int ly = 14; ly <= 15; ly++)
            {
                var r0 = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 227);
                r0.Properties["Type"] = "Dirt";
                r0.Properties.Remove("Passable");
                backLayer.Tiles[0, ly] = r0;

                for (int lx = 1; lx <= 2; lx++)
                {
                    var d = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, TerrainTileClassifier.DirtPlainTileIndex);
                    d.Properties["Type"] = "Dirt";
                    d.Properties.Remove("Passable");
                    backLayer.Tiles[lx, ly] = d;
                    alwaysFrontLayer.Tiles[lx, ly] = null;
                }

                var edgeR = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 228);
                edgeR.Properties["Type"] = "Dirt";
                edgeR.Properties.Remove("Passable");
                backLayer.Tiles[3, ly] = edgeR;
                alwaysFrontLayer.Tiles[3, ly] = null;

                for (int lx = 0; lx <= 4; lx++)
                {
                    bldLayer.Tiles[lx, ly] = null;
                    frontLayer.Tiles[lx, ly] = null;
                    alwaysFrontLayer.Tiles[lx, ly] = null;
                }
                var g4 = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, TerrainTileClassifier.SunnyGrassTileIndex);
                g4.Properties.Remove("Passable");
                backLayer.Tiles[4, ly] = g4;
            }

            // Bordes de tierra superior e inferior:
            for (int lx = 1; lx <= 3; lx++)
            {
                var edgeT = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 201);
                edgeT.Properties["Type"] = "Dirt";
                edgeT.Properties.Remove("Passable");
                backLayer.Tiles[lx, 13] = edgeT;

                var edgeB = new StaticTile(backLayer, outdoorsSheet, BlendMode.Alpha, 251);
                edgeB.Properties["Type"] = "Dirt";
                edgeB.Properties.Remove("Passable");
                backLayer.Tiles[lx, 16] = edgeB;
            }

            // Despejar dosel sobre el sendero de entrada oeste (lx = 0..2, ly = 14..15) para que sea visible y abierto:
            for (int lx = 0; lx <= 2; lx++)
            {
                alwaysFrontLayer.Tiles[lx, 14] = null;
                alwaysFrontLayer.Tiles[lx, 15] = null;
            }

            // Postes rústicos de entrada al claro en x = 3:
            var postTop = new StaticTile(bldLayer, outdoorsSheet, BlendMode.Alpha, 386);
            postTop.Properties["Passable"] = "F";
            bldLayer.Tiles[3, 13] = postTop;
            frontLayer.Tiles[3, 13] = new StaticTile(frontLayer, outdoorsSheet, BlendMode.Alpha, 361);

            var postBot = new StaticTile(bldLayer, outdoorsSheet, BlendMode.Alpha, 386);
            postBot.Properties["Passable"] = "F";
            bldLayer.Tiles[3, 16] = postBot;
            frontLayer.Tiles[3, 16] = new StaticTile(frontLayer, outdoorsSheet, BlendMode.Alpha, 361);
        }

        public static void EnsureBorderCollision(
            Layer bldLayer,
            TileSheet outdoorsSheet,
            AnnexSide side,
            int width,
            int height)
        {
            if (side != AnnexSide.South)
            {
                for (int lx = 0; lx < width; lx++)
                {
                    if (bldLayer.Tiles[lx, 0] == null)
                    {
                        var b = new StaticTile(bldLayer, outdoorsSheet, BlendMode.Alpha, 16);
                        b.Properties["Passable"] = "F";
                        bldLayer.Tiles[lx, 0] = b;
                    }
                }
            }

            for (int lx = 0; lx < width; lx++)
            {
                if (side == AnnexSide.North && (lx == 26 || lx == 27))
                    continue;

                if (bldLayer.Tiles[lx, height - 1] == null)
                {
                    var b = new StaticTile(bldLayer, outdoorsSheet, BlendMode.Alpha, 16);
                    b.Properties["Passable"] = "F";
                    bldLayer.Tiles[lx, height - 1] = b;
                }
            }

            for (int ly = 0; ly < height; ly++)
            {
                if (side == AnnexSide.East && (ly == 14 || ly == 15))
                    continue;

                if (bldLayer.Tiles[0, ly] == null)
                {
                    var b = new StaticTile(bldLayer, outdoorsSheet, BlendMode.Alpha, 16);
                    b.Properties["Passable"] = "F";
                    bldLayer.Tiles[0, ly] = b;
                }
            }

            for (int ly = 0; ly < height; ly++)
            {
                if (bldLayer.Tiles[width - 1, ly] == null)
                {
                    var b = new StaticTile(bldLayer, outdoorsSheet, BlendMode.Alpha, 16);
                    b.Properties["Passable"] = "F";
                    bldLayer.Tiles[width - 1, ly] = b;
                }
            }
        }
    }
}
