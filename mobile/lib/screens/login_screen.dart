import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../data/providers.dart';
import '../widgets/logo.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});
  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _email = TextEditingController();
  final _password = TextEditingController();

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final ok = await ref.read(authProvider.notifier).login(_email.text.trim(), _password.text);
    if (!ok && mounted) {
      final err = ref.read(authProvider).error ?? 'Login failed';
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(err), backgroundColor: const Color(0xFFE11D48)),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(authProvider);
    return Scaffold(
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            const SizedBox(height: 40),
            const Center(child: WinMasterLogo(size: 76)),
            const SizedBox(height: 16),
            const Center(child: WinMasterWordmark(logoSize: 34)),
            const SizedBox(height: 4),
            const Center(child: Text('WhatsApp marketing console', style: TextStyle(color: Color(0xFF6B7A90)))),
            const SizedBox(height: 32),
            const Text('Email', style: TextStyle(fontWeight: FontWeight.w600, fontSize: 13)),
            const SizedBox(height: 6),
            TextField(controller: _email, keyboardType: TextInputType.emailAddress,
                decoration: const InputDecoration(hintText: 'you@company.com')),
            const SizedBox(height: 16),
            const Text('Password', style: TextStyle(fontWeight: FontWeight.w600, fontSize: 13)),
            const SizedBox(height: 6),
            TextField(controller: _password, obscureText: true, onSubmitted: (_) => _submit(),
                decoration: const InputDecoration(hintText: '••••••••')),
            const SizedBox(height: 24),
            FilledButton(
              onPressed: state.loading ? null : _submit,
              child: state.loading
                  ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2))
                  : const Text('Sign in'),
            ),
          ]),
        ),
      ),
    );
  }
}
