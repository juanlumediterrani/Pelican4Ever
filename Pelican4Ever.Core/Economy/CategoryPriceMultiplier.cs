namespace Pelican4Ever.Core.Economy
{
    public static class CategoryPriceMultiplier
    {
        public static float ForCategory(int category, float crop, float artisan, float fish, float mineral) => category switch
        {
            -75 or -79 or -80 => crop,
            -26 => artisan,
            -4 => fish,
            -2 or -12 or -15 => mineral,
            _ => 1f
        };
    }
}
