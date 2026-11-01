# Campaign Reliability, Admin Reporting, AI Templates and Media Plan

## Scope

Implement five additive improvements without changing existing WhatsApp campaign routes or
the existing direct-send flow.

## 1. Connection-aware campaign lifecycle

### Behavior

1. Before `Start` and `Restart`, verify every selected Baileys account through the bridge live
   status endpoint. If no account is connected, block the action with a QR-scan message.
2. Before every outbound send, verify the live session again.
3. If the bridge/session drops during a running campaign:
   - mark the campaign `Paused` with `PausedByConnection=true`;
   - do not mark the current recipient as successfully sent;
   - ask the bridge to reconnect / produce a QR;
   - keep retrying on the worker interval;
   - automatically resume only after the bridge reports `connected`.
4. A user pause remains a normal manual pause and is never automatically resumed.
5. On reconnect, the current pending recipient is sent once; already successful recipients are
   never duplicated.

### Data/API changes

- `Campaign.PausedByConnection` and `Campaign.ConnectionMessage`.
- `POST /api/campaigns/{id}/retry-failed` for failed-only recovery.
- Existing `start`, `pause`, `resume`, `stop`, `restart` routes remain compatible.

## 2. Admin user-wise reporting

Add an Admin/SuperAdmin-only report endpoint:

`GET /api/reports/users?from=&to=` → user aggregates:

- campaigns created/running/completed
- recipients processed/sent/delivered/read/failed
- delivery rate
- converted leads and conversion value

Web Reports receives a **User activity** table. Existing user activity/audit endpoints remain
available. Message bodies and full phone numbers are never included in the report.

## 3. AI template generation

- Add `POST /api/templates/generate` using the existing free/local AI client and hosted free
  providers.
- Input: topic/user brief, tone, goal, include variables, include spintax.
- Output: editable message text; the user explicitly saves it as a template.
- Template builder gets an **AI generate** button. Campaign builder already has AI generation
  and remains unchanged.
- The saved template is sent through the normal campaign pipeline, preserving personalization,
  spintax, media and interactive elements.

## 4. Template media delivery

Current campaign worker was passing `null` media even when a template had media. Fix it to pass:

- template `MediaUrl`
- template `MediaType`
- serialized buttons
- serialized list menu

Baileys sends image/document/video/audio via the bridge. Meta Cloud API sends supported media
types through Graph API. Media upload validation remains unchanged.

## 5. Retry failed only

- Add `POST /api/campaigns/{id}/retry-failed`.
- Reset only `Failed` recipients to `Pending`; clear only their attempt fields; preserve Sent,
  Delivered and Read rows.
- Clear/recreate checkpoint safely and run the campaign.
- Add Restart/Retry failed controls to web and mobile campaign detail/list actions.

## Quality gates

- Existing backend suite remains green; add tests for pause-on-disconnect, reconnect-resume,
  failed-only retry, media propagation, AI template generation and user report authorization.
- Angular production build and Flutter analyze/test remain green.
- Verify with a mock bridge: connected → send, disconnected → paused, connected → resumed,
  and failed-only retry sends only failed phone numbers.
