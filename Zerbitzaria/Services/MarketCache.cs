using System;
using System.Collections.Generic;
using System.Threading;

namespace Zerbitzaria.Services
{
    // Lightweight thread-safe in-memory cache for market data
    public sealed class MarketCache
    {
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
        private List<object> _items = new List<object>();
        private DateTime _lastUpdated = DateTime.MinValue;

        public bool HasData
        {
            get
            {
                _lock.EnterReadLock();
                try { return _items != null && _items.Count > 0; }
                finally { _lock.ExitReadLock(); }
            }
        }

        public IReadOnlyList<object> GetAll()
        {
            _lock.EnterReadLock();
            try { return _items.ToArray(); }
            finally { _lock.ExitReadLock(); }
        }

        public void SetAll(IEnumerable<object> items)
        {
            _lock.EnterWriteLock();
            try
            {
                _items = new List<object>(items ?? Array.Empty<object>());
                _lastUpdated = DateTime.UtcNow;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public DateTime LastUpdatedUtc => _lastUpdated;
    }
}
