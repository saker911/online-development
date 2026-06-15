using Microsoft.AspNetCore.Routing;

namespace VehiclePermitSystemWeb.Models.ViewModels.Reports
{
    public sealed class ReportPaginationViewModel
    {
        public string ActionName { get; init; } = string.Empty;

        public string AriaLabel { get; init; } = string.Empty;

        public string Fragment { get; init; } = string.Empty;

        public string Theme { get; init; } = "primary";

        public int CurrentPage { get; init; }

        public int TotalPages { get; init; }

        public RouteValueDictionary RouteValues { get; init; } = new();
    }
}
