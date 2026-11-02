import 'dart:async';
import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

final accountsProvider = FutureProvider.autoDispose<List<WaAccount>>((ref) async {
  final j = await ref.read(apiProvider).get('/api/whatsapp-accounts') as List;
  return j.map((e) => WaAccount.fromJson(e as Map<String, dynamic>)).toList();
});

class AccountsScreen extends ConsumerWidget {
  const AccountsScreen({super.key});

  Future<void> _addAccount(BuildContext context, WidgetRef ref) async {
    final name = TextEditingController();
    final phone = TextEditingController();
    final phoneNumberId = TextEditingController();
    final accessToken = TextEditingController();
    final wabaId = TextEditingController();
    String engine = 'Baileys';
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setState) => Padding(
          padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom, left: 16, right: 16, top: 16),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            const Text('Add WhatsApp account', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
            const SizedBox(height: 12),
            TextField(controller: name, decoration: const InputDecoration(labelText: 'Display name')),
            const SizedBox(height: 10),
            DropdownButtonFormField<String>(
              initialValue: engine,
              decoration: const InputDecoration(labelText: 'Engine'),
              items: const [DropdownMenuItem(value: 'Baileys', child: Text('Baileys')), DropdownMenuItem(value: 'CloudApi', child: Text('Cloud API'))],
              onChanged: (v) => setState(() => engine = v ?? 'Baileys'),
            ),
            const SizedBox(height: 10),
            TextField(controller: phone, decoration: const InputDecoration(labelText: 'Phone number', hintText: '+1...')),
            if (engine == 'CloudApi') ...[
              const SizedBox(height: 10),
              const Align(alignment: Alignment.centerLeft, child: Text('Official WhatsApp Cloud API (Meta) — no QR, stays connected, ban-safe. Paste from Meta app → WhatsApp → API Setup.', style: TextStyle(fontSize: 12, color: Color(0xFF6B7A90)))),
              const SizedBox(height: 10),
              TextField(controller: phoneNumberId, decoration: const InputDecoration(labelText: 'Phone Number ID')),
              const SizedBox(height: 10),
              TextField(controller: accessToken, decoration: const InputDecoration(labelText: 'Access Token')),
              const SizedBox(height: 10),
              TextField(controller: wabaId, decoration: const InputDecoration(labelText: 'WhatsApp Business Account ID (optional)')),
            ],
            const SizedBox(height: 16),
            FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Create')),
            const SizedBox(height: 16),
          ]),
        ),
      ),
    );
    if (saved != true) return;
    try {
      Map<String, dynamic>? credentials;
      if (engine == 'CloudApi') {
        if (phoneNumberId.text.trim().isEmpty || accessToken.text.trim().isEmpty) {
          if (context.mounted) showError(context, 'Cloud API needs a Phone Number ID and Access Token');
          return;
        }
        credentials = {
          'phoneNumberId': phoneNumberId.text.trim(),
          'accessToken': accessToken.text.trim(),
          'wabaId': wabaId.text.trim(),
        };
      }
      await ref.read(apiProvider).post('/api/whatsapp-accounts', body: {
        'engineType': engine, 'displayName': name.text, 'phoneNumber': phone.text, 'proxyId': null, 'credentials': credentials,
      });
      ref.invalidate(accountsProvider);
      if (context.mounted) showOk(context, 'Account added');
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  Future<void> _connect(BuildContext context, WidgetRef ref, WaAccount a) async {
    try {
      await ref.read(apiProvider).post('/api/whatsapp-accounts/${a.id}/connect');
      ref.invalidate(accountsProvider);
      if (context.mounted) {
        await showDialog(
          context: context,
          barrierDismissible: false,
          builder: (ctx) => _QrDialog(accountId: a.id, displayName: a.displayName),
        );
        ref.invalidate(accountsProvider);
      }
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  Future<void> _disconnect(BuildContext context, WidgetRef ref, WaAccount a) async {
    try {
      await ref.read(apiProvider).post('/api/whatsapp-accounts/${a.id}/disconnect');
      ref.invalidate(accountsProvider);
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  Future<void> _clearRestriction(BuildContext context, WidgetRef ref, WaAccount a) async {
    try {
      await ref.read(apiProvider).post('/api/whatsapp-accounts/${a.id}/clear-restriction');
      ref.invalidate(accountsProvider);
      if (context.mounted) showOk(context, 'Restriction cleared — usable again within safe limits');
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  Future<void> _makeDefault(BuildContext context, WidgetRef ref, WaAccount a) async {
    try {
      await ref.read(apiProvider).post('/api/whatsapp-accounts/${a.id}/set-default');
      ref.invalidate(accountsProvider);
      if (context.mounted) showOk(context, '${a.displayName} is now your default sending number');
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  Future<void> _antiBan(BuildContext context, WidgetRef ref, WaAccount a) async {
    Map<String, dynamic> s;
    try {
      s = Map<String, dynamic>.from(await ref.read(apiProvider).get('/api/anti-ban/${a.id}'));
    } catch (e) {
      if (context.mounted) showError(context, e);
      return;
    }
    int minD = s['minDelaySec'] ?? 30, maxD = s['maxDelaySec'] ?? 60, batch = s['batchSize'] ?? 15, cool = s['cooldownMinutes'] ?? 10, cap = s['dailyCap'] ?? 50;
    if (!context.mounted) return;
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setState) => Padding(
          padding: EdgeInsets.only(bottom: MediaQuery.of(ctx).viewInsets.bottom, left: 16, right: 16, top: 16),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Text('Anti-ban — ${a.displayName}', style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
            _row('Min delay (s): $minD', minD, 1, 60, (v) => setState(() => minD = v)),
            _row('Max delay (s): $maxD', maxD, 1, 120, (v) => setState(() => maxD = v)),
            _row('Batch size: $batch', batch, 10, 500, (v) => setState(() => batch = v)),
            _row('Cooldown (min): $cool', cool, 0, 60, (v) => setState(() => cool = v)),
            _row('Daily cap: $cap', cap, 100, 5000, (v) => setState(() => cap = v)),
            const SizedBox(height: 8),
            FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('Save')),
            const SizedBox(height: 16),
          ]),
        ),
      ),
    );
    if (saved != true) return;
    try {
      await ref.read(apiProvider).put('/api/anti-ban/${a.id}', body: {
        'minDelaySec': minD, 'maxDelaySec': maxD, 'batchSize': batch, 'cooldownMinutes': cool, 'dailyCap': cap,
      });
      if (context.mounted) showOk(context, 'Anti-ban saved');
    } catch (e) {
      if (context.mounted) showError(context, e);
    }
  }

  static Widget _row(String label, int value, int min, int max, ValueChanged<int> onChanged) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: const TextStyle(fontSize: 13, color: Color(0xFF6B7A90))),
          Slider(value: value.toDouble().clamp(min.toDouble(), max.toDouble()), min: min.toDouble(), max: max.toDouble(),
              divisions: ((max - min) ~/ (max > 100 ? 10 : 1)).clamp(1, 1000), label: '$value', onChanged: (v) => onChanged(v.round())),
        ],
      );

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(accountsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('WhatsApp Accounts')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => _addAccount(context, ref), icon: const Icon(Icons.add), label: const Text('Add')),
      body: RefreshIndicator(
        onRefresh: () async => ref.invalidate(accountsProvider),
        child: async.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (e, _) => ListView(children: [const SizedBox(height: 120), EmptyState('Could not load accounts.\n$e', icon: Icons.cloud_off)]),
          data: (list) => list.isEmpty
              ? ListView(children: const [SizedBox(height: 120), EmptyState('No accounts', icon: Icons.smartphone)])
              : ListView.builder(
                  padding: const EdgeInsets.all(12),
                  itemCount: list.length,
                  itemBuilder: (_, i) {
                    final a = list[i];
                    return Card(
                      margin: const EdgeInsets.only(bottom: 10),
                      child: Padding(
                        padding: const EdgeInsets.all(16),
                        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                          Row(children: [
                            Expanded(child: Text(a.displayName, style: const TextStyle(fontWeight: FontWeight.w700))),
                            if (a.isDefault) ...[
                              Container(
                                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                                margin: const EdgeInsets.only(right: 8),
                                decoration: BoxDecoration(color: const Color(0xFFDCFCE7), borderRadius: BorderRadius.circular(20)),
                                child: const Text('Default', style: TextStyle(fontSize: 11, color: Color(0xFF166534), fontWeight: FontWeight.w600)),
                              ),
                            ],
                            StatusChip(a.status),
                          ]),
                          const SizedBox(height: 4),
                          Text('${a.phoneNumber ?? '—'} · ${a.engineType} · warmup ${a.warmupState}',
                              style: const TextStyle(fontSize: 12, color: Color(0xFF6B7A90))),
                          if (a.isRestricted) ...[
                            const SizedBox(height: 8),
                            Container(
                              padding: const EdgeInsets.all(8),
                              decoration: BoxDecoration(
                                color: const Color(0xFFFEE2E2),
                                borderRadius: BorderRadius.circular(8),
                                border: Border.all(color: const Color(0xFFDC2626)),
                              ),
                              child: Row(children: [
                                const Icon(Icons.shield_outlined, size: 18, color: Color(0xFFB91C1C)),
                                const SizedBox(width: 8),
                                Expanded(
                                  child: Text(
                                    'Restricted by WhatsApp — ${a.restrictionReason ?? 'sending stopped to protect the number'}. Campaigns are paused; it recovers automatically after the cool-down.',
                                    style: const TextStyle(fontSize: 11, color: Color(0xFF991B1B)),
                                  ),
                                ),
                              ]),
                            ),
                            Align(
                              alignment: Alignment.centerLeft,
                              child: TextButton(
                                onPressed: () => _clearRestriction(context, ref, a),
                                child: const Text('Clear & use again'),
                              ),
                            ),
                          ],
                          const SizedBox(height: 10),
                          LinearProgressIndicator(
                            value: a.effectiveDailyCap == 0 ? 0 : (a.sentToday / a.effectiveDailyCap).clamp(0, 1),
                            backgroundColor: const Color(0xFFE8EDF3), minHeight: 6,
                          ),
                          const SizedBox(height: 4),
                          Text('${a.sentToday}/${a.effectiveDailyCap} sent today (safe cap)', style: const TextStyle(fontSize: 11, color: Color(0xFF6B7A90))),
                          const SizedBox(height: 8),
                          Wrap(spacing: 6, children: [
                            if (a.engineType == 'Baileys' && a.status != 'Connected')
                              FilledButton.tonalIcon(onPressed: () => _connect(context, ref, a), icon: const Icon(Icons.qr_code, size: 18), label: const Text('Connect')),
                            if (a.status == 'Connected')
                              OutlinedButton(onPressed: () => _disconnect(context, ref, a), child: const Text('Disconnect')),
                            OutlinedButton.icon(onPressed: () => _antiBan(context, ref, a), icon: const Icon(Icons.shield_outlined, size: 18), label: const Text('Anti-ban')),
                            if (!a.isDefault)
                              OutlinedButton.icon(onPressed: () => _makeDefault(context, ref, a), icon: const Icon(Icons.star_outline, size: 18), label: const Text('Make default')),
                          ]),
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

/// Shows the WhatsApp QR as a scannable image, polling the API until the
/// account links (then auto-closes). The QR arrives asynchronously from the
/// bridge, so we poll /qr for the PNG and /status for "Connected".
class _QrDialog extends ConsumerStatefulWidget {
  final int accountId;
  final String displayName;
  const _QrDialog({required this.accountId, required this.displayName});
  @override
  ConsumerState<_QrDialog> createState() => _QrDialogState();
}

class _QrDialogState extends ConsumerState<_QrDialog> {
  Timer? _timer;
  String? _qrPng;
  String _status = 'Starting session…';

  @override
  void initState() {
    super.initState();
    _poll();
    _timer = Timer.periodic(const Duration(seconds: 2), (_) => _poll());
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _poll() async {
    try {
      final acc = await ref.read(apiProvider).get('/api/whatsapp-accounts/${widget.accountId}');
      if ((acc['status']?.toString() ?? '') == 'Connected') {
        _timer?.cancel();
        if (mounted) {
          showOk(context, 'WhatsApp connected!');
          Navigator.of(context).pop();
        }
        return;
      }
      final qr = await ref.read(apiProvider).get('/api/whatsapp-accounts/${widget.accountId}/qr');
      final png = qr['qrPngBase64']?.toString();
      if (mounted) {
        setState(() {
          _qrPng = (png != null && png.isNotEmpty) ? png : _qrPng;
          _status = _qrPng == null ? 'Waiting for QR…' : '';
        });
      }
    } catch (_) {
      // QR not ready yet (404) — keep polling.
      if (mounted && _qrPng == null) setState(() => _status = 'Waiting for QR…');
    }
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: Text('Scan QR — ${widget.displayName}'),
      content: Column(mainAxisSize: MainAxisSize.min, children: [
        if (_qrPng != null)
          Image.memory(base64Decode(_qrPng!), width: 240, height: 240, gaplessPlayback: true)
        else
          SizedBox(
            width: 240, height: 240,
            child: Center(
              child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
                const CircularProgressIndicator(),
                const SizedBox(height: 14),
                Text(_status, style: const TextStyle(color: Color(0xFF6B7A90))),
              ]),
            ),
          ),
        const SizedBox(height: 12),
        const Text('Open WhatsApp → Linked devices → Link a device',
            textAlign: TextAlign.center, style: TextStyle(fontSize: 12, color: Color(0xFF6B7A90))),
      ]),
      actions: [TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Close'))],
    );
  }
}
