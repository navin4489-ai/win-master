import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Securely stores the refresh token; access token is kept in memory only.
class TokenStore {
  static const _storage = FlutterSecureStorage();
  static const _kRefresh = 'wm_refresh';
  static const _kBaseUrl = 'wm_base_url';

  String? accessToken;

  Future<String?> readRefresh() => _storage.read(key: _kRefresh);
  Future<void> writeRefresh(String token) => _storage.write(key: _kRefresh, value: token);
  Future<void> clear() async {
    accessToken = null;
    await _storage.delete(key: _kRefresh);
  }

  Future<String?> readBaseUrl() => _storage.read(key: _kBaseUrl);
  Future<void> writeBaseUrl(String url) => _storage.write(key: _kBaseUrl, value: url);
}
