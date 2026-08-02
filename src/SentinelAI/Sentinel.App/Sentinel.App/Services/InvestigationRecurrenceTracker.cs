/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Tracks repeated investigation reason codes during the current Sentinel
    /// session so transient conditions can remain quiet while recurring findings
    /// can be escalated deliberately.
    /// </summary>
    public sealed class InvestigationRecurrenceTracker
    {
        private static readonly TimeSpan RecurrenceWindow = TimeSpan.FromHours(24);
        private static readonly TimeSpan MinimumDistinctObservationInterval = TimeSpan.FromMinutes(5);
        private readonly object _sync = new();
        private readonly Dictionary<string, RecurrenceState> _states = new(StringComparer.OrdinalIgnoreCase);

        public RecurrenceResult Record(string? reasonCode, bool requiresAttention, DateTimeOffset timestamp)
        {
            if (!requiresAttention || string.IsNullOrWhiteSpace(reasonCode) ||
                string.Equals(reasonCode, "healthy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reasonCode, "initializing", StringComparison.OrdinalIgnoreCase))
            {
                return new RecurrenceResult(0, false, false);
            }

            lock (_sync)
            {
                if (!_states.TryGetValue(reasonCode, out RecurrenceState? state) ||
                    timestamp - state.FirstSeen > RecurrenceWindow)
                {
                    state = new RecurrenceState(timestamp, timestamp, 1);
                    _states[reasonCode] = state;
                    return new RecurrenceResult(1, false, false);
                }

                // Monitoring refreshes frequently. Re-reading the same live finding
                // must not be counted as a new occurrence on every refresh; otherwise
                // one condition could falsely become "recurring" within seconds.
                if (timestamp - state.LastSeen < MinimumDistinctObservationInterval)
                {
                    return new RecurrenceResult(
                        state.Count,
                        IsRecurring: state.Count >= 2,
                        ShouldEscalate: state.Count >= 3);
                }

                state = state with { LastSeen = timestamp, Count = state.Count + 1 };
                _states[reasonCode] = state;

                // Two distinct observations establish recurrence; three or more
                // justify stronger escalation. The caller still decides what action
                // is safe and whether the evidence is actionable.
                return new RecurrenceResult(
                    state.Count,
                    IsRecurring: state.Count >= 2,
                    ShouldEscalate: state.Count >= 3);
            }
        }

        public void Clear(string? reasonCode)
        {
            if (string.IsNullOrWhiteSpace(reasonCode))
            {
                return;
            }

            lock (_sync)
            {
                _states.Remove(reasonCode);
            }
        }

        public sealed record RecurrenceResult(
            int Count,
            bool IsRecurring,
            bool ShouldEscalate);

        private sealed record RecurrenceState(
            DateTimeOffset FirstSeen,
            DateTimeOffset LastSeen,
            int Count);
    }
}
