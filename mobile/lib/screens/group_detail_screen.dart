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

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.groupName),
        actions: [
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
                            trailing: c.waRegistered == true
                                ? const StatusChip('Active')
                                : c.waRegistered == false
                                    ? const Icon(Icons.cancel, color: Color(0xFFE11D48), size: 20)
                                    : const Icon(Icons.help_outline, color: Color(0xFF64748B), size: 20),
                          );
                        },
                      ),
                    ),
        ),
      ]),
    );
  }
}
