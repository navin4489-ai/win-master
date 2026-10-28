import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'core/config.dart';
import 'data/providers.dart';
import 'router.dart';

void main() {
  runApp(const ProviderScope(child: WinMasterApp()));
}

class WinMasterApp extends ConsumerStatefulWidget {
  const WinMasterApp({super.key});
  @override
  ConsumerState<WinMasterApp> createState() => _WinMasterAppState();
}

class _WinMasterAppState extends ConsumerState<WinMasterApp> {
  @override
  void initState() {
    super.initState();
    // Restore a persisted API base URL and attempt silent re-login.
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      final saved = await ref.read(tokenStoreProvider).readBaseUrl();
      if (saved != null && saved.isNotEmpty) AppConfig.baseUrl = saved;
      await ref.read(authProvider.notifier).bootstrap();
    });
  }

  @override
  Widget build(BuildContext context) {
    final router = ref.watch(routerProvider);
    return MaterialApp.router(
      title: 'Win Master',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.light(),
      routerConfig: router,
    );
  }
}
