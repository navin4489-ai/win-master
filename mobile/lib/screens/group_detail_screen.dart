import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

class GroupDetailScreen extends ConsumerStatefulWidget {
  final int groupId;
  final String groupName;
  const GroupDetailScreen({super.key, required this.groupId, required this.groupName});
  @override
  ConsumerState<GroupDetailScreen> createState() => _GroupDetailScreenState();
}

class _GroupDetailScreenState extends ConsumerState<GroupDetailScreen> {
  List<Contact> _contacts = [];
  bool _loading = true;
  final _search = TextEditingController();

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final j = await ref.read(apiProvider).get('/api/contact-groups/${widget.groupId}/contacts',
          query: {'pageSize': 100, 'q': _search.text});
      setState(() {
        _contacts = Paged.from(j, Contact.fromJson).items;
        _loading = false;
      });
    } catch (e) {
      setState(() => _loading = false);
      if (mounted) showError(context, e);
    }
  }

  Future<void> _validate() async {
    final phones = _contacts.map((c) => c.phone).toList();
    if (phones.isEmpty) return;
    try {
      final r = await ref.read(apiProvider).post('/api/contact-groups/validate', body: {'phones': phones}) as List;
      final onWa = r.where((x) => x['waRegistered'] == true).length;
      if (mounted) showOk(context, '$onWa/${r.length} are on WhatsApp');
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  Future<void> _send(Contact c) async {
    final ctrl = TextEditingController();
    final send = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => Padding(
        padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom, left: 16, right: 16, top: 16),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Text('Send to ${c.name ?? c.phone}', style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
          const SizedBox(height: 4),
          Text(c.phone, style: const TextStyle(color: Color(0xFF6B7A90), fontSize: 13)),
          const SizedBox(height: 12),
          TextField(controller: ctrl, maxLines: 3, autofocus: true, decoration: const InputDecoration(hintText: 'Type your message…')),
          const SizedBox(height: 14),
          FilledButton.icon(onPressed: () => Navigator.pop(ctx, true), icon: const Icon(Icons.send), label: const Text('Send message')),
          const SizedBox(height: 16),
        ]),
      ),
    );
    if (send != true || ctrl.text.trim().isEmpty) return;
    try {
      await ref.read(apiProvider).post('/api/inbox/send', body: {'phone': c.phone, 'body': ctrl.text.trim()});
      if (mounted) showOk(context, 'Message sent');
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  Future<void> _addContact() async {
    final phone = TextEditingController();
    final name = TextEditingController();
    final tags = TextEditingController();
    final save = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => Padding(
        padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom, left: 16, right: 16, top: 16),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          const Text('Add contact to group', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
          const SizedBox(height: 12),
          TextField(controller: phone, keyboardType: TextInputType.phone, decoration: const InputDecoration(labelText: 'Phone number', hintText: '+15551234567')),
          const SizedBox(height: 10),
          TextField(controller: name, decoration: const InputDecoration(labelText: 'Name')),
          const SizedBox(height: 10),
          TextField(controller: tags, decoration: const InputDecoration(labelText: 'Tags (comma separated)')),
          const SizedBox(height: 16),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Add contact')),
          const SizedBox(height: 16),
        ]),
      ),
    );
    if (save != true || phone.text.trim().isEmpty) return;
    try {
      await ref.read(apiProvider).post('/api/contact-groups/${widget.groupId}/contacts', body: {
        'phone': phone.text.trim(), 'name': name.text.trim().isEmpty ? null : name.text.trim(),
        'customFields': {}, 'tags': tags.text.split(',').map((x) => x.trim()).where((x) => x.isNotEmpty).toList(),
      });
      if (mounted) { showOk(context, 'Contact added'); _load(); }
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.groupName),
        actions: [
          IconButton(icon: const Icon(Icons.person_add_alt_1), tooltip: 'Add contact', onPressed: _addContact),
          IconButton(icon: const Icon(Icons.verified_outlined), tooltip: 'Validate numbers', onPressed: _validate),
        ],
      ),
      body: Column(children: [
        Padding(
          padding: const EdgeInsets.all(12),
          child: TextField(
            controller: _search,
            decoration: InputDecoration(
              hintText: 'Search…',
              prefixIcon: const Icon(Icons.search),
              suffixIcon: IconButton(icon: const Icon(Icons.arrow_forward), onPressed: _load),
            ),
            onSubmitted: (_) => _load(),
          ),
        ),
        Expanded(
          child: _loading
              ? const Center(child: CircularProgressIndicator())
              : _contacts.isEmpty
                  ? const EmptyState('No contacts', icon: Icons.person_off_outlined)
                  : RefreshIndicator(
                      onRefresh: _load,
                      child: ListView.separated(
                        itemCount: _contacts.length,
                        separatorBuilder: (_, _) => const Divider(height: 1),
                        itemBuilder: (_, i) {
                          final c = _contacts[i];
                          return ListTile(
                            title: Text(c.name ?? c.phone),
                            subtitle: Text(c.phone),
                            trailing: Row(mainAxisSize: MainAxisSize.min, children: [
                              if (c.waRegistered == true)
                                const StatusChip('Active')
                              else if (c.waRegistered == false)
                                const Icon(Icons.cancel, color: Color(0xFFE11D48), size: 20)
                              else
                                const Icon(Icons.help_outline, color: Color(0xFF64748B), size: 20),
                              IconButton(
                                icon: const Icon(Icons.send, color: Color(0xFF128C4A), size: 20),
                                tooltip: 'Send message',
                                onPressed: () => _send(c),
                              ),
                            ]),
                          );
                        },
                      ),
                    ),
        ),
      ]),
    );
  }
}
