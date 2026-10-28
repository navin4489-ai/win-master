import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

final deliveryProvider = FutureProvider.autoDispose<DeliveryReport>((ref) async {
  final j = await ref.read(apiProvider).get('/api/reports/delivery');
  return DeliveryReport.fromJson(j);
});

class ReportsScreen extends ConsumerWidget {
  const ReportsScreen({super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(deliveryProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Reports')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(deliveryProvider),
        child: async.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => EmptyState('Could not load reports.\n$e', icon: Icons.cloud_off),
          data: (r) => ListView(padding: const EdgeInsets.all(16), children: [
            GridView.count(
              crossAxisCount: 2, shrinkWrap: true, physics: const NeverScrollableScrollPhysics(),
              mainAxisSpacing: 12, crossAxisSpacing: 12, childAspectRatio: 1.5,
              children: [
                StatCard(label: 'Delivery Rate', value: '${r.deliveryRate}%', icon: Icons.done_all),
                StatCard(label: 'Read Rate', value: '${r.readRate}%', icon: Icons.visibility),
                StatCard(label: 'Sent', value: '${r.sent}', icon: Icons.send),
                StatCard(label: 'Delivered', value: '${r.delivered}', icon: Icons.check_circle_outline),
                StatCard(label: 'Read', value: '${r.read}', icon: Icons.mark_email_read_outlined),
                StatCard(label: 'Failed', value: '${r.failed}', icon: Icons.error_outline),
              ],
            ),
          ]),
        ),
      ),
    );
  }
}
