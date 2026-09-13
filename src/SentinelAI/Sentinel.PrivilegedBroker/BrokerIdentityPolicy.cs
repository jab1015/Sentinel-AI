internal static class BrokerIdentityPolicy
{
    internal static bool SamePackage(string? brokerPackageFullName, string? clientPackageFullName) =>
        !string.IsNullOrWhiteSpace(brokerPackageFullName) &&
        !string.IsNullOrWhiteSpace(clientPackageFullName) &&
        string.Equals(brokerPackageFullName, clientPackageFullName, StringComparison.OrdinalIgnoreCase);
}
