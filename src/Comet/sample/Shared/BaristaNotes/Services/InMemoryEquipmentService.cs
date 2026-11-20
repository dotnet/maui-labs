using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CometBaristaNotes.Data;
using CometBaristaNotes.Models;
using CometBaristaNotes.Models.Enums;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public sealed class InMemoryEquipmentService : IEquipmentService
{
    private readonly IBaristaDataStore _store;
    private readonly IDataChangeNotifier _notifier;

    public InMemoryEquipmentService(IBaristaDataStore store, IDataChangeNotifier notifier)
    {
        _store = store;
        _notifier = notifier;
    }

    public Task<List<EquipmentDto>> GetAllActiveEquipmentAsync()
        => Task.FromResult(_store.Equipment.Where(e => e.IsActive && !e.IsDeleted).Select(MapToDto).ToList());

    public Task<List<EquipmentDto>> GetEquipmentByTypeAsync(EquipmentType type)
        => Task.FromResult(_store.Equipment.Where(e => e.Type == type && e.IsActive && !e.IsDeleted).Select(MapToDto).ToList());

    public Task<EquipmentDto?> GetEquipmentByIdAsync(int id)
    {
        var e = _store.Equipment.FirstOrDefault(x => x.Id == id && !x.IsDeleted);
        return Task.FromResult(e == null ? null : MapToDto(e));
    }

    public Task<EquipmentDto> CreateEquipmentAsync(CreateEquipmentDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Name is required");
        if (dto.Name.Length > 100)
            throw new ArgumentException("Name must be 100 characters or less");
        if (dto.Notes?.Length > 500)
            throw new ArgumentException("Notes must be 500 characters or less");

        var eq = _store.ExecuteMutation(() =>
        {
            var created = new Equipment
            {
                Id = _store.NextEquipmentId(),
                Name = dto.Name.Trim(),
                Type = dto.Type,
                Notes = dto.Notes?.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                SyncId = Guid.NewGuid(),
                LastModifiedAt = DateTime.UtcNow,
            };
            _store.Equipment.Add(created);
            _store.SaveChanges();
            return created;
        });
        _notifier.NotifyDataChanged(DataChangeType.EquipmentCreated, eq);
        return Task.FromResult(MapToDto(eq));
    }

    public Task<EquipmentDto> UpdateEquipmentAsync(int id, UpdateEquipmentDto dto)
    {
        var eq = _store.ExecuteMutation(() =>
        {
            var existing = _store.Equipment.FirstOrDefault(e => e.Id == id && !e.IsDeleted)
                ?? throw new KeyNotFoundException($"Equipment {id} not found");
            if (dto.Name != null)
            {
                if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 100)
                    throw new ArgumentException("Name must be between 1 and 100 characters");
                existing.Name = dto.Name.Trim();
            }
            if (dto.Type.HasValue) existing.Type = dto.Type.Value;
            if (dto.Notes != null)
            {
                if (dto.Notes.Length > 500)
                    throw new ArgumentException("Notes must be 500 characters or less");
                existing.Notes = dto.Notes.Trim();
            }
            if (dto.IsActive.HasValue) existing.IsActive = dto.IsActive.Value;
            existing.LastModifiedAt = DateTime.UtcNow;
            _store.SaveChanges();
            return existing;
        });
        _notifier.NotifyDataChanged(DataChangeType.EquipmentUpdated, eq);
        return Task.FromResult(MapToDto(eq));
    }

    public Task ArchiveEquipmentAsync(int id)
    {
        var eq = _store.ExecuteMutation(() =>
        {
            var existing = _store.Equipment.FirstOrDefault(e => e.Id == id && !e.IsDeleted)
                ?? throw new KeyNotFoundException($"Equipment {id} not found");
            existing.IsActive = false;
            existing.LastModifiedAt = DateTime.UtcNow;
            _store.SaveChanges();
            return existing;
        });
        _notifier.NotifyDataChanged(DataChangeType.EquipmentUpdated, eq);
        return Task.CompletedTask;
    }

    public Task DeleteEquipmentAsync(int id)
    {
        _store.ExecuteMutation(() =>
        {
            var eq = _store.Equipment.FirstOrDefault(e => e.Id == id && !e.IsDeleted)
                ?? throw new KeyNotFoundException($"Equipment {id} not found");
            eq.IsDeleted = true;
            eq.LastModifiedAt = DateTime.UtcNow;
            _store.SaveChanges();
        });
        return Task.CompletedTask;
    }

    private static EquipmentDto MapToDto(Equipment e) => new()
    {
        Id = e.Id, Name = e.Name, Type = e.Type, Notes = e.Notes, IsActive = e.IsActive, CreatedAt = e.CreatedAt,
    };
}
