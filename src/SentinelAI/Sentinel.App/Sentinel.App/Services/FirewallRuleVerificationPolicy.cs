using System;
using System.Collections.Generic;
using System.Net;

namespace Sentinel.App.Services
{
    /// <summary>
    /// Pure decision boundary for interpreting the structured Windows Firewall rule
    /// evidence collected by FirewallContainmentService. A containment claim is valid
    /// only for one exact, enabled, outbound Block rule with the expected remote IP and
    /// otherwise unrestricted scope. Malformed query output fails closed.
    /// </summary>
    internal static class FirewallRuleVerificationPolicy
    {
        internal static FirewallRuleVerification Evaluate(
            IReadOnlyDictionary<string, string> values,
            string expectedRemoteIp)
        {
            ArgumentNullException.ThrowIfNull(values);

            if (!IPAddress.TryParse(expectedRemoteIp, out IPAddress? expectedAddress) || expectedAddress is null)
                return new(false, false, false, "expected remote IP is invalid");

            if (!values.TryGetValue("FOUND", out string? foundText) ||
                !int.TryParse(foundText, out int found) || found < 0)
            {
                return new(false, false, false, "firewall query output did not contain a valid FOUND count");
            }

            if (found == 0)
                return new(true, false, false, "rule not found");

            if (found != 1 || values.ContainsKey("CONFLICT"))
                return new(true, true, false, "more than one rule has the deterministic Sentinel name");

            bool enabled = IsExactly(values, "ENABLED", "True");
            bool block = IsExactly(values, "ACTION", "Block");
            bool outbound = IsOneOf(values, "DIRECTION", "Outbound", "Out");
            bool profileAny = IsExactly(values, "PROFILE", "Any");
            bool remoteExact = values.TryGetValue("REMOTE", out string? remote) &&
                               AddressListExactlyMatches(remote, expectedAddress);
            bool localAny = IsAny(values, "LOCAL");
            bool protocolAny = IsAny(values, "PROTOCOL");
            bool localPortAny = IsAny(values, "LOCALPORT");
            bool remotePortAny = IsAny(values, "REMOTEPORT");
            bool programAny = IsAny(values, "PROGRAM");
            bool serviceAny = IsAny(values, "SERVICE");

            bool exact = enabled && block && outbound && profileAny && remoteExact && localAny &&
                         protocolAny && localPortAny && remotePortAny && programAny && serviceAny;

            string detail = exact
                ? "exact enabled outbound Block rule verified"
                : $"Enabled={enabled}; ActionBlock={block}; Outbound={outbound}; ProfileAny={profileAny}; RemoteExact={remoteExact}; LocalAny={localAny}; ProgramAny={programAny}; ServiceAny={serviceAny}; ProtocolAny={protocolAny}; PortsAny={localPortAny && remotePortAny}";

            return new(true, true, exact, detail);
        }

        private static bool IsExactly(IReadOnlyDictionary<string, string> values, string key, string expected) =>
            values.TryGetValue(key, out string? value) &&
            value.Equals(expected, StringComparison.OrdinalIgnoreCase);

        private static bool IsOneOf(
            IReadOnlyDictionary<string, string> values,
            string key,
            params string[] expectedValues)
        {
            if (!values.TryGetValue(key, out string? value)) return false;
            foreach (string expected in expectedValues)
            {
                if (value.Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsAny(IReadOnlyDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out string? value) &&
            !string.IsNullOrWhiteSpace(value) &&
            (value.Equals("Any", StringComparison.OrdinalIgnoreCase) || value.Equals("*", StringComparison.OrdinalIgnoreCase));

        private static bool AddressListExactlyMatches(string value, IPAddress expected)
        {
            string[] items = (value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return items.Length == 1 &&
                   IPAddress.TryParse(items[0], out IPAddress? parsed) &&
                   parsed.Equals(expected);
        }
    }

    internal sealed record FirewallRuleVerification(
        bool QueryValid,
        bool Exists,
        bool IsExactBlock,
        string Detail);
}
