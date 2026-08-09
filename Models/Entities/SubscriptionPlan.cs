namespace VehiclePermitSystemWeb.Models.Entities
{
    public sealed class SubscriptionPlan
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public int DurationMonths { get; set; }
        public decimal Price { get; set; }
        public decimal? OriginalPrice { get; set; }
        public string OfferLabel { get; set; } = string.Empty;
        public DateTime? OfferStartsAtUtc { get; set; }
        public DateTime? OfferEndsAtUtc { get; set; }
        public bool IsFeatured { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public string FeaturesJson { get; set; } = "[]";
        public DateTime UpdatedAtUtc { get; set; }
    }
}
