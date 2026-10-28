import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../core/config.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

final threadsProvider = FutureProvider.autoDispose<List<InboxThread>>((ref) async {
  final j = await ref.read(apiProvider).get('/api/inbox/threads', query: {'pageSize': 100});
  return Paged.from(j, InboxThread.fromJson).items;
});

class InboxScreen extends ConsumerWidget {
  const InboxScreen({super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(threadsProvider);
    return RefreshIndicator(
      onRefresh: () async => ref.invalidate(threadsProvider),
      child: async.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ListView(children: [const SizedBox(height: 120), EmptyState('Could not load inbox.\n$e', icon: Icons.cloud_off)]),
        data: (list) => list.isEmpty
            ? ListView(children: const [SizedBox(height: 120), EmptyState('No conversations', icon: Icons.chat_bubble_outline)])
            : ListView.separated(
                itemCount: list.length,
                separatorBuilder: (_, _) => const Divider(height: 1),
                itemBuilder: (_, i) {
                  final t = list[i];
                  return ListTile(
                    leading: CircleAvatar(
                      backgroundColor: AppTheme.primary.withValues(alpha: 0.12),
                      child: Text((t.contactName ?? t.contactPhoneMasked).characters.first.toUpperCase(),
                          style: const TextStyle(color: AppTheme.primary, fontWeight: FontWeight.bold)),
                    ),
                    title: Text(t.contactName ?? t.contactPhoneMasked, style: const TextStyle(fontWeight: FontWeight.w600)),
                    subtitle: Text(t.preview ?? '', maxLines: 1, overflow: TextOverflow.ellipsis),
                    trailing: t.unreadCount > 0
                        ? CircleAvatar(radius: 11, backgroundColor: AppTheme.accent,
                            child: Text('${t.unreadCount}', style: const TextStyle(color: Colors.white, fontSize: 11)))
                        : Text(t.accountLabel, style: const TextStyle(fontSize: 11, color: Color(0xFF6B7A90))),
                    onTap: () => context.push('/inbox/${t.id}', extra: t),
                  );
                },
              ),
      ),
    );
  }
}

class ChatScreen extends ConsumerStatefulWidget {
  final int threadId;
  final InboxThread? thread;
  const ChatScreen({super.key, required this.threadId, this.thread});
  @override
  ConsumerState<ChatScreen> createState() => _ChatScreenState();
}

class _ChatScreenState extends ConsumerState<ChatScreen> {
  final _ctrl = TextEditingController();
  List<InboxMessage> _messages = [];
  bool _loading = true;
  bool _botEnabled = true;

  @override
  void initState() {
    super.initState();
    _botEnabled = widget.thread?.isBotEnabled ?? true;
    _load();
  }

  @override
  void dispose() {
    _ctrl.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final j = await ref.read(apiProvider).get('/api/inbox/threads/${widget.threadId}/messages', query: {'pageSize': 200});
      setState(() {
        _messages = Paged.from(j, InboxMessage.fromJson).items;
        _loading = false;
      });
    } catch (e) {
      setState(() => _loading = false);
      if (mounted) showError(context, e);
    }
  }

  Future<void> _send() async {
    final text = _ctrl.text.trim();
    if (text.isEmpty) return;
    _ctrl.clear();
    try {
      final j = await ref.read(apiProvider).post('/api/inbox/threads/${widget.threadId}/messages', body: {'body': text});
      setState(() => _messages = [..._messages, InboxMessage.fromJson(j)]);
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  Future<void> _toggleBot(bool v) async {
    setState(() => _botEnabled = v);
    try {
      await ref.read(apiProvider).put('/api/inbox/threads/${widget.threadId}/bot', body: {'isBotEnabled': v});
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.thread?.contactName ?? widget.thread?.contactPhoneMasked ?? 'Chat'),
        actions: [
          Row(children: [
            const Text('Bot', style: TextStyle(fontSize: 12)),
            Switch(value: _botEnabled, onChanged: _toggleBot),
          ]),
        ],
      ),
      backgroundColor: const Color(0xFFECE5DD),
      body: Column(children: [
        Expanded(
          child: _loading
              ? const Center(child: CircularProgressIndicator())
              : ListView.builder(
                  padding: const EdgeInsets.all(12),
                  itemCount: _messages.length,
                  itemBuilder: (_, i) {
                    final m = _messages[i];
                    final out = m.direction == 'Out';
                    return Align(
                      alignment: out ? Alignment.centerRight : Alignment.centerLeft,
                      child: Container(
                        margin: const EdgeInsets.symmetric(vertical: 3),
                        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                        constraints: BoxConstraints(maxWidth: MediaQuery.of(context).size.width * 0.75),
                        decoration: BoxDecoration(
                          color: out ? const Color(0xFFDCF8C6) : Colors.white,
                          borderRadius: BorderRadius.circular(10),
                        ),
                        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                          Text(m.body),
                          if (m.status != null)
                            Text(m.status!, style: const TextStyle(fontSize: 10, color: Color(0xFF6B7A90))),
                        ]),
                      ),
                    );
                  },
                ),
        ),
        SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(8),
            child: Row(children: [
              Expanded(
                child: TextField(
                  controller: _ctrl,
                  decoration: const InputDecoration(hintText: 'Type a reply…', fillColor: Colors.white),
                  onSubmitted: (_) => _send(),
                ),
              ),
              const SizedBox(width: 8),
              CircleAvatar(
                backgroundColor: AppTheme.primary,
                child: IconButton(icon: const Icon(Icons.send, color: Colors.white), onPressed: _send),
              ),
            ]),
          ),
        ),
      ]),
    );
  }
}

