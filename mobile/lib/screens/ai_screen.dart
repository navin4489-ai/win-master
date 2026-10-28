import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../data/models.dart';
import '../widgets/ui.dart';

class AiScreen extends ConsumerStatefulWidget {
  const AiScreen({super.key});
  @override
  ConsumerState<AiScreen> createState() => _AiScreenState();
}

class _AiScreenState extends ConsumerState<AiScreen> {
  List<AiProviderInfo> _providers = [];
  AiProviderInfo? _current;
  String _provider = 'Local';
  String _model = 'local-generator';
  final _apiKey = TextEditingController();
  final _systemPrompt = TextEditingController();
  bool _active = true;
  String? _maskedKey;

  final _testMsg = TextEditingController(text: 'Hi, what are your prices?');
  final _genPrompt = TextEditingController();
  String _reply = '';
  String _generated = '';
  bool _busy = false;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _apiKey.dispose();
    _systemPrompt.dispose();
    _testMsg.dispose();
    _genPrompt.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final p = await ref.read(apiProvider).get('/api/ai-settings/providers');
      final s = await ref.read(apiProvider).get('/api/ai-settings');
      final settings = AiSettings.fromJson(s);
      setState(() {
        _providers = (p['providers'] as List).map((e) => AiProviderInfo.fromJson(e)).toList();
        _provider = settings.provider ?? 'Local';
        _model = settings.model;
        _systemPrompt.text = settings.systemPrompt;
        _active = settings.isActive;
        _maskedKey = settings.maskedApiKey;
        _syncCurrent();
        _loading = false;
      });
    } catch (e) {
      setState(() => _loading = false);
      if (mounted) showError(context, e);
    }
  }

  void _syncCurrent() {
    _current = _providers.where((p) => p.id == _provider).firstOrNull;
    if (_current != null && !_current!.models.contains(_model)) {
      _model = _current!.models.first;
    }
  }

  Future<void> _save() async {
    try {
      await ref.read(apiProvider).put('/api/ai-settings', body: {
        'provider': _provider, 'apiKey': _apiKey.text.isEmpty ? null : _apiKey.text,
        'model': _model, 'systemPrompt': _systemPrompt.text, 'isActive': _active, 'dailyTokenBudget': 100000,
      });
      _apiKey.clear();
      if (mounted) showOk(context, 'AI settings saved');
      _load();
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  Future<void> _test() async {
    setState(() => _busy = true);
    try {
      final r = await ref.read(apiProvider).post('/api/ai-settings/test', body: {'message': _testMsg.text});
      setState(() => _reply = r['reply'] ?? '');
    } catch (e) {
      if (mounted) showError(context, e);
    } finally {
      setState(() => _busy = false);
    }
  }

  Future<void> _generate() async {
    if (_genPrompt.text.trim().isEmpty) {
      showError(context, 'Describe what the message is about');
      return;
    }
    setState(() => _busy = true);
    try {
      final r = await ref.read(apiProvider).post('/api/ai-settings/generate', body: {
        'prompt': _genPrompt.text, 'tone': 'Friendly', 'goal': 'Drive sales',
        'includeSpintax': true, 'includeVariables': true,
      });
      setState(() => _generated = r['message'] ?? '');
    } catch (e) {
      if (mounted) showError(context, e);
    } finally {
      setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('AI Chatbot & Generator')),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : ListView(padding: const EdgeInsets.all(16), children: [
              const Text('Provider', style: TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 8),
              DropdownButtonFormField<String>(
                initialValue: _provider,
                decoration: const InputDecoration(labelText: 'AI Provider'),
                items: _providers.map((p) => DropdownMenuItem(value: p.id, child: Text('${p.name}${p.free ? ' · FREE' : ''}'))).toList(),
                onChanged: (v) => setState(() { _provider = v ?? 'Local'; _syncCurrent(); }),
              ),
              const SizedBox(height: 10),
              if (_current != null) ...[
                DropdownButtonFormField<String>(
                  initialValue: _current!.models.contains(_model) ? _model : _current!.models.first,
                  decoration: const InputDecoration(labelText: 'Model'),
                  items: _current!.models.map((m) => DropdownMenuItem(value: m, child: Text(m))).toList(),
                  onChanged: (v) => setState(() => _model = v ?? _model),
                ),
                const SizedBox(height: 10),
                if (_current!.needsKey)
                  TextField(controller: _apiKey, obscureText: true, decoration: InputDecoration(labelText: 'API key', hintText: _maskedKey ?? 'paste key'))
                else
                  const Chip(label: Text('No API key required'), backgroundColor: Color(0xFFDBEAFE)),
              ],
              const SizedBox(height: 10),
              TextField(controller: _systemPrompt, maxLines: 2, decoration: const InputDecoration(labelText: 'Chatbot personality')),
              SwitchListTile(value: _active, onChanged: (v) => setState(() => _active = v), title: const Text('Enable AI auto-replies'), contentPadding: EdgeInsets.zero),
              FilledButton(onPressed: _save, child: const Text('Save')),
              const Divider(height: 32),
              const Text('Test chatbot', style: TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 8),
              TextField(controller: _testMsg, decoration: const InputDecoration(labelText: 'Incoming message')),
              const SizedBox(height: 8),
              OutlinedButton(onPressed: _busy ? null : _test, child: Text(_busy ? 'Thinking…' : 'Get AI reply')),
              if (_reply.isNotEmpty)
                Container(margin: const EdgeInsets.only(top: 10), padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(color: const Color(0xFFF6F8FB), borderRadius: BorderRadius.circular(10)),
                    child: Text(_reply)),
              const Divider(height: 32),
              const Text('Generate a campaign message', style: TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 8),
              TextField(controller: _genPrompt, decoration: const InputDecoration(labelText: 'What is it about?', hintText: 'e.g. 20% off weekend sale')),
              const SizedBox(height: 8),
              FilledButton.icon(onPressed: _busy ? null : _generate, icon: const Icon(Icons.auto_awesome), label: Text(_busy ? 'Generating…' : 'Generate')),
              if (_generated.isNotEmpty) ...[
                Container(margin: const EdgeInsets.only(top: 10), padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(color: const Color(0xFFF6F8FB), borderRadius: BorderRadius.circular(10)),
                    child: Text(_generated)),
              ],
            ]),
    );
  }
}
