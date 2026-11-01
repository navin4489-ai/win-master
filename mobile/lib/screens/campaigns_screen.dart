import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

final campaignsProvider = FutureProvider.autoDispose<List<Campaign>>((ref) async {
  final j = await ref.read(apiProvider).get('/api/campaigns', query: {'pageSize': 100});
  return Paged.from(j, Campaign.fromJson).items;
});

class CampaignsScreen extends ConsumerWidget {
  const CampaignsScreen({super.key});
  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(campaignsProvider);
    return RefreshIndicator(
      onRefresh: () async => ref.invalidate(campaignsProvider),
      child: async.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ListView(children: [const SizedBox(height: 120), EmptyState('Could not load campaigns.\n$e', icon: Icons.cloud_off)]),
        data: (list) => list.isEmpty
            ? ListView(children: const [SizedBox(height: 120), EmptyState('No campaigns yet')])
            : ListView.builder(
                padding: const EdgeInsets.all(12),
                itemCount: list.length,
                itemBuilder: (_, i) {
                  final c = list[i];
                  return Card(
                    margin: const EdgeInsets.only(bottom: 10),
                    child: ListTile(
                      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
                      title: Text(c.name, style: const TextStyle(fontWeight: FontWeight.w700)),
                      subtitle: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        const SizedBox(height: 4),
                        Text('${c.engineType} · ${c.groupName ?? ''}', style: const TextStyle(fontSize: 12)),
                        const SizedBox(height: 6),
                        LinearProgressIndicator(
                          value: c.totalRecipients == 0 ? 0 : c.processedCount / c.totalRecipients,
                          backgroundColor: const Color(0xFFE8EDF3),
                          minHeight: 6,
                        ),
                        const SizedBox(height: 4),
                        Text('${c.processedCount}/${c.totalRecipients} processed', style: const TextStyle(fontSize: 11, color: Color(0xFF6B7A90))),
                      ]),
                      trailing: StatusChip(c.status),
                      onTap: () => context.push('/campaigns/${c.id}'),
                    ),
                  );
                },
              ),
      ),
    );
  }
}

class CampaignDetailScreen extends ConsumerStatefulWidget {
  final int id;
  const CampaignDetailScreen({super.key, required this.id});
  @override
  ConsumerState<CampaignDetailScreen> createState() => _CampaignDetailScreenState();
}

class _CampaignDetailScreenState extends ConsumerState<CampaignDetailScreen> {
  CampaignProgress? _p;
  String _name = 'Campaign';
  bool _pausedByConnection = false;
  String _connectionMessage = '';
  int _failedCount = 0;
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _load();
    _timer = Timer.periodic(const Duration(seconds: 3), (_) => _poll());
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final c = await ref.read(apiProvider).get('/api/campaigns/${widget.id}');
      if (mounted) {
        setState(() {
          _name = c['name'] ?? 'Campaign';
          _pausedByConnection = c['pausedByConnection'] == true;
          _connectionMessage = c['connectionMessage']?.toString() ?? '';
          _failedCount = (c['failedCount'] as num?)?.toInt() ?? 0;
        });
      }
    } catch (_) {}
    _poll();
  }

  Future<void> _poll() async {
    try {
      final j = await ref.read(apiProvider).get('/api/campaigns/${widget.id}/progress');
      if (mounted) setState(() => _p = CampaignProgress.fromJson(j));
    } catch (_) {}
  }

  Future<void> _action(String a) async {
    try {
      await ref.read(apiProvider).post('/api/campaigns/${widget.id}/$a');
      if (mounted) showOk(context, 'Campaign ${a}ed');
      _poll();
      _load();
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  Future<void> _retryFailed() async {
    try {
      await ref.read(apiProvider).post('/api/campaigns/${widget.id}/retry-failed');
      if (mounted) showOk(context, 'Retrying failed recipients');
      _poll();
      _load();
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  @override
  Widget build(BuildContext context) {
    final p = _p;
    return Scaffold(
      appBar: AppBar(title: Text(_name)),
      body: p == null
          ? const Center(child: CircularProgressIndicator())
          : ListView(padding: const EdgeInsets.all(16), children: [
              if (_pausedByConnection)
                Container(
                  margin: const EdgeInsets.only(bottom: 14),
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: const Color(0xFFFEF3C7),
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: const Color(0xFFF59E0B)),
                  ),
                  child: Row(children: [
                    const Icon(Icons.wifi_off, color: Color(0xFFB45309)),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        const Text('Paused — WhatsApp disconnected', style: TextStyle(fontWeight: FontWeight.w700, color: Color(0xFFB45309))),
                        const SizedBox(height: 2),
                        Text(_connectionMessage.isEmpty
                            ? 'Scan the QR under Accounts to reconnect. This campaign resumes automatically once connected.'
                            : _connectionMessage, style: const TextStyle(fontSize: 12, color: Color(0xFF92400E))),
                      ]),
                    ),
                  ]),
                ),
              Row(children: [StatusChip(p.status), const Spacer(), Text('${p.processed}/${p.total}')]),
              const SizedBox(height: 12),
              LinearProgressIndicator(
                value: p.total == 0 ? 0 : p.processed / p.total,
                backgroundColor: const Color(0xFFE8EDF3), minHeight: 10,
              ),
              const SizedBox(height: 20),
              GridView.count(
                crossAxisCount: 2, shrinkWrap: true, physics: const NeverScrollableScrollPhysics(),
                mainAxisSpacing: 12, crossAxisSpacing: 12, childAspectRatio: 1.7,
                children: [
                  StatCard(label: 'Sent', value: '${p.sent}', icon: Icons.send),
                  StatCard(label: 'Delivered', value: '${p.delivered}', icon: Icons.done_all),
                  StatCard(label: 'Read', value: '${p.read}', icon: Icons.visibility),
                  StatCard(label: 'Failed', value: '${p.failed}', icon: Icons.error_outline),
                ],
              ),
              const SizedBox(height: 20),
              Wrap(spacing: 10, children: [
                if (p.status == 'Draft' || p.status == 'Scheduled' || p.status == 'Paused')
                  FilledButton.icon(onPressed: () => _action('start'), icon: const Icon(Icons.play_arrow), label: const Text('Start')),
                if (p.status == 'Running')
                  OutlinedButton.icon(onPressed: () => _action('pause'), icon: const Icon(Icons.pause), label: const Text('Pause')),
                if (p.status == 'Paused')
                  FilledButton.icon(onPressed: () => _action('resume'), icon: const Icon(Icons.play_arrow), label: const Text('Resume')),
                if (p.status == 'Running' || p.status == 'Paused')
                  OutlinedButton.icon(onPressed: () => _action('stop'), icon: const Icon(Icons.stop), label: const Text('Stop')),
                if (p.status == 'Completed' || p.status == 'Stopped' || p.status == 'Failed')
                  FilledButton.icon(onPressed: () => _action('restart'), icon: const Icon(Icons.refresh), label: const Text('Restart')),
                if (p.status != 'Running' && _failedCount > 0)
                  OutlinedButton.icon(onPressed: _retryFailed, icon: const Icon(Icons.replay), label: Text('Retry failed ($_failedCount)')),
              ]),
            ]),
    );
  }
}
