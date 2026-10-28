import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'api_client.dart';
import 'token_store.dart';
import 'models.dart';

final tokenStoreProvider = Provider<TokenStore>((ref) => TokenStore());
final apiProvider = Provider<ApiClient>((ref) => ApiClient(ref.read(tokenStoreProvider)));

/// Authentication state + actions.
class AuthState {
  final User? user;
  final bool loading;
  final String? error;
  const AuthState({this.user, this.loading = false, this.error});
  AuthState copyWith({User? user, bool? loading, String? error, bool clearUser = false}) =>
      AuthState(user: clearUser ? null : (user ?? this.user), loading: loading ?? this.loading, error: error);
  bool get isAuthenticated => user != null;
}

class AuthController extends StateNotifier<AuthState> {
  final Ref ref;
  AuthController(this.ref) : super(const AuthState());

  ApiClient get _api => ref.read(apiProvider);
  TokenStore get _tokens => ref.read(tokenStoreProvider);

  /// Tries to restore a session from the stored refresh token on startup.
  Future<void> bootstrap() async {
    state = state.copyWith(loading: true);
    // Show the branded splash for a minimum moment so it doesn't flash by.
    final minSplash = Future<void>.delayed(const Duration(milliseconds: 1600));
    try {
      final rt = await _tokens.readRefresh();
      if (rt == null) {
        await minSplash;
        state = const AuthState();
        return;
      }
      final j = await _api.post('/api/auth/refresh', body: {'refreshToken': rt});
      final auth = AuthResult.fromJson(j);
      _tokens.accessToken = auth.accessToken;
      await _tokens.writeRefresh(auth.refreshToken);
      await minSplash;
      state = AuthState(user: auth.user);
    } catch (_) {
      await _tokens.clear();
      await minSplash;
      state = const AuthState();
    }
  }

  Future<bool> login(String email, String password) async {
    state = state.copyWith(loading: true, error: null);
    try {
      final j = await _api.post('/api/auth/login', body: {'email': email, 'password': password});
      final auth = AuthResult.fromJson(j);
      _tokens.accessToken = auth.accessToken;
      await _tokens.writeRefresh(auth.refreshToken);
      state = AuthState(user: auth.user);
      return true;
    } on ApiException catch (e) {
      state = AuthState(error: e.message);
      return false;
    } catch (_) {
      state = const AuthState(error: 'Could not reach the server.');
      return false;
    }
  }

  Future<void> logout() async {
    final rt = await _tokens.readRefresh();
    if (rt != null) {
      try {
        await _api.post('/api/auth/logout', body: {'refreshToken': rt});
      } catch (_) {}
    }
    await _tokens.clear();
    state = const AuthState();
  }

  bool atLeast(String min) {
    const order = ['User', 'Reseller', 'Admin', 'SuperAdmin'];
    final r = state.user?.role;
    return r != null && order.indexOf(r) >= order.indexOf(min);
  }
}

final authProvider = StateNotifierProvider<AuthController, AuthState>((ref) => AuthController(ref));
