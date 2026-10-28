import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';
import 'campaigns_screen.dart';

class CampaignCreateScreen extends ConsumerStatefulWidget {
  const CampaignCreateScreen({super.key});
  @override
  ConsumerState<CampaignCreateScreen> createState() => _CampaignCreateScreenState();
}

class _CampaignCreateScreenState extends ConsumerState<CampaignCreateScreen> {
  final _name = TextEditingController();
  final _body = TextEditingController();
  List<ContactGroup> _groups = [];
  List<Template> _templates = [];
  int? _groupId;
  int? _templateId;
  String _engine = 'Baileys';
  bool _spintax = true;
  bool _saving = false;
  bool _aiBusy = false;
  int _minDelay = 5, _maxDelay = 15, _batch = 50, _cooldown = 5;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _name.dispose();
    _body.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final g = await ref.read(apiProvider).get('/api/contact-groups', query: {'pageSize': 100});
      final t = await ref.read(apiProvider).get('/api/templates') as List;
      setState(() {
        _groups = Paged.from(g, ContactGroup.fromJson).items;
        _templates = t.map((e) => Template.fromJson(e)).toList();
        if (_groups.isNotEmpty) _groupId = _groups.first.id;
      });
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  Future<void> _aiGenerate() async {
    final topic = _body.text.trim().isNotEmpty ? _body.text.trim() : _name.text.trim();
    if (topic.isEmpty) {
      showError(context, 'Enter a name or topic first');
      return;
    }
    setState(() => _aiBusy = true);
    try {
      final r = await ref.read(apiProvider).post('/api/ai-settings/generate', body: {
        'prompt': topic, 'tone': 'Friendly', 'goal': 'Drive sales',
        'includeSpintax': _spintax, 'includeVariables': true,
      });
      setState(() => _body.text = r['message'] ?? '');
      if (mounted) showOk(context, 'AI message generated');
    } catch (e) {
      if (mounted) showError(context, e);
    } finally {
      setState(() => _aiBusy = false);
    }
  }

  Future<void> _save() async {
    if (_name.text.trim().isEmpty || _groupId == null) {
      showError(context, 'Name and group are required');
      return;
    }
    setState(() => _saving = true);
    try {
      await ref.read(apiProvider).post('/api/campaigns', body: {
        'name': _name.text.trim(),
        'templateId': _templateId,
        'body': _templateId == null ? _body.text : null,
        'groupId': _groupId,
        'engineType': _engine,
        'accountId': null,
        'rotationMode': 'RoundRobin',
        'scheduledAt': null,
        'spintaxEnabled': _spintax,
        'personalizeMessage': true,
        'pacing': {'minDelaySec': _minDelay, 'maxDelaySec': _maxDelay, 'batchSize': _batch, 'cooldownMinutes': _cooldown},
      });
      ref.invalidate(campaignsProvider);
      if (mounted) {
        showOk(context, 'Campaign created');
        Navigator.pop(context);
      }
    } catch (e) {
      if (mounted) showError(context, e);
    } finally {
      setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('New Campaign')),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        TextField(controller: _name, decoration: const InputDecoration(labelText: 'Campaign name')),
        const SizedBox(height: 14),
        DropdownButtonFormField<int>(
          initialValue: _groupId,
          decoration: const InputDecoration(labelText: 'Contact group'),
          items: _groups.map((g) => DropdownMenuItem(value: g.id, child: Text('${g.name} (${g.totalContacts})'))).toList(),
          onChanged: (v) => setState(() => _groupId = v),
        ),
        const SizedBox(height: 14),
        DropdownButtonFormField<int?>(
          initialValue: _templateId,
          decoration: const InputDecoration(labelText: 'Template'),
          items: [
            const DropdownMenuItem(value: null, child: Text('— inline message —')),
            ..._templates.map((t) => DropdownMenuItem(value: t.id, child: Text(t.name))),
          ],
          onChanged: (v) => setState(() => _templateId = v),
        ),
        if (_templateId == null) ...[
          const SizedBox(height: 14),
          Row(children: [
            const Text('Message', style: TextStyle(fontWeight: FontWeight.w600)),
            const Spacer(),
            TextButton.icon(
              onPressed: _aiBusy ? null : _aiGenerate,
              icon: _aiBusy
                  ? const SizedBox(width: 14, height: 14, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.auto_awesome, size: 18),
              label: const Text('AI generate'),
            ),
          ]),
          TextField(
            controller: _body,
            maxLines: 4,
            decoration: const InputDecoration(hintText: '{Hi|Hello} {{Name}}, ...'),
          ),
        ],
        const SizedBox(height: 14),
        DropdownButtonFormField<String>(
          initialValue: _engine,
          decoration: const InputDecoration(labelText: 'Engine'),
          items: const [DropdownMenuItem(value: 'Baileys', child: Text('Baileys')), DropdownMenuItem(value: 'CloudApi', child: Text('Cloud API'))],
          onChanged: (v) => setState(() => _engine = v ?? 'Baileys'),
        ),
        const SizedBox(height: 8),
        SwitchListTile(
          value: _spintax,
          onChanged: (v) => setState(() => _spintax = v),
          title: const Text('Spintax variation'),
          contentPadding: EdgeInsets.zero,
        ),
        const Divider(),
        const Text('Anti-ban pacing', style: TextStyle(fontWeight: FontWeight.w700)),
        _slider('Min delay (s)', _minDelay, 1, 60, (v) => setState(() => _minDelay = v)),
        _slider('Max delay (s)', _maxDelay, 1, 120, (v) => setState(() => _maxDelay = v)),
        _slider('Batch size', _batch, 10, 500, (v) => setState(() => _batch = v)),
        _slider('Cooldown (min)', _cooldown, 0, 60, (v) => setState(() => _cooldown = v)),
        const SizedBox(height: 16),
        FilledButton(onPressed: _saving ? null : _save, child: Text(_saving ? 'Creating…' : 'Create campaign')),
      ]),
    );
  }

  Widget _slider(String label, int value, int min, int max, ValueChanged<int> onChanged) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('$label: $value', style: const TextStyle(fontSize: 13, color: Color(0xFF6B7A90))),
          Slider(value: value.toDouble().clamp(min.toDouble(), max.toDouble()), min: min.toDouble(), max: max.toDouble(),
              divisions: (max - min), label: '$value', onChanged: (v) => onChanged(v.round())),
        ],
      );
}
