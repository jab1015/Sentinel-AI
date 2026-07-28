# SAI-007 — UI/UX Design System

**Document ID:** SAI-007  
**Title:** UI/UX Design System  
**Version:** 1.0  
**Status:** Approved (Working Draft)  
**Project:** Sentinel AI

---

# Revision History

| Version | Date | Author | Description |
|---------|------|--------|-------------|
| 1.0 | 2026-07-28 | Sentinel AI Team | Initial Release |

---

# 1. Purpose

This document defines the visual language, interaction patterns, navigation structure, accessibility requirements, and user experience standards for Sentinel AI.

Every screen, control, animation, and workflow shall conform to this design system.

---

# 2. Design Philosophy

Sentinel AI is not designed to look like traditional antivirus software.

Instead, it should feel like a professional Security Operations Center (SOC) that remains approachable for everyday users.

The interface should communicate:

- Confidence
- Clarity
- Precision
- Transparency
- Calmness

Users should never feel overwhelmed.

---

# 3. Design Principles

## Explain, Don't Alarm

The interface should explain events rather than simply displaying warnings.

---

## Information Before Action

Always present evidence before recommending action.

---

## Progressive Disclosure

Simple information first.

Advanced technical details available on demand.

---

## Minimal Friction

Frequently used actions should require as few clicks as possible.

---

## Professional Appearance

Avoid unnecessary animations or distracting effects.

Every element should serve a purpose.

---

# 4. Navigation

Primary navigation shall appear on the left.

```
Dashboard

Activity

Processes

Network

Threats

Timeline

Reports

Settings

Help
```

Navigation should remain visible at all times.

---

# 5. Dashboard Layout

The dashboard shall contain:

Top Bar

- Current Status
- Security Score
- Search
- Notifications
- Settings

Main Cards

- Security Status
- CPU
- Memory
- Disk
- Network
- Firewall
- Defender
- Active Threats

Lower Section

- Live Timeline
- AI Recommendations
- Recent Alerts

---

# 6. Color Palette

## Safe

Green

Used for:

- Normal operation
- Successful actions
- Healthy status

---

## Information

Blue

Used for:

- General information
- Recommendations
- Neutral notifications

---

## Warning

Amber

Used for:

- Suspicious behavior
- Attention required

---

## Critical

Red

Used only for confirmed or highly probable threats.

---

## Background

Support both:

- Light Theme
- Dark Theme

Dark theme shall be the default.

---

# 7. Typography

Primary Font

Segoe UI Variable

Fallback

Segoe UI

Text hierarchy

- Page Title
- Section Header
- Card Title
- Body
- Caption

Text should remain readable at all supported DPI settings.

---

# 8. Iconography

Use Microsoft's Fluent System Icons.

Categories include:

- Shield
- Process
- Network
- Warning
- Success
- Information
- Settings
- AI
- Database
- Timeline

Avoid custom icons unless necessary.

---

# 9. Dashboard Cards

Each card shall include:

- Title
- Current value
- Status indicator
- Optional trend
- Optional details button

Cards should maintain a consistent size and spacing.

---

# 10. Timeline

The timeline displays:

Timestamp

↓

Event

↓

Severity

↓

Explanation

↓

Recommended Action

Timeline filters:

- Information
- Warning
- Critical
- Network
- Processes
- Security
- AI

---

# 11. Threat Details

Selecting a threat opens a detailed investigation panel.

Contents:

Overview

Evidence

Risk Score

Confidence

Timeline

Processes

Connections

AI Explanation

Recommended Actions

Raw Technical Details

---

# 12. Search

Global search shall locate:

- Events
- Processes
- Threats
- Connections
- Devices
- Reports

Search results should appear instantly as the user types.

---

# 13. Notifications

Notification levels

Information

Recommendation

Warning

Critical

Critical alerts remain visible until acknowledged.

---

# 14. Settings

Categories:

General

Appearance

Notifications

AI

Firewall

Database

Privacy

Updates

Advanced

---

# 15. Accessibility

Sentinel AI shall support:

Keyboard navigation

Screen readers

High contrast mode

Color-blind friendly indicators

Large text scaling

Accessible focus indicators

---

# 16. Responsive Behavior

The application shall support:

1080p

1440p

4K

Ultra-wide displays

Minimum supported resolution:

1280 × 720

---

# 17. Performance

Animations

Less than 200 milliseconds

Dashboard refresh

Less than 1 second

Window startup

Less than 5 seconds

Scrolling

60 FPS when practical

---

# 18. Future Interface

Future releases may include:

World map

Network graph

Threat heat map

Behavior graph

AI chat panel

Enterprise dashboard

Multiple computer management

---

# 19. User Experience Goals

A first-time user should understand:

Current security status

Recent activity

Any problems requiring attention

Recommended next steps

within thirty seconds of opening Sentinel AI.

---

# 20. Design Review Checklist

Every new screen shall be reviewed for:

Visual consistency

Accessibility

Performance

Readability

Keyboard usability

Dark mode compatibility

Responsive layout

Consistency with Fluent Design

---

# Conclusion

The Sentinel AI Design System establishes a consistent visual and interaction language that supports the product's mission of making advanced cybersecurity understandable, trustworthy, and approachable.

Every future interface shall conform to this design system.

---

# End of Document

**Document ID:** SAI-007  
**Version:** 1.0  
**Status:** Approved (Working Draft)