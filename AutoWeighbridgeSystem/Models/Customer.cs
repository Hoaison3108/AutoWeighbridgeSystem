using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoWeighbridgeSystem.Models
{
    [Table("Customers")]
    public class Customer
    {
        [Key]
        [Required]
        [MaxLength(8)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)] // Không tự sinh ID
        public string CustomerId { get; set; }

        [Required]
        [MaxLength(255)]
        public string CustomerName { get; set; }

        public bool IsDeleted { get; set; } = false;
    }
}
