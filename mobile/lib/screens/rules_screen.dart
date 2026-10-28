import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

final rulesProvider = FutureProvider.autoDispose<List<AutoReplyRule>>((ref) async {
  final j = await ref.read(apiProvider).get('/api/auto-reply-rules') as List;
  return j.map((e) => AutoReplyRule.fromJson(e)).toList();
});
final rulesAccountsProvider = FutureProvider.autoDispose<List<WaAccount>>((ref) async {
  final j = await ref.read(apiProvider).get('/api/whatsapp-accounts') as List;
  return j.map((e) => WaAccount.fromJson(e)).toList();
});

class RulesScreen extends ConsumerWidget {
  const RulesScreen({super.key});

  Future<void> _add(BuildContext context, WidgetRef ref, List<WaAccount> accounts) async {
    if (accounts.isEmpty) {
      showError(context, 'Add a WhatsApp account first');
      return;
    }
    final pattern = TextEditingController();
    final response = TextEditingController();
    int accountId = accounts.first.id;
    String matchType = 'Exact';
    bool useAi = false;
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setState) => Padding(
          padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom, left: 16, right: 16, top: 16),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            const Text('New auto-reply rule', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
            const SizedBox(height: 12),
            DropdownButtonFormField<int>(
              initialValue: accountId,
              decoration: const InputDecoration(labelText: 'Account'),
              items: accounts.map((a) => DropdownMenuItem(value: a.id, child: Text(a.displayName))).toList(),
              onChanged: (v) => setState(() => accountId = v ?? accountId),
            ),
            const SizedBox(height: 10),
            DropdownButtonFormField<String>(
              initialValue: matchType,
              decoration: const InputDecoration(labelText: 'Match type'),
              items: const ['Exact', 'Wildcard', 'Regex'].map((t) => DropdownMenuItem(value: t, child: Text(t))).toList(),
              onChanged: (v) => setState(() => matchType = v ?? 'Exact'),
            ),
            const SizedBox(height: 10),
            TextField(controller: pattern, decoration: const InputDecoration(labelText: 'Pattern (hi / *price* / ^stop\$)')),
            const SizedBox(height: 10),
            TextField(controller: response, maxLines: 2, decoration: const InputDecoration(labelText: 'Response text')),
            SwitchListTile(value: useAi, onChanged: (v) => setState(() => useAi = v), title: const Text('Use AI response'), contentPadding: EdgeInsets.zero),
            FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Create')),
            const SizedBox(height: 16),
          ]),
        ),
      ),
    );
    if (saved != true) return;
    try {
      await ref.read(apiProvider).post('/api/auto-reply-rules', body: {
        'accountId': accountId, 'matchType': matchType, 'pattern': pattern.text, 'priority': 1,
        'responseText': response.text, 'responseTemplateId': null, 'useAi': useAi, 'isActive': true,
      });
      ref.invalidate(rulesProvider);
      if (context.mounted) showOk(context, 'Rule created');
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  Future<void> _toggle(WidgetRef ref, AutoReplyRule r) async {
    await ref.read(apiProvider).put('/api/auto-reply-rules/${r.id}/toggle');
    ref.invalidate(rulesProvider);
  }

  Future<void> _delete(WidgetRef ref, AutoReplyRule r) async {
    await ref.read(apiProvider).delete('/api/auto-reply-rules/${r.id}');
    ref.invalidate(rulesProvider);
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(rulesProvider);
    final accounts = ref.watch(rulesAccountsProvider).value ?? [];
    return Scaffold(
      appBar: AppBar(title: const Text('Auto-Reply Rules')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _add(context, ref, accounts), icon: const Icon(Icons.add), label: const Text('New')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(rulesProvider),
        child: async.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => EmptyState('Could not load rules.\n$e', icon: Icons.cloud_off),
          data: (list) => list.isEmpty
              ? const EmptyState('No rules', icon: Icons.smart_toy_outlined)
              : ListView.builder(
                  padding: const EdgeInsets.all(12),
                  itemCount: list.length,
                  itemBuilder: (_, i) {
                    final r = list[i];
                    return Card(
                      margin: const EdgeInsets.only(bottom: 10),
                      child: ListTile(
                        title: Row(children: [
                          Expanded(child: Text(r.pattern, style: const TextStyle(fontWeight: FontWeight.w700, fontFamily: 'monospace'))),
                          if (r.useAi) const Padding(padding: EdgeInsets.only(right: 6), child: Icon(Icons.auto_awesome, size: 16, color: Color(0xFF128C4A))),
                        ]),
                        subtitle: Text('${r.matchType} → ${r.responseText ?? (r.useAi ? 'AI reply' : '')}', maxLines: 2, overflow: TextOverflow.ellipsis),
                        trailing: Row(mainAxisSize: MainAxisSize.min, children: [
                          Switch(value: r.isActive, onChanged: (_) => _toggle(ref, r)),
                          IconButton(icon: const Icon(Icons.delete_outline), onPressed: () => _delete(ref, r)),
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
