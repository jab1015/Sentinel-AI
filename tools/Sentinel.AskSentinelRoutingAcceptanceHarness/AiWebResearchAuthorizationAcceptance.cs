using Sentinel.App.Services;
using System.Runtime.CompilerServices;

internal static class AiWebResearchAuthorizationAcceptance
{
    [ModuleInitializer]
    internal static void Verify()
    {
        if (AiWebResearchAuthorizationPolicy.IsAuthorized(
                advanced: false,
                purpose: "external-investigation"))
        {
            throw new InvalidOperationException(
                "Basic AI was incorrectly authorized to accept web-derived research metadata.");
        }

        if (AiWebResearchAuthorizationPolicy.IsAuthorized(
                advanced: true,
                purpose: "ask-sentinel-basic"))
        {
            throw new InvalidOperationException(
                "Advanced AI outside the external-investigation pass was incorrectly authorized to accept web-derived research metadata.");
        }

        if (!AiWebResearchAuthorizationPolicy.IsAuthorized(
                advanced: true,
                purpose: "external-investigation"))
        {
            throw new InvalidOperationException(
                "Advanced external-investigation was not authorized to accept attributable web research.");
        }

        if (!AiWebResearchAuthorizationPolicy.IsAuthorized(
                advanced: true,
                purpose: "  EXTERNAL-INVESTIGATION  "))
        {
            throw new InvalidOperationException(
                "The desktop web-research boundary did not normalize the intended purpose safely.");
        }
    }
}
