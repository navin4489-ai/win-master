import 'package:flutter/material.dart';
import '../core/config.dart';

class StatusChip extends StatelessWidget {
  final String status;
  const StatusChip(this.status, {super.key});
  @override
  Widget build(BuildContext context) {
    final c = statusColor(status);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      decoration: BoxDecoration(color: c.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(999)),
      child: Text(status, style: TextStyle(color: c, fontWeight: FontWeight.w700, fontSize: 12)),
    );
  }
}

class StatCard extends StatelessWidget {
  final String label, value;
  final String? sub;
  final IconData icon;
  const StatCard({super.key, required this.label, required this.value, required this.icon, this.sub});
  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Icon(icon, size: 18, color: AppTheme.primary),
            const SizedBox(width: 8),
            Expanded(child: Text(label.toUpperCase(),
                style: const TextStyle(fontSize: 11, color: Color(0xFF6B7A90), fontWeight: FontWeight.w600, letterSpacing: .4))),
          ]),
          const SizedBox(height: 10),
          Text(value, style: const TextStyle(fontSize: 26, fontWeight: FontWeight.w800)),
          if (sub != null) Padding(padding: const EdgeInsets.only(top: 2),
              child: Text(sub!, style: const TextStyle(fontSize: 12, color: Color(0xFF6B7A90)))),
        ]),
      ),
    );
  }
}

class EmptyState extends StatelessWidget {
  final String text;
  final IconData icon;
  const EmptyState(this.text, {super.key, this.icon = Icons.inbox_outlined});
  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(40),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Icon(icon, size: 44, color: const Color(0xFFB4C0CF)),
            const SizedBox(height: 12),
            Text(text, style: const TextStyle(color: Color(0xFF6B7A90))),
          ]),
        ),
      );
}

void showError(BuildContext context, Object e) {
  ScaffoldMessenger.of(context).showSnackBar(
    SnackBar(content: Text(e.toString()), backgroundColor: const Color(0xFFE11D48)),
  );
}

void showOk(BuildContext context, String msg) {
  ScaffoldMessenger.of(context).showSnackBar(
    SnackBar(content: Text(msg), backgroundColor: AppTheme.primary),
  );
}
