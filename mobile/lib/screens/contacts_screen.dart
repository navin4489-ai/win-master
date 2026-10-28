import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

final groupsProvider = FutureProvider.autoDispose<List<ContactGroup>>((ref) async {
  final j = await ref.read(apiProvider).get('/api/contact-groups', query: {'pageSize': 100});
  return Paged.from(j, ContactGroup.fromJson).items;
});

class ContactsScreen extends ConsumerWidget {
  const ContactsScreen({super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(groupsProvider);
    return RefreshIndicator(
      onRefresh: () async => ref.invalidate(groupsProvider),
      child: async.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ListView(children: [const SizedBox(height: 120), EmptyState('Could not load contacts.\n$e', icon: Icons.cloud_off)]),
        data: (list) => list.isEmpty
            ? ListView(children: const [SizedBox(height: 120), EmptyState('No contact groups', icon: Icons.people_outline)])
            : ListView.builder(
                padding: const EdgeInsets.all(12),
                itemCount: list.length,
                itemBuilder: (_, i) {
                  final g = list[i];
                  return Card(
                    margin: const EdgeInsets.only(bottom: 10),
                    child: ListTile(
                      leading: const CircleAvatar(child: Icon(Icons.folder_shared_outlined)),
                      title: Text(g.name, style: const TextStyle(fontWeight: FontWeight.w700)),
                      subtitle: Text('${g.totalContacts} contacts · ${g.validContacts} on WhatsApp'),
                      trailing: Wrap(spacing: 4, children: [
                        for (final t in g.tags.take(2))
                          Chip(label: Text(t, style: const TextStyle(fontSize: 11)), visualDensity: VisualDensity.compact),
                      ]),
                      onTap: () => context.push('/contacts/${g.id}', extra: g.name),
                    ),
                  );
                },
              ),
      ),
    );
  }
}
