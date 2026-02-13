using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Zerbitzaria.Dtos;

namespace Zerbitzaria.Services
{
    // High-performance in-memory market cache using ConcurrentDictionary and snapshot semantics.
    public sealed class MarketCache
    {
        private readonly ConcurrentDictionary<string, MarketDto> _map = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastUpdated = DateTime.MinValue;

        public bool HasData => !_map.IsEmpty;

        // Returns a snapshot list of current markets
        public IReadOnlyList<MarketDto> GetAll()
        {
            var arr = new List<MarketDto>(_map.Count);
            foreach (var kv in _map)
            {
                arr.Add(kv.Value);
            }
            return arr;
        }

        // Replace all entries atomically (best-effort) - used when updating from external feed
        public void SetAll(IEnumerable<MarketDto> items)
        {
            if (items == null) return;
            _map.Clear();
            foreach (var it in items)
            {
                _map[it.Symbol] = it;
            }
            _lastUpdated = DateTime.UtcNow;
        }

        // Update or add a single market entry
        public void Upsert(MarketDto dto)
        {
            if (dto == null) return;
            _map.AddOrUpdate(dto.Symbol, dto, (k, v) => dto);
            _lastUpdated = DateTime.UtcNow;
        }

        public DateTime LastUpdatedUtc => _lastUpdated;
    }
}
