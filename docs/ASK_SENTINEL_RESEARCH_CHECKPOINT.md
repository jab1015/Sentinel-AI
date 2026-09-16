# Ask Sentinel Research and Conversation Checkpoint

Last updated: 2026-09-15
Branch: `feature/premium-privacy-foundation`
Qualified source checkpoint: `6fcdfaf068f054a8b9d02b6c7bae2abde5b81eaa`

## Scope

This checkpoint records the current Ask Sentinel reasoning, conversation, and external-research architecture. It distinguishes source/CI qualification from deployment and installed-runtime validation. A green repository build does not prove that the production Google Cloud Run gateway is running the same image.

## Question routing

Ask Sentinel routes by intent instead of exact phrases:

- clearly local computer-state questions use fresh verified local evidence when that evidence is sufficient;
- unresolved, explanatory, comparison, and natural-fragment questions default to Basic AI;
- explicit current/latest/web/official-source requests route to external research;
- local freshness such as CPU usage "right now" remains local rather than being mistaken for internet freshness;
- the first unresolved AI pass remains Basic/Economy; Advanced is reserved for the paid external-investigation path.

The routing acceptance harness covers definitions, terse local topics, arbitrary fragments, ambiguous keywords, local freshness, explicit research wording, tiering, and gateway token ceilings.

## Conversational continuity

Typed referential follow-ups such as `Why?`, `What do you mean?`, `What about Windows 11?`, and `Would you recommend that?` can use the immediately preceding validated Ask Sentinel exchange.

Conversation memory is deliberately bounded:

- process-local and non-persistent;
- maximum age of 30 minutes;
- only a bounded previous question/answer is retained;
- only an answer that crossed the final display-safety boundary is remembered;
- prior conversation is labeled advisory context, not machine evidence;
- fresh local-state questions ignore prior conversational wording;
- every claim about this computer must still be checked against the newly refreshed local evidence package.

The conversation context stays below the evidence-line ceiling so its anti-staleness and non-evidence rules cannot be truncated from the cloud package.

## Microsoft research

Paid external investigation first uses the read-only Microsoft Learn MCP endpoint for semantic documentation search. Returned documents are bounded and accepted only from `https://learn.microsoft.com`.

Pinned Microsoft pages remain a bounded fallback when Learn search is unavailable or returns no useful result. External documentation is guidance only and is never treated as proof of the current PC state or proof that Sentinel performed an action.

## Paid cited web research

Advanced AI can use provider web search only when the request purpose is exactly `external-investigation`.

The server gateway enforces this boundary and the Windows client independently re-enforces it before accepting web-derived metadata. Basic AI cannot accept web-search results, and Advanced AI used for any other purpose cannot accept them either.

Provider requests:

- set `store: false`;
- allow a maximum of two web-search tool calls;
- request attributable web-search source metadata;
- instruct the model to prefer Microsoft and first-party vendor sources when available;
- require current/vendor/version/security-advisory claims to use supplied research rather than pretending model knowledge is current.

Provider responses are bounded and parsed defensively:

- source/citation URLs must be HTTPS and contain no userinfo;
- citation/source counts and URL/title sizes are capped;
- invalid citation ranges are discarded;
- a web-derived answer with no usable attributable HTTPS source fails closed;
- provider citation offsets are normalized with answer whitespace so links remain attached to the intended text;
- web sources remain external research, not verified local machine/action evidence.

## User-visible citations

Ask Sentinel can render valid cited answer spans as clickable hyperlinks and adds a bounded clickable `Sources` section. Microsoft Learn/pinned research and provider web sources are merged and deduplicated for display.

If final display-safety validation replaces an answer, the associated citations and sources are discarded as well. Sentinel never displays citations as if they support replacement text they were not attached to.

## Trust boundaries retained

The research/conversation work does not change these rules:

- AI output is advisory;
- external research never proves this computer's state;
- AI cannot authorize a repair or establish repair success;
- actual privileged/remediation paths retain their own verification and user-approval boundaries;
- cloud evidence continues through the identifier/credential/IP/MAC/user-path redaction boundary;
- Basic sessions cannot request Advanced reasoning;
- paid Advanced sessions still require the server-side Microsoft Store entitlement path;
- every composed Ask Sentinel answer still crosses the final display-safety validator before presentation.

## Qualified commits

- `0332ba47ffcf95afee8832191f0670f337750b6b` — bounded typed conversation context checkpoint.
- `827285a77ef8e09449e4d87aa583a22c88835525` — attributable paid web research implementation.
- `7439ee305d1004209785bf10a464d0ca607682a6` — preserved explicit gateway token-budget source acceptance boundary.
- `ff7745eb1264976fc01b8dbd87b41c30554db357` — provider citation-offset normalization and regression.
- `6fcdfaf068f054a8b9d02b6c7bae2abde5b81eaa` — independent desktop-side web-research authorization boundary.

## CI evidence

Premium Privacy Foundation run `35041349167` on `6fcdfaf068f054a8b9d02b6c7bae2abde5b81eaa` passed the source/security/routing, native Explorer, desktop, gateway, unsigned x64 MSIX, and packaged Explorer architecture gates.

Earlier exact-head web-research run `35040704555` on `ff7745eb1264976fc01b8dbd87b41c30554db357` also completed successfully end-to-end, including the citation-offset regression.

The dedicated signed LocalDev Windows VM-package workflow remains the separate packaging/installation qualification lane. Its status must be checked independently and must not be inferred from the unsigned package workflow.

## Remaining deployment/runtime boundary

The repository contains `src/SentinelAI/Sentinel.AiGateway/Dockerfile` and the Windows app targets the existing production endpoint:

`https://sentinel-ai-gateway-49908265995.us-central1.run.app/`

No repository-controlled `gcloud`, Cloud Build, or Cloud Run deployment workflow was found at this checkpoint, and the repository does not contain the Google Cloud project/service-account deployment identity needed to deploy safely from GitHub.

Therefore:

- the new gateway source is CI-qualified;
- the production Cloud Run service has **not** been proven to be running this new image;
- live web-search behavior must not be claimed until the gateway is deployed and runtime-tested;
- deployment should use existing Google Cloud ownership/credentials rather than inventing new secrets or weakening authentication;
- staging/live validation should verify health, free-session behavior, paid entitlement, web-search attribution, `store: false` request behavior where observable, provider timeout/failure handling, rate/concurrency controls, and source/citation rendering through the installed Windows application.

## Current release statement

Ask Sentinel's conversation and research implementation is source/CI-qualified on the checkpoint above. Production deployment and installed Windows/runtime validation remain required before describing the new web-research capability as production-live.
