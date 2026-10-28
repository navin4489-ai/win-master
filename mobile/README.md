# Win Master — Flutter Companion App

Mobile companion for the Win Master WhatsApp marketing platform (Path A — dashboard/companion app).

## Features
- **Auth** — email/password login, secure refresh-token storage, silent re-login, auto-refresh on 401.
- **Dashboard** — live KPI cards (campaigns, messages, accounts, contacts, licenses, inbox).
- **Campaigns** — list + detail with **live progress polling** and start/pause/resume/stop.
- **Inbox** — thread list + two-way chat with delivery status and a bot on/off toggle.
- **Contacts** — groups with counts, validity and tags.
- **Licenses** — list, generate (role-aware), revoke (Reseller+).
- **Accounts** — WhatsApp account status, warm-up and daily-send usage.
- **Settings** — profile, configurable API base URL, push opt-in, logout.

## Tech
Flutter 3.47 · Riverpod · go_router · http · flutter_secure_storage.
State in `lib/data/providers.dart`, HTTP in `lib/data/api_client.dart`, routing in `lib/router.dart`.
Push notifications are abstracted behind `PushService` (`NoOpPushService` by default; Firebase plugs in later).

## Configure
The API base URL defaults to the deployed Render service and can be overridden:

```bash
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5188   # local API on Android emulator
```

Or change it in-app via **Settings → API connection**.

## Run / verify
```bash
flutter pub get
flutter analyze        # clean
flutter test           # model + parsing tests
flutter run            # device/emulator
flutter build apk      # Android
flutter build web      # web
```

**Demo logins** (password `Win@12345`): `admin@winmaster.io` · `reseller@winmaster.io` · `user@winmaster.io`
