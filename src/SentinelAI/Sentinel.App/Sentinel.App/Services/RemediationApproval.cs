/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Represents one explicit, short-lived user approval for one remediation action.
    /// Approval cannot be reused for another action or after it has been consumed.
    /// </summary>
    public sealed class RemediationApproval
    {
        private bool _consumed;

        public RemediationApproval(string scope, TimeSpan validFor)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new ArgumentException("Approval scope is required.", nameof(scope));
            }

            if (validFor <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(validFor));
            }

            Scope = scope;
            ApprovedAtUtc = DateTimeOffset.UtcNow;
            ExpiresAtUtc = ApprovedAtUtc.Add(validFor);
        }

        public string Scope { get; }

        public DateTimeOffset ApprovedAtUtc { get; }

        public DateTimeOffset ExpiresAtUtc { get; }

        public bool IsConsumed => _consumed;

        public bool IsValidFor(string requiredScope)
        {
            return !_consumed
                && DateTimeOffset.UtcNow <= ExpiresAtUtc
                && string.Equals(Scope, requiredScope, StringComparison.Ordinal);
        }

        public bool TryConsume(string requiredScope)
        {
            if (!IsValidFor(requiredScope))
            {
                return false;
            }

            _consumed = true;
            return true;
        }
    }
}
