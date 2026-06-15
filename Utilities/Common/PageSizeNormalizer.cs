namespace VehiclePermitSystemWeb.Utilities.Common
{
    public static class PageSizeNormalizer
    {
        public static int NormalizePageSize(int pageSize)
        {
            return pageSize switch
            {
                <= 5 => 5,
                <= 10 => 10,
                <= 20 => 20,
                <= 50 => 50,
                _ => 100,
            };
        }
    }
}
