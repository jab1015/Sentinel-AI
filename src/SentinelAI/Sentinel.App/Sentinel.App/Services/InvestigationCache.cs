/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Concurrent;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Provides a thread-safe, time-bounded cache for expensive investigation evidence.
    /// The cache keeps slow collectors off repeated refresh paths while ensuring stale
    /// evidence is never treated as current indefinitely.
    /// </summary>
    public sealed class InvestigationCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _entries =
            new(StringComparer.OrdinalIgnoreCase);

        public bool TryGet<T>(string key, out T? value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            value = default;
            if (!_entries.TryGetValue(key, out CacheEntry? entry))
            {
                return false;
            }

            if (entry.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                _entries.TryRemove(key, out _);
                return false;
            }

            if (entry.Value is not T typedValue)
            {
                return false;
            }

            value = typedValue;
            return true;
        }

        public void Set<T>(string key, T value, TimeSpan lifetime)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);

            if (lifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lifetime),
                    lifetime,
                    "Cache lifetime must be greater than zero.");
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            _entries[key] = new CacheEntry(value, now, now.Add(lifetime));
        }

        public bool Remove(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            return _entries.TryRemove(key, out _);
        }

        public void Clear() => _entries.Clear();

        public int RemoveExpired()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int removed = 0;

            foreach ((string key, CacheEntry entry) in _entries)
            {
                if (entry.ExpiresAtUtc <= now && _entries.TryRemove(key, out _))
                {
                    removed++;
                }
            }

            return removed;
        }

        public CacheStatus GetStatus()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int active = 0;
            int expired = 0;

            foreach (CacheEntry entry in _entries.Values)
            {
                if (entry.ExpiresAtUtc > now)
                {
                    active++;
                }
                else
                {
                    expired++;
                }
            }

            return new CacheStatus(active, expired, _entries.Count);
        }

        private sealed record CacheEntry(
            object Value,
            DateTimeOffset CreatedAtUtc,
            DateTimeOffset ExpiresAtUtc);

        public sealed record CacheStatus(
            int ActiveEntryCount,
            int ExpiredEntryCount,
            int TotalEntryCount);
    }
}
