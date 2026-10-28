import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../core/config.dart';
import '../data/providers.dart';
import '../widgets/ui.dart';

class SettingsScreen extends ConsumerStatefulWidget {
  const SettingsScreen({super.key});
  @override
  ConsumerState<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends ConsumerState<SettingsScreen> {
  late final TextEditingController _url = TextEditingController(text: AppConfig.baseUrl);
  bool _fcm = false;

  @override
  void dispose() {
    _url.dispose();
    super.dispose();
  }

  Future<void> _saveUrl() async {
    AppConfig.baseUrl = _url.text.trim();
    await ref.read(tokenStoreProvider).writeBaseUrl(AppConfig.baseUrl);
    if (mounted) showOk(context, 'API URL saved — sign in again to apply.');
  }

  @override
  Widget build(BuildContext context) {
    final user = ref.watch(authProvider).user;
    return Scaffold(
      appBar: AppBar(title: const Text('Settings')),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Row(children: [
              CircleAvatar(
                radius: 26,
                backgroundColor: AppTheme.primary,
                child: Text((user?.displayName ?? '?').characters.first.toUpperCase(),
                    style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(user?.displayName ?? '', style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
                  Text(user?.email ?? '', style: const TextStyle(color: Color(0xFF6B7A90))),
                  const SizedBox(height: 4),
                  StatusChip(user?.role ?? 'User'),
                ]),
              ),
            ]),
          ),
        ),
        const SizedBox(height: 16),
        const Text('API connection', style: TextStyle(fontWeight: FontWeight.w700)),
        const SizedBox(height: 8),
        TextField(controller: _url, decoration: const InputDecoration(labelText: 'Base URL')),
        const SizedBox(height: 8),
        OutlinedButton(onPressed: _saveUrl, child: const Text('Save URL')),
        const SizedBox(height: 16),
        SwitchListTile(
          value: _fcm,
          onChanged: (v) => setState(() => _fcm = v),
          title: const Text('Push notifications'),
          subtitle: const Text('Campaign & inbox alerts (requires Firebase)'),
          contentPadding: EdgeInsets.zero,
        ),
        const SizedBox(height: 24),
        FilledButton.icon(
          style: FilledButton.styleFrom(backgroundColor: const Color(0xFFE11D48)),
          onPressed: () => ref.read(authProvider.notifier).logout(),
          icon: const Icon(Icons.logout),
          label: const Text('Logout'),
        ),
        const SizedBox(height: 16),
        const Center(child: Text('Win Master · v1.0', style: TextStyle(color: Color(0xFF6B7A90), fontSize: 12))),
      ]),
    );
  }
}
