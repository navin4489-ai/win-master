import 'dart:convert';
import 'package:http/http.dart' as http;
import '../core/config.dart';
import 'token_store.dart';

class ApiException implements Exception {
  final int statusCode;
  final String message;
  ApiException(this.statusCode, this.message);
  @override
  String toString() => message;
}

/// Thin HTTP client with bearer auth + one-shot refresh on 401.
class ApiClient {
  final TokenStore tokens;
  final http.Client _http;
  ApiClient(this.tokens, [http.Client? client]) : _http = client ?? http.Client();

  Uri _uri(String path, [Map<String, dynamic>? query]) {
    final q = <String, String>{};
    query?.forEach((k, v) {
      if (v != null && '$v'.isNotEmpty) q[k] = '$v';
    });
    return Uri.parse('${AppConfig.baseUrl}$path').replace(queryParameters: q.isEmpty ? null : q);
  }

  Map<String, String> get _headers => {
        'Content-Type': 'application/json',
        if (tokens.accessToken != null) 'Authorization': 'Bearer ${tokens.accessToken}',
      };

  Future<dynamic> get(String path, {Map<String, dynamic>? query}) =>
      _send(() => _http.get(_uri(path, query), headers: _headers));
  Future<dynamic> post(String path, {Object? body}) =>
      _send(() => _http.post(_uri(path), headers: _headers, body: jsonEncode(body ?? {})));
  Future<dynamic> put(String path, {Object? body}) =>
      _send(() => _http.put(_uri(path), headers: _headers, body: jsonEncode(body ?? {})));
  Future<dynamic> delete(String path) => _send(() => _http.delete(_uri(path), headers: _headers));

  Future<dynamic> _send(Future<http.Response> Function() run) async {
    var res = await run();
    if (res.statusCode == 401 && await _tryRefresh()) {
      res = await run();
    }
    return _parse(res);
  }

  bool _refreshing = false;
  Future<bool> _tryRefresh() async {
    if (_refreshing) return false;
    _refreshing = true;
    try {
      final rt = await tokens.readRefresh();
      if (rt == null) return false;
      final res = await _http.post(_uri('/api/auth/refresh'),
          headers: {'Content-Type': 'application/json'}, body: jsonEncode({'refreshToken': rt}));
      if (res.statusCode == 200) {
        final j = jsonDecode(res.body);
        tokens.accessToken = j['accessToken'];
        await tokens.writeRefresh(j['refreshToken']);
        return true;
      }
      return false;
    } catch (_) {
      return false;
    } finally {
      _refreshing = false;
    }
  }

  dynamic _parse(http.Response res) {
    if (res.statusCode >= 200 && res.statusCode < 300) {
      if (res.body.isEmpty) return null;
      return jsonDecode(res.body);
    }
    String msg = 'Request failed (${res.statusCode})';
    try {
      final j = jsonDecode(res.body);
      msg = j['detail'] ?? j['title'] ?? msg;
    } catch (_) {}
    throw ApiException(res.statusCode, msg);
  }
}
