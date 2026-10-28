import 'api_client.dart';

/// Abstraction for push notifications. A real Firebase Cloud Messaging implementation
/// plugs in here; [NoOpPushService] keeps the app buildable without Firebase config.
abstract class PushService {
  Future<void> init();
  Future<void> registerToken(ApiClient api);
}

class NoOpPushService implements PushService {
  @override
  Future<void> init() async {}

  @override
  Future<void> registerToken(ApiClient api) async {
    // When Firebase is configured, obtain the FCM token and POST it:
    // await api.post('/api/push-tokens', body: {'platform': 'Android', 'token': token});
  }
}
