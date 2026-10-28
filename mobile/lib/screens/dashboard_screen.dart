import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

final dashboardProvider = FutureProvider.autoDispose<DashboardStats>((ref) async {
  final j = await ref.read(apiProvider).get('/api/dashboard/stats');
  return DashboardStats.fromJson(j);
});

class DashboardScreen extends ConsumerWidget {
  const DashboardScreen({super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(dashboardProvider);
    return RefreshIndicator(
      onRefresh: () async => ref.invalidate(dashboardProvider),
      child: async.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ListView(children: [const SizedBox(height: 120), EmptyState('Could not load dashboard.\n$e', icon: Icons.cloud_off)]),
        data: (s) => ListView(
          padding: const EdgeInsets.all(16),
          children: [
            GridView.count(
              crossAxisCount: 2,
              shrinkWrap: true,
              physics: const NeverScrollableScrollPhysics(),
              mainAxisSpacing: 12,
              crossAxisSpacing: 12,
              childAspectRatio: 1.35,
              children: [
                StatCard(label: 'Campaigns', value: '${s.campaignsTotal}', icon: Icons.rocket_launch, sub: '${s.campaignsRunning} running · ${s.campaignsScheduled} scheduled'),
                StatCard(label: 'Sent Today', value: '${s.sentToday}', icon: Icons.send, sub: '${s.deliveredToday} delivered · ${s.failedToday} failed'),
                StatCard(label: 'Accounts', value: '${s.accountsConnected}/${s.accountsTotal}', icon: Icons.smartphone, sub: 'connected'),
                StatCard(label: 'Contacts', value: '${s.contactsTotal}', icon: Icons.people, sub: '${s.contactsGroups} groups'),
                StatCard(label: 'Licenses', value: '${s.licensesActive}', icon: Icons.vpn_key, sub: '${s.licensesExpiring} expiring'),
                StatCard(label: 'Inbox', value: '${s.inboxUnread}', icon: Icons.chat, sub: '${s.inboxThreads} threads'),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
