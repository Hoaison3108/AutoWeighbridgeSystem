using AutoWeighbridgeSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Data.Configuration
{
    public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
    {
        public void Configure(EntityTypeBuilder<Customer> builder)
        {
            builder.HasKey(c => c.CustomerId);

            builder.Property(c => c.CustomerId).HasMaxLength(8);
            builder.Property(c => c.CustomerName).IsRequired().HasMaxLength(255);

            // DÒNG QUAN TRỌNG: Tự động lọc các bản ghi đã xóa
            builder.HasQueryFilter(c => !c.IsDeleted);

            builder.HasData(
                new Customer { CustomerId = "MX1", CustomerName = "Máy xay 1" },
                new Customer { CustomerId = "MX2", CustomerName = "Máy xay 2" },
                new Customer { CustomerId = "MX3", CustomerName = "Máy xay 3" }
            );
        }
    }
}
