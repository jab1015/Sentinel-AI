/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using System.Collections.Generic;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Records remediation decisions and outcomes so Sentinel can explain what it
    /// considered, what it changed, and whether verification succeeded.
    /// </summary>
    public sealed class RemediationAuditTrail
    {
        private const int MaximumEntries = 100;
        private readonly object _sync = new();
        private readonly Queue<RemediationAuditEntry> _entries = new();

        public void Record(
            string action,
            string target,
            RemediationAuditOutcome outcome,
            string summary,
            bool requiredUserApproval)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(action);
            ArgumentException.ThrowIfNullOrWhiteSpace(summary);

            RemediationAuditEntry entry = new(
                DateTimeOffset.Now,
                action,
                string.IsNullOrWhiteSpace(target) ? "None" : target,
                outcome,
                summary,
                requiredUserApproval);

            lock (_sync)
            {
                _entries.Enqueue(entry);
                while (_entries.Count > MaximumEntries)
                {
                    _entries.Dequeue();
                }
            }
        }

        public IReadOnlyList<RemediationAuditEntry> GetEntries()
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }

        public RemediationAuditEntry? GetLatest()
        {
            lock (_sync)
            {
                RemediationAuditEntry? latest = null;
                foreach (RemediationAuditEntry entry in _entries)
                {
                    latest = entry;
                }

                return latest;
            }
        }

        public sealed record RemediationAuditEntry(
            DateTimeOffset Timestamp,
            string Action,
            string Target,
            RemediationAuditOutcome Outcome,
            string Summary,
            bool RequiredUserApproval);

        public enum RemediationAuditOutcome
        {
            Recommended,
            AwaitingApproval,
            AutomaticallyApproved,
            Started,
            Succeeded,
            Failed,
            Denied
        }
    }
}
