namespace Sentinel.App.Models
{
    public sealed class SystemSnapshot
    {
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string DefenderStatus { get; set; } = string.Empty;
        public string FirewallStatus { get; set; } = string.Empty;
        public string ProtectionHealthSummary { get; set; } = string.Empty;
        public bool NetworkConnectionMonitoringAvailable { get; set; }
        public bool EventLogMonitoringAvailable { get; set; }
        public bool AuthenticationMonitoringAvailable { get; set; }
        public bool ProcessMonitoringAvailable { get; set; }
        public bool CommandLineMonitoringAvailable { get; set; }
        public bool ProcessLineageMonitoringAvailable { get; set; }
        public bool ServiceMonitoringAvailable { get; set; }
        public bool StartupPersistenceMonitoringAvailable { get; set; }
        public bool ScheduledTaskMonitoringAvailable { get; set; }
        public bool CrashEvidenceAvailable { get; set; }
        public int ExternalConnectionCount { get; set; }
        public int InboundExternalConnectionCount { get; set; }
        public int OutboundExternalConnectionCount { get; set; }
        public int ListeningTcpEndpointCount { get; set; }
        public int AttributedExternalConnectionCount { get; set; }
        public string AuthenticationAnomalySummary { get; set; } = string.Empty;
        public string InvestigationReasonCode { get; set; } = string.Empty;
        public string InvestigationConclusion { get; set; } = string.Empty;
        public string InvestigationSummary { get; set; } = string.Empty;
        public string GuidanceEvidence { get; set; } = string.Empty;
        public int FlaggedProcessCount { get; set; }
        public string PrimaryFlaggedProcessName { get; set; } = string.Empty;
        public string PrimaryFlaggedProcessReason { get; set; } = string.Empty;
        public int FlaggedConnectionCount { get; set; }
        public string PrimaryFlaggedConnectionProcessName { get; set; } = string.Empty;
        public string PrimaryFlaggedConnectionRemoteEndpoint { get; set; } = string.Empty;
        public string PrimaryFlaggedConnectionReason { get; set; } = string.Empty;
        public int FlaggedServiceCount { get; set; }
        public string PrimaryFlaggedServiceName { get; set; } = string.Empty;
        public int FlaggedCommandLineCount { get; set; }
        public string PrimaryCommandLineProcessName { get; set; } = string.Empty;
        public string PrimaryCommandLineReason { get; set; } = string.Empty;
        public int FlaggedStartupEntryCount { get; set; }
        public string PrimaryFlaggedStartupEntryName { get; set; } = string.Empty;
        public string PrimaryFlaggedStartupEntryReason { get; set; } = string.Empty;
        public int FlaggedScheduledTaskCount { get; set; }
        public string PrimaryFlaggedScheduledTaskName { get; set; } = string.Empty;
        public string PrimaryFlaggedScheduledTaskReason { get; set; } = string.Empty;
        public string RecentCrashSummary { get; set; } = string.Empty;
    }
}

namespace Sentinel.App.Services
{
    public sealed record ExternalInvestigationSource(string SourceName, bool Reached);

    public sealed class ExternalInvestigationResult
    {
        public string Topic { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public IReadOnlyList<string> MatchedTerms { get; set; } = Array.Empty<string>();
        public IReadOnlyList<ExternalInvestigationSource> Sources { get; set; } = Array.Empty<ExternalInvestigationSource>();
    }
}
