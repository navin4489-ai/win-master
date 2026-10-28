import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

final usersProvider = FutureProvider.autoDispose<List<AppUser>>((ref) async {
  final j = await ref.read(apiProvider).get('/api/users', query: {'pageSize': 100});
  return Paged.from(j, AppUser.fromJson).items;
});

class UsersScreen extends ConsumerWidget {
  const UsersScreen({super.key});

  Future<void> _add(BuildContext context, WidgetRef ref) async {
    final role = ref.read(authProvider).user?.role;
    final roles = role == 'SuperAdmin' ? ['Admin', 'Reseller', 'User'] : ['Reseller', 'User'];
    final name = TextEditingController();
    final email = TextEditingController();
    final pass = TextEditingController();
    String selected = roles.last;
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setState) => Padding(
          padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom, left: 16, right: 16, top: 16),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            const Text('New user', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
            const SizedBox(height: 12),
            TextField(controller: name, decoration: const InputDecoration(labelText: 'Display name')),
            const SizedBox(height: 10),
            TextField(controller: email, keyboardType: TextInputType.emailAddress, decoration: const InputDecoration(labelText: 'Email')),
            const SizedBox(height: 10),
            TextField(controller: pass, obscureText: true, decoration: const InputDecoration(labelText: 'Password')),
            const SizedBox(height: 10),
            DropdownButtonFormField<String>(
              initialValue: selected,
              decoration: const InputDecoration(labelText: 'Role'),
              items: roles.map((r) => DropdownMenuItem(value: r, child: Text(r))).toList(),
              onChanged: (v) => setState(() => selected = v ?? selected),
            ),
            const SizedBox(height: 16),
            FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Create')),
            const SizedBox(height: 16),
          ]),
        ),
      ),
    );
    if (saved != true) return;
    try {
      await ref.read(apiProvider).post('/api/users', body: {
        'email': email.text, 'password': pass.text, 'displayName': name.text, 'role': selected, 'parentUserId': null,
      });
      ref.invalidate(usersProvider);
      if (context.mounted) showOk(context, 'User created');
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  Future<void> _toggle(WidgetRef ref, AppUser u) async {
    await ref.read(apiProvider).put('/api/users/${u.id}', body: {'displayName': u.displayName, 'isActive': !u.isActive});
    ref.invalidate(usersProvider);
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(usersProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Users')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _add(context, ref), icon: const Icon(Icons.add), label: const Text('New')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(usersProvider),
        child: async.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => EmptyState('Could not load users.\n$e', icon: Icons.cloud_off),
          data: (list) => list.isEmpty
              ? const EmptyState('No users', icon: Icons.group_outlined)
              : ListView.builder(
                  padding: const EdgeInsets.all(12),
                  itemCount: list.length,
                  itemBuilder: (_, i) {
                    final u = list[i];
                    return Card(
                      margin: const EdgeInsets.only(bottom: 10),
                      child: ListTile(
                        leading: CircleAvatar(child: Text(u.displayName.isNotEmpty ? u.displayName[0].toUpperCase() : '?')),
                        title: Text(u.displayName, style: const TextStyle(fontWeight: FontWeight.w700)),
                        subtitle: Text(u.email),
                        trailing: Row(mainAxisSize: MainAxisSize.min, children: [
                          StatusChip(u.role),
                          Switch(value: u.isActive, onChanged: (_) => _toggle(ref, u)),
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
