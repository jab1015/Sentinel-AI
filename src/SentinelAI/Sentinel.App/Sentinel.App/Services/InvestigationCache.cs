/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Concurrent;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Provides a thread-safe in-memory cache for expensive investigation data.
    /// Cached evidence is short-lived so Sentinel can reduce repeated Windows
    /// queries without reporting stale security conclusions.
    /// </summary>
    public sealed class InvestigationCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _entries =
            new(StringComparer.Ordinal);

        public bool TryGet<T>(string key, out T? value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            value = default;
            if (!_entries.TryGetValue(key, out CacheEntry? entry))
            {
                return false;
            }

            if (entry.ExpiresAtUtc <= DateTime.UtcNow)
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

        public void Set<T>(string key, T value, TimeSpan duration)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(value);

            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(duration),
                    duration,
                    "Cache duration must be greater than zero.");
            }

            _entries[key] = new CacheEntry(value, DateTime.UtcNow.Add(duration));
        }

        public void Remove(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            _entries.TryRemove(key, out _);
        }

        public void Clear() => _entries.Clear();

        public int RemoveExpired()
        {
            int removed = 0;
            DateTime now = DateTime.UtcNow;

            foreach ((string key, CacheEntry entry) in _entries)
            {
                if (entry.ExpiresAtUtc <= now && _entries.TryRemove(key, out _))
                {
                    removed++;
                }
            }

            return removed;
        }

        private sealed record CacheEntry(object Value, DateTime ExpiresAtUtc);
    }
}
