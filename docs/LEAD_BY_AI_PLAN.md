# Lead by AI — Contact Group Lead Discovery Plan

## Goal

Allow a user to open a contact group, enter:

- Indian location PIN code
- Number of leads requested
- Business type/category
- Optional search keyword

The system then returns real, reviewable business candidates with business/owner name,
phone, address and source. The user reviews the table and explicitly chooses **Add selected
contacts**. No record is inserted automatically.

## Safety and data-quality rules

1. AI is used to normalize the request, generate search terms and score/rank candidates.
2. AI must never invent phone numbers, names or businesses.
3. Real contact discovery comes from a configured compliant provider (Google Places-compatible
   provider in phase 1). If the provider is not configured or returns no data, the API returns
   an actionable configuration/error state rather than fake contacts.
4. Only public business contact data is imported; private/personal data is not scraped.
5. Every result includes `source`, `sourceUrl`, `provider`, and `confidence` for review.
6. India is the default country. Phone numbers are normalized to `+91XXXXXXXXXX` when a
   ten-digit Indian number is returned. Existing international numbers remain unchanged.
7. Existing contact import, manual add, campaigns, WhatsApp send, and lead analytics are
   unchanged.

## Backend implementation

### New contract

`POST /api/contact-groups/{groupId}/lead-search`

Request:

```json
{
  "pincode": "400001",
  "leadCount": 25,
  "businessType": "Dentist",
  "keyword": "family dental clinic",
  "countryCode": "+91"
}
```

Response:

```json
{
  "provider": "GooglePlaces",
  "query": "family dental clinic near 400001, India",
  "results": [
    {
      "externalId": "...",
      "businessName": "Example Dental Clinic",
      "ownerName": null,
      "phone": "+912212345678",
      "address": "...",
      "source": "Google Places",
      "sourceUrl": "...",
      "confidence": 0.88,
      "selected": true
    }
  ],
  "message": null
}
```

`POST /api/contact-groups/{groupId}/lead-search/import`

Request `{ "leads": [LeadSearchResultDto...] }`. The service revalidates, deduplicates by
normalized phone, and returns `{ imported, duplicates, invalid, errors[] }`.

### New configuration

`LeadDiscovery:Provider=GooglePlaces` and `LeadDiscovery:ApiKey` (or environment variable
`LeadDiscovery__ApiKey`). The key is never returned to the client.

### Service layers

- `LeadDiscoveryService`: validates request, normalizes India phone numbers, calls provider,
  maps only returned public data, and imports selected rows.
- `GooglePlacesLeadProvider`: Text Search + Place Details, using a bounded timeout and
  retry policy. Phone number comes only from Place Details.
- `AiLeadPlanner`: uses existing `IAiClient` to normalize business type/query; falls back
  to deterministic normalization if the selected AI provider is local.

## Web implementation

Contact group detail receives a **Lead by AI** button. The modal contains:

- PIN code input (required)
- Lead count (1–100)
- Business type dropdown with common Indian business categories plus Other
- Optional keyword
- Country code default `+91`

Results render in a review table with select checkboxes, business name, owner if supplied,
phone, address, source and confidence. **Add selected contacts** imports only checked rows.

## Acceptance criteria

- Existing WhatsApp tests and APIs remain green.
- Missing provider key produces a clear setup message, never fake contacts.
- A configured provider returns real results and allows selective import.
- Imported Indian phones are stored as `+91` numbers and duplicates are not created.
- Web production build succeeds.
