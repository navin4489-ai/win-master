import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'data/providers.dart';
import 'data/models.dart';
import 'screens/splash_screen.dart';
import 'screens/login_screen.dart';
import 'screens/home_shell.dart';
import 'screens/campaigns_screen.dart';
import 'screens/campaign_create_screen.dart';
import 'screens/inbox_screen.dart';
import 'screens/accounts_screen.dart';
import 'screens/licenses_screen.dart';
import 'screens/settings_screen.dart';
import 'screens/group_detail_screen.dart';
import 'screens/templates_screen.dart';
import 'screens/proxies_screen.dart';
import 'screens/rules_screen.dart';
import 'screens/ai_screen.dart';
import 'screens/reports_screen.dart';
import 'screens/users_screen.dart';

/// Rebuilds the router's redirect logic whenever auth state changes.
class _AuthListenable extends ChangeNotifier {
  _AuthListenable(Ref ref) {
    ref.listen(authProvider, (_, _) => notifyListeners());
  }
}

GoRouter buildRouter(Ref ref) {
  final refresh = _AuthListenable(ref);
  return GoRouter(
    initialLocation: '/splash',
    refreshListenable: refresh,
    redirect: (context, state) {
      final auth = ref.read(authProvider);
      final loc = state.matchedLocation;
      if (auth.loading) return loc == '/splash' ? null : '/splash';
      final loggedIn = auth.isAuthenticated;
      if (!loggedIn) return loc == '/login' ? null : '/login';
      if (loc == '/login' || loc == '/splash') return '/';
      return null;
    },
    routes: [
      GoRoute(path: '/splash', builder: (_, _) => const SplashScreen()),
      GoRoute(path: '/login', builder: (_, _) => const LoginScreen()),
      GoRoute(path: '/', builder: (_, _) => const HomeShell()),
      GoRoute(path: '/campaigns/new', builder: (_, _) => const CampaignCreateScreen()),
      GoRoute(path: '/campaigns/:id', builder: (_, s) => CampaignDetailScreen(id: int.parse(s.pathParameters['id']!))),
      GoRoute(path: '/contacts/:id', builder: (_, s) => GroupDetailScreen(groupId: int.parse(s.pathParameters['id']!), groupName: (s.extra as String?) ?? 'Group')),
      GoRoute(path: '/inbox/:id', builder: (_, s) => ChatScreen(threadId: int.parse(s.pathParameters['id']!), thread: s.extra as InboxThread?)),
      GoRoute(path: '/accounts', builder: (_, _) => const AccountsScreen()),
      GoRoute(path: '/proxies', builder: (_, _) => const ProxiesScreen()),
      GoRoute(path: '/templates', builder: (_, _) => const TemplatesScreen()),
      GoRoute(path: '/rules', builder: (_, _) => const RulesScreen()),
      GoRoute(path: '/ai', builder: (_, _) => const AiScreen()),
      GoRoute(path: '/reports', builder: (_, _) => const ReportsScreen()),
      GoRoute(path: '/users', builder: (_, _) => const UsersScreen()),
      GoRoute(path: '/licenses', builder: (_, _) => const LicensesScreen()),
      GoRoute(path: '/settings', builder: (_, _) => const SettingsScreen()),
    ],
  );
}

final routerProvider = Provider<GoRouter>((ref) => buildRouter(ref));

