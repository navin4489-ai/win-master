import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

final accountsProvider = FutureProvider.autoDispose<List<WaAccount>>((ref) async {
  final j = await ref.read(apiProvider).get('/api/whatsapp-accounts') as List;
  return j.map((e) => WaAccount.fromJson(e as Map<String, dynamic>)).toList();
});

class AccountsScreen extends ConsumerWidget {
  const AccountsScreen({super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(accountsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('WhatsApp Accounts')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(accountsProvider),
        child: async.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(children: [const SizedBox(height: 120), EmptyState('Could not load accounts.\n$e', icon: Icons.cloud_off)]),
          data: (list) => list.isEmpty
              ? ListView(children: const [SizedBox(height: 120), EmptyState('No accounts', icon: Icons.smartphone)])
              : ListView.builder(
                  padding: const EdgeInsets.all(12),
                  itemCount: list.length,
                  itemBuilder: (_, i) {
                    final a = list[i];
                    return Card(
                      margin: const EdgeInsets.only(bottom: 10),
                      child: Padding(
                        padding: const EdgeInsets.all(16),
                        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                          Row(children: [
                            Expanded(child: Text(a.displayName, style: const TextStyle(fontWeight: FontWeight.w700))),
                            StatusChip(a.status),
                          ]),
                          const SizedBox(height: 4),
                          Text('${a.phoneNumber ?? '—'} · ${a.engineType} · warmup ${a.warmupState}',
                              style: const TextStyle(fontSize: 12, color: Color(0xFF6B7A90))),
                          const SizedBox(height: 10),
                          LinearProgressIndicator(
                            value: a.dailyLimit == 0 ? 0 : (a.sentToday / a.dailyLimit).clamp(0, 1),
                            backgroundColor: const Color(0xFFE8EDF3), minHeight: 6,
                          ),
                          const SizedBox(height: 4),
                          Text('${a.sentToday}/${a.dailyLimit} sent today', style: const TextStyle(fontSize: 11, color: Color(0xFF6B7A90))),
                        ]),
                      ),
                    );
                  },
                ),
        ),
      ),
    );
  }
}
