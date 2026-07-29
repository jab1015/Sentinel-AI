/*
 * Sentinel AI
 * Copyright (c) 2026 Modern Methods.
 */

using System;
using Sentinel.App.Models;

namespace Sentinel.App.Services
{
    public sealed class RiskAssessmentEngine
    {
        public RiskAssessment Assess(SystemSnapshot snapshot)
        {
            int score = 0;
            string recommendation = "No immediate action is required. Continue normal monitoring.";

            if (!snapshot.DefenderEnabled)
            {
                score += 40;
                recommendation = "Turn on Microsoft Defender or confirm that another trusted antivirus product is actively protecting this computer.";
            }

            if (!snapshot.FirewallEnabled)
            {
                score += 35;
                recommendation = "Turn on Windows Firewall for all network profiles unless another managed firewall is providing equivalent protection.";
            }

            score += Math.Min(snapshot.CriticalEventCount * 12, 24);
            score += Math.Min(snapshot.ErrorEventCount * 2, 16);

            if (snapshot.MemoryUsagePercent >= 90)
            {
                score += 10;
                recommendation = "Memory use is very high. Close unneeded applications and review the highest-memory process.";
            }
            else if (snapshot.MemoryUsagePercent >= 80)
            {
                score += 5;
                if (score < 20)
                {
                    recommendation = "Memory use is elevated. Review running applications if the computer feels slow.";
                }
            }

            if (snapshot.DiskUsagePercent >= 95)
            {
                score += 15;
                recommendation = "The system drive is nearly full. Free storage space to reduce reliability and update risks.";
            }
            else if (snapshot.DiskUsagePercent >= 85)
            {
                score += 8;
                if (score < 20)
                {
                    recommendation = "Available disk space is becoming limited. Consider removing temporary or unneeded files.";
                }
            }

            score = Math.Clamp(score, 0, 100);

            string level = score switch
            {
                >= 70 => "High",
                >= 35 => "Elevated",
                >= 15 => "Moderate",
                _ => "Low"
            };

            string summary = level switch
            {
                "High" => "Important security or reliability conditions need attention.",
                "Elevated" => "One or more conditions should be reviewed soon.",
                "Moderate" => "The computer is generally protected, with a few items worth reviewing.",
                _ => "Core protections are active and no major warning conditions were detected."
            };

            return new RiskAssessment(score, level, summary, recommendation);
        }

        public sealed record RiskAssessment(
            int Score,
            string Level,
            string Summary,
            string Recommendation);
    }
}
