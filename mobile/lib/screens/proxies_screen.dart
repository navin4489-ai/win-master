import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

final proxiesProvider = FutureProvider.autoDispose<List<Proxy>>((ref) async {
  final j = await ref.read(apiProvider).get('/api/proxies') as List;
  return j.map((e) => Proxy.fromJson(e)).toList();
});

class ProxiesScreen extends ConsumerWidget {
  const ProxiesScreen({super.key});

  Future<void> _add(BuildContext context, WidgetRef ref) async {
    final host = TextEditingController();
    final port = TextEditingController(text: '1080');
    final user = TextEditingController();
    final pass = TextEditingController();
    String type = 'SOCKS5';
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setState) => Padding(
          padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom, left: 16, right: 16, top: 16),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            const Text('Add proxy', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
            const SizedBox(height: 12),
            DropdownButtonFormField<String>(
              initialValue: type,
              decoration: const InputDecoration(labelText: 'Type'),
              items: const ['HTTP', 'HTTPS', 'SOCKS5'].map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
              onChanged: (v) => setState(() => type = v ?? 'SOCKS5'),
            ),
            const SizedBox(height: 10),
            Row(children: [
              Expanded(flex: 2, child: TextField(controller: host, decoration: const InputDecoration(labelText: 'Host'))),
              const SizedBox(width: 10),
              Expanded(child: TextField(controller: port, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: 'Port'))),
            ]),
            const SizedBox(height: 10),
            Row(children: [
              Expanded(child: TextField(controller: user, decoration: const InputDecoration(labelText: 'User'))),
              const SizedBox(width: 10),
              Expanded(child: TextField(controller: pass, obscureText: true, decoration: const InputDecoration(labelText: 'Password'))),
            ]),
            const SizedBox(height: 16),
            FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Add')),
            const SizedBox(height: 16),
          ]),
        ),
      ),
    );
    if (saved != true) return;
    try {
      await ref.read(apiProvider).post('/api/proxies', body: {
        'type': type, 'host': host.text, 'port': int.tryParse(port.text) ?? 1080,
        'username': user.text.isEmpty ? null : user.text, 'password': pass.text.isEmpty ? null : pass.text,
      });
      ref.invalidate(proxiesProvider);
      if (context.mounted) showOk(context, 'Proxy added');
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  Future<void> _test(BuildContext context, WidgetRef ref, Proxy p) async {
    try {
      final r = await ref.read(apiProvider).post('/api/proxies/${p.id}/test');
      if (context.mounted) {
        r['ok'] == true ? showOk(context, 'OK ${r['latencyMs']}ms') : showError(context, r['error'] ?? 'Failed');
      }
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  Future<void> _delete(BuildContext context, WidgetRef ref, Proxy p) async {
    try {
      await ref.read(apiProvider).delete('/api/proxies/${p.id}');
      ref.invalidate(proxiesProvider);
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(proxiesProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Proxies')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _add(context, ref), icon: const Icon(Icons.add), label: const Text('Add')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(proxiesProvider),
        child: async.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => EmptyState('Could not load proxies.\n$e', icon: Icons.cloud_off),
          data: (list) => list.isEmpty
              ? const EmptyState('No proxies', icon: Icons.router_outlined)
              : ListView.builder(
                  padding: const EdgeInsets.all(12),
                  itemCount: list.length,
                  itemBuilder: (_, i) {
                    final p = list[i];
                    return Card(
                      margin: const EdgeInsets.only(bottom: 10),
                      child: ListTile(
                        title: Text('${p.host}:${p.port}', style: const TextStyle(fontWeight: FontWeight.w700)),
                        subtitle: Text('${p.type}${p.username != null ? ' · ${p.username}' : ''}'),
                        trailing: Row(mainAxisSize: MainAxisSize.min, children: [
                          StatusChip(p.isHealthy ? 'Active' : 'Failed'),
                          IconButton(icon: const Icon(Icons.wifi_tethering), onPressed: () => _test(context, ref, p)),
                          IconButton(icon: const Icon(Icons.delete_outline), onPressed: () => _delete(context, ref, p)),
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
