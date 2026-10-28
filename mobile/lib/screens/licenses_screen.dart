import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

final licensesProvider = FutureProvider.autoDispose<List<License>>((ref) async {
  final j = await ref.read(apiProvider).get('/api/licenses', query: {'pageSize': 100});
  return Paged.from(j, License.fromJson).items;
});

class LicensesScreen extends ConsumerWidget {
  const LicensesScreen({super.key});

  List<String> _issuable(String? role) {
    if (role == 'SuperAdmin') return ['Admin', 'Reseller', 'User'];
    if (role == 'Admin') return ['Reseller', 'User'];
    return ['User'];
  }

  Future<void> _generate(BuildContext context, WidgetRef ref) async {
    final role = ref.read(authProvider).user?.role;
    final types = _issuable(role);
    String type = types.last;
    final result = await showDialog<bool>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setState) => AlertDialog(
          title: const Text('Generate license'),
          content: DropdownButtonFormField<String>(
            initialValue: type,
            items: types.map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
            onChanged: (v) => setState(() => type = v ?? type),
            decoration: const InputDecoration(labelText: 'Type'),
          ),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('Cancel')),
            FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Generate')),
          ],
        ),
      ),
    );
    if (result != true) return;
    try {
      await ref.read(apiProvider).post('/api/licenses', body: {
        'licenseType': type,
        'features': ['campaigns', 'inbox'],
        'startsAt': DateTime.now().toUtc().toIso8601String(),
        'expiresAt': null,
        'maxActivations': 1,
        'userId': null,
      });
      if (context.mounted) showOk(context, 'License generated');
      ref.invalidate(licensesProvider);
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  Future<void> _revoke(BuildContext context, WidgetRef ref, License l) async {
    try {
      await ref.read(apiProvider).post('/api/licenses/${l.id}/revoke');
      if (context.mounted) showOk(context, 'Revoked');
      ref.invalidate(licensesProvider);
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(licensesProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Licenses')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _generate(context, ref),
        icon: const Icon(Icons.add),
        label: const Text('Generate'),
      ),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(licensesProvider),
        child: async.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(children: [const SizedBox(height: 120), EmptyState('Could not load licenses.\n$e', icon: Icons.cloud_off)]),
          data: (list) => list.isEmpty
              ? ListView(children: const [SizedBox(height: 120), EmptyState('No licenses', icon: Icons.vpn_key_outlined)])
              : ListView.builder(
                  padding: const EdgeInsets.all(12),
                  itemCount: list.length,
                  itemBuilder: (_, i) {
                    final l = list[i];
                    return Card(
                      margin: const EdgeInsets.only(bottom: 10),
                      child: ListTile(
                        title: Text(l.key, style: const TextStyle(fontWeight: FontWeight.w700, fontFamily: 'monospace', fontSize: 13)),
                        subtitle: Text('${l.licenseType} · ${l.activationCount}/${l.maxActivations} devices · ${l.userEmail ?? 'unassigned'}'),
                        trailing: Row(mainAxisSize: MainAxisSize.min, children: [
                          StatusChip(l.status),
                          if (l.status == 'Active')
                            IconButton(icon: const Icon(Icons.block, size: 20), onPressed: () => _revoke(context, ref, l)),
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
