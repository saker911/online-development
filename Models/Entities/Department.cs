using System.ComponentModel.DataAnnotations;

namespace VehiclePermitSystemWeb.Models.Entities
{
    public class Department
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "اسم القسم مطلوب.")]
        [StringLength(128, ErrorMessage = "اسم القسم يجب ألا يتجاوز 128 حرفًا.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(64, ErrorMessage = "اسم مستخدم مدير القسم يجب ألا يتجاوز 64 حرفًا.")]
        public string ManagerUsername { get; set; } = string.Empty;

        [StringLength(128, ErrorMessage = "اسم مدير القسم يجب ألا يتجاوز 128 حرفًا.")]
        public string ManagerDisplayName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}
