using AutoWeighbridgeSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services
{
    public interface IExportService
    {
        Task ExportTicketsToExcelAsync(IEnumerable<WeighingTicket> tickets, string reportTitle);
    }
}
