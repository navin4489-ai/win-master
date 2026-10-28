import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

final templatesProvider = FutureProvider.autoDispose<List<Template>>((ref) async {
  final j = await ref.read(apiProvider).get('/api/templates') as List;
  return j.map((e) => Template.fromJson(e)).toList();
});

class TemplatesScreen extends ConsumerWidget {
  const TemplatesScreen({super.key});

  Future<void> _edit(BuildContext context, WidgetRef ref, [Template? t]) async {
    final name = TextEditingController(text: t?.name ?? '');
    final body = TextEditingController(text: t?.body ?? '');
    final category = TextEditingController(text: t?.category ?? 'General');
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => Padding(
        padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom, left: 16, right: 16, top: 16),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Text(t == null ? 'New template' : 'Edit template', style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
          const SizedBox(height: 12),
          TextField(controller: name, decoration: const InputDecoration(labelText: 'Name')),
          const SizedBox(height: 10),
          TextField(controller: category, decoration: const InputDecoration(labelText: 'Category')),
          const SizedBox(height: 10),
          TextField(controller: body, maxLines: 4, decoration: const InputDecoration(labelText: 'Body ({{Name}}, {a|b} spintax)')),
          const SizedBox(height: 16),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Save')),
          const SizedBox(height: 16),
        ]),
      ),
    );
    if (saved != true) return;
    final payload = {'name': name.text, 'body': body.text, 'mediaType': 'None', 'category': category.text};
    try {
      if (t == null) {
        await ref.read(apiProvider).post('/api/templates', body: payload);
      } else {
        await ref.read(apiProvider).put('/api/templates/${t.id}', body: payload);
      }
      ref.invalidate(templatesProvider);
      if (context.mounted) showOk(context, 'Saved');
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  Future<void> _delete(BuildContext context, WidgetRef ref, Template t) async {
    try {
      await ref.read(apiProvider).delete('/api/templates/${t.id}');
      ref.invalidate(templatesProvider);
      if (context.mounted) showOk(context, 'Deleted');
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(templatesProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Templates')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _edit(context, ref), icon: const Icon(Icons.add), label: const Text('New')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(templatesProvider),
        child: async.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => EmptyState('Could not load templates.\n$e', icon: Icons.cloud_off),
          data: (list) => list.isEmpty
              ? const EmptyState('No templates', icon: Icons.description_outlined)
              : ListView.builder(
                  padding: const EdgeInsets.all(12),
                  itemCount: list.length,
                  itemBuilder: (_, i) {
                    final t = list[i];
                    return Card(
                      margin: const EdgeInsets.only(bottom: 10),
                      child: ListTile(
                        title: Text(t.name, style: const TextStyle(fontWeight: FontWeight.w700)),
                        subtitle: Text(t.body, maxLines: 2, overflow: TextOverflow.ellipsis),
                        onTap: () => _edit(context, ref, t),
                        trailing: IconButton(icon: const Icon(Icons.delete_outline), onPressed: () => _delete(context, ref, t)),
                      ),
                    );
                  },
                ),
        ),
      ),
    );
  }
}
