using System.Collections.Generic;
using System.Threading.Tasks;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public interface IEquipmentService
{
    Task<List<EquipmentDto>> GetAllActiveEquipmentAsync();
    Task<List<EquipmentDto>> GetEquipmentByTypeAsync(EquipmentType type);
    Task<EquipmentDto?> GetEquipmentByIdAsync(int id);
    Task<EquipmentDto> CreateEquipmentAsync(CreateEquipmentDto dto);
    Task<EquipmentDto> UpdateEquipmentAsync(int id, UpdateEquipmentDto dto);
    Task ArchiveEquipmentAsync(int id);
    Task DeleteEquipmentAsync(int id);
}
